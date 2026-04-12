using InertiaCore.Extensions;
using InertiaCore.Props;
using InertiaCore.Utils;
using Microsoft.AspNetCore.Http;

namespace InertiaCore;

/// <summary>
/// Walks the prop tree in a single pass, applying partial filtering and
/// exclusion rules while collecting all page-level metadata (mergeProps,
/// deferredProps, scrollProps, onceProps, sharedProps...). Ports the
/// canonical Laravel 3.x implementation at
/// <c>inertia-laravel/src/PropsResolver.php</c>.
///
/// Recursion means nested prop types get the same pipeline treatment as
/// top-level props: a <see cref="MergeProp"/> or <see cref="OnceProp"/>
/// nested at <c>data.users</c> now contributes metadata with the correct
/// dot-path, and a top-level dot-notation key such as
/// <c>["user.profile.name"] = "Alice"</c> is unpacked into
/// <c>{user: {profile: {name: "Alice"}}}</c>.
/// </summary>
internal sealed class PropsResolver
{
    private readonly HttpRequest _request;
    private readonly string _component;

    private readonly bool _isPartial;
    private readonly bool _isInertia;
    private readonly List<string>? _only;
    private readonly List<string>? _except;
    private readonly List<string> _resetProps;
    private readonly List<string> _loadedOnceProps;

    private readonly Dictionary<string, List<string>> _deferredProps = new();
    private readonly List<string> _mergeProps = new();
    private readonly List<string> _prependProps = new();
    private readonly List<string> _deepMergeProps = new();
    private readonly List<string> _matchPropsOn = new();
    private readonly Dictionary<string, object> _scrollProps = new();
    private readonly Dictionary<string, object> _onceProps = new();
    private readonly List<string> _sharedPropKeys = new();

    public PropsResolver(HttpRequest request, string component)
    {
        _request = request;
        _component = component;

        _isPartial = request.Headers[InertiaHeader.PartialComponent].ToString() == component;
        _isInertia = bool.TryParse(request.Headers[InertiaHeader.Inertia], out _);
        _only = ParseHeader(InertiaHeader.PartialOnly);
        _except = ParseHeader(InertiaHeader.PartialExcept);
        _resetProps = ParseHeader(InertiaHeader.Reset) ?? new List<string>();
        _loadedOnceProps = ParseHeader(InertiaHeader.ExceptOnceProps) ?? new List<string>();
    }

    /// <summary>
    /// Resolve the given shared and page props, collecting their metadata.
    /// Mirrors <c>PropsResolver::resolve()</c> in Laravel.
    /// </summary>
    public async Task<(Dictionary<string, object?> Props, PropsMetadata Metadata)> Resolve(
        IReadOnlyDictionary<string, object?> shared,
        Dictionary<string, object?> props)
    {
        var merged = new Dictionary<string, object?>();
        foreach (var kv in ResolveSharedProps(shared))
            merged[kv.Key] = kv.Value;
        foreach (var kv in props)
            merged[kv.Key] = kv.Value;

        merged = UnpackDotProps(merged);
        var resolved = await ResolveProps(merged);
        return (resolved, BuildMetadata());
    }

    /// <summary>
    /// Resolve shared property providers and collect top-level shared prop keys.
    /// </summary>
    private Dictionary<string, object?> ResolveSharedProps(IReadOnlyDictionary<string, object?> shared)
    {
        var sharedDict = new Dictionary<string, object?>();
        foreach (var kv in shared)
            sharedDict[kv.Key] = kv.Value;

        var resolved = ResolvePropertyProviders(sharedDict);

        foreach (var key in resolved.Keys)
        {
            var normalized = key.ToCamelCase();
            var topLevel = normalized.Contains('.')
                ? normalized.Substring(0, normalized.IndexOf('.'))
                : normalized;
            if (!_sharedPropKeys.Contains(topLevel))
                _sharedPropKeys.Add(topLevel);
        }

        return resolved;
    }

    /// <summary>
    /// Resolve <see cref="ProvidesInertiaProperties"/> instances into keyed props.
    /// Unlike Laravel (which requires a numeric key), InertiaCore replaces the
    /// wrapping key with the provided properties to preserve existing behavior.
    /// </summary>
    private Dictionary<string, object?> ResolvePropertyProviders(Dictionary<string, object?> props)
    {
        RenderContext? context = null;
        var result = new Dictionary<string, object?>();

        foreach (var kv in props)
        {
            if (kv.Value is ProvidesInertiaProperties provider)
            {
                context ??= new RenderContext(_component, _request);
                foreach (var provided in provider.ToInertiaProperties(context))
                    result[provided.Key] = provided.Value;
            }
            else
            {
                result[kv.Key] = kv.Value;
            }
        }

        return result;
    }

    /// <summary>
    /// Build the non-empty metadata collections for the page response.
    /// </summary>
    private PropsMetadata BuildMetadata() => new()
    {
        SharedProps = _sharedPropKeys.Count > 0 ? _sharedPropKeys : null,
        MergeProps = _mergeProps.Count > 0 ? _mergeProps : null,
        PrependProps = _prependProps.Count > 0 ? _prependProps : null,
        DeepMergeProps = _deepMergeProps.Count > 0 ? _deepMergeProps : null,
        MatchPropsOn = _matchPropsOn.Count > 0 ? _matchPropsOn : null,
        DeferredProps = _deferredProps.Count > 0 ? _deferredProps : null,
        ScrollProps = _scrollProps.Count > 0 ? _scrollProps : null,
        OnceProps = _onceProps.Count > 0 ? _onceProps : null,
    };

    /// <summary>
    /// Recursively resolve the props tree, collecting metadata along the way.
    /// </summary>
    private async Task<Dictionary<string, object?>> ResolveProps(
        Dictionary<string, object?> props,
        string prefix = "",
        bool parentWasResolved = false)
    {
        props = ResolvePropertyProviders(props);
        var result = new Dictionary<string, object?>();

        foreach (var kv in props)
        {
            var key = kv.Key.ToCamelCase();
            var path = prefix.Length == 0 ? key : $"{prefix}.{key}";
            var prop = kv.Value;

            // On partial requests, we only include props that match the paths
            // specified in the request headers. AlwaysProp instances and the
            // children of already-resolved values bypass this filter.
            if (!ShouldIncludeInPartialResponse(prop, path, parentWasResolved))
                continue;

            // On initial page loads, certain prop types (e.g. DeferProp,
            // OptionalProp) are excluded before resolution to avoid
            // executing their closures unnecessarily.
            if (!_isPartial && ExcludeFromInitialResponse(prop, path))
                continue;

            var value = await ResolveValue(prop, path, props);

            // A closure may return a prop type instead of a plain value. When
            // this happens, we unwrap it one more level so the prop type can
            // participate in filtering and metadata collection below.
            if (!ReferenceEquals(value, prop) && IsPropType(value))
            {
                prop = value;

                if (!_isPartial && ExcludeFromInitialResponse(prop, path))
                    continue;

                value = await ResolveValue(prop, path, props);
            }

            CollectMetadata(prop, path);

            // When the resolved value is a dictionary, we recurse into it.
            // If the original prop was not already a dictionary (e.g. a
            // closure that returned one), its children bypass partial
            // filtering, matching Laravel semantics.
            if (value is Dictionary<string, object?> dict)
            {
                result[key] = await ResolveProps(
                    dict,
                    path,
                    parentWasResolved || prop is not Dictionary<string, object?>);
            }
            else
            {
                result[key] = value;
            }
        }

        return result;
    }

    /// <summary>
    /// Determine if a prop should be included in a partial response.
    /// <see cref="AlwaysProp"/> and children of already-resolved values bypass
    /// partial filtering.
    /// </summary>
    private bool ShouldIncludeInPartialResponse(object? prop, string path, bool parentWasResolved)
    {
        if (!_isPartial || prop is AlwaysProp || parentWasResolved)
            return true;

        return PathMatchesPartialRequest(path);
    }

    /// <summary>
    /// Bidirectional prefix match on the only/except partial headers.
    /// </summary>
    private bool PathMatchesPartialRequest(string path)
    {
        if (_only != null && !MatchesOnly(path) && !LeadsToOnly(path))
            return false;

        if (_except != null && MatchesExcept(path))
            return false;

        return true;
    }

    /// <summary>
    /// Determine if a prop should be excluded from the initial page response.
    /// Each exclusion type collects its metadata before the prop is removed.
    /// </summary>
    private bool ExcludeFromInitialResponse(object? prop, string path)
    {
        if (prop is IIgnoresFirstLoad)
            return ExcludeIgnoredProp(prop, path);

        if (prop is ScrollProp scroll && scroll.ShouldDefer())
            return ExcludeDeferredProp(scroll, path);

        if (_isInertia && WasAlreadyLoadedByClient(prop, path))
            return ExcludeAlreadyLoadedProp(prop, path);

        return false;
    }

    private bool ExcludeIgnoredProp(object? prop, string path)
    {
        // DeferProp / ScrollProp (both Deferrable) may contribute a defer group.
        if (prop is DeferProp deferProp && !WasAlreadyLoadedByClient(prop, path))
        {
            CollectDeferredPropMetadata(path, deferProp.Group());
        }
        else if (prop is ScrollProp scrollProp && scrollProp.ShouldDefer()
            && !WasAlreadyLoadedByClient(prop, path))
        {
            CollectDeferredPropMetadata(path, scrollProp.Group());
        }

        if (prop is Mergeable mergeable && mergeable.ShouldMerge())
        {
            // Scroll merge intent is normally configured during resolveValue,
            // but excluded scroll props never get resolved. Configure it here
            // so AppendsAtPaths is populated before metadata collection.
            if (prop is ScrollProp excludedScroll)
                excludedScroll.ConfigureMergeIntent(_request);

            CollectMergeableMetadata(path, mergeable);
        }

        if (prop is IOnceable once && once.ShouldResolveOnce())
            CollectOnceMetadata(path, once);

        return true;
    }

    private bool ExcludeDeferredProp(ScrollProp prop, string path)
    {
        CollectDeferredPropMetadata(path, prop.Group());

        if (((Mergeable)prop).ShouldMerge())
        {
            prop.ConfigureMergeIntent(_request);
            CollectMergeableMetadata(path, prop);
        }

        return true;
    }

    private bool ExcludeAlreadyLoadedProp(object? prop, string path)
    {
        if (prop is IOnceable once)
            CollectOnceMetadata(path, once);
        return true;
    }

    private bool WasAlreadyLoadedByClient(object? prop, string path)
    {
        if (prop is not IOnceable once)
            return false;
        if (!once.ShouldResolveOnce() || once.ShouldBeRefreshed())
            return false;

        var key = once.GetOnceKey() ?? path;
        return _loadedOnceProps.Contains(key, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Resolve a single prop value through the resolution pipeline.
    /// </summary>
    private async Task<object?> ResolveValue(object? value, string path, Dictionary<string, object?> siblings)
    {
        if (value is ScrollProp scrollProp)
            scrollProp.ConfigureMergeIntent(_request);

        value = await ResolveCallable(value);

        if (value is ProvidesInertiaProperty pip)
            value = pip.ToInertiaProperty(new PropertyContext(path, siblings, _request));

        return value;
    }

    /// <summary>
    /// Unwrap InvokableProp / Func / Task values, mirroring the existing
    /// <c>ResolvePropertyInstances</c> behavior.
    /// </summary>
    private static async Task<object?> ResolveCallable(object? value) => value switch
    {
        InvokableProp invokable => await invokable.Invoke(),
        Func<object?> f => await f.ResolveAsync(),
        Task t => await t.ResolveResult(),
        _ => value,
    };

    /// <summary>
    /// Determine if the value is a prop type that requires further filtering
    /// or metadata collection after being unwrapped from a closure.
    /// </summary>
    private static bool IsPropType(object? value) =>
        value is AlwaysProp
        || value is DeferProp
        || value is ScrollProp
        || value is IIgnoresFirstLoad
        || value is Mergeable
        || value is IOnceable;

    private void CollectMetadata(object? prop, string path)
    {
        if (prop is Mergeable mergeable && mergeable.ShouldMerge())
            CollectMergeableMetadata(path, mergeable);

        if (prop is ScrollProp scroll)
            CollectScrollMetadata(path, scroll);

        if (prop is IOnceable once && once.ShouldResolveOnce())
            CollectOnceMetadata(path, once);
    }

    private void CollectDeferredPropMetadata(string path, string? group)
    {
        var g = group ?? "default";
        if (!_deferredProps.TryGetValue(g, out var list))
        {
            list = new List<string>();
            _deferredProps[g] = list;
        }
        list.Add(path);
    }

    private void CollectMergeableMetadata(string path, Mergeable prop)
    {
        if (PathInList(_resetProps, path))
            return;

        if (_isPartial && !IsIncludedInPartialMetadata(path))
            return;

        if (prop.ShouldDeepMerge())
        {
            _deepMergeProps.Add(path);
        }
        else if (prop.AppendsAtRoot())
        {
            _mergeProps.Add(path);
        }
        else if (prop.PrependsAtRoot())
        {
            _prependProps.Add(path);
        }
        else
        {
            foreach (var appendPath in prop.AppendsAtPaths)
                _mergeProps.Add($"{path}.{appendPath}");
            foreach (var prependPath in prop.PrependsAtPaths)
                _prependProps.Add($"{path}.{prependPath}");
        }

        var matchOn = prop.GetMatchOn();
        if (matchOn != null)
        {
            foreach (var strategy in matchOn)
                _matchPropsOn.Add($"{path}.{strategy}");
        }
    }

    private void CollectScrollMetadata(string path, ScrollProp prop)
    {
        var metadata = prop.GetMetadata();
        var entry = new Dictionary<string, object?>();
        foreach (var kv in metadata)
            entry[kv.Key] = kv.Value;
        entry["reset"] = PathInList(_resetProps, path);
        _scrollProps[path] = entry;
    }

    private void CollectOnceMetadata(string path, IOnceable prop)
    {
        if (!prop.ShouldResolveOnce())
            return;

        if (_isPartial && !IsIncludedInPartialMetadata(path))
            return;

        var key = prop.GetOnceKey() ?? path;
        _onceProps[key] = new Dictionary<string, object?>
        {
            { "prop", path },
            { "expiresAt", prop.ExpiresAt() },
        };
    }

    private bool IsIncludedInPartialMetadata(string path)
    {
        if (_only != null && !MatchesOnly(path))
            return false;
        if (_except != null && MatchesExcept(path))
            return false;
        return true;
    }

    private bool MatchesOnly(string path)
    {
        if (_only == null) return false;
        foreach (var onlyPath in _only)
        {
            if (string.Equals(path, onlyPath, StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(onlyPath + ".", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private bool LeadsToOnly(string path)
    {
        if (_only == null) return false;
        foreach (var onlyPath in _only)
        {
            if (onlyPath.StartsWith(path + ".", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private bool MatchesExcept(string path)
    {
        if (_except == null) return false;
        foreach (var exceptPath in _except)
        {
            if (string.Equals(path, exceptPath, StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(exceptPath + ".", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static bool PathInList(List<string> list, string path) =>
        list.Contains(path, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Unpack top-level dot-notation keys into nested dictionaries.
    /// Note: only string keys are supported (as required in .NET),
    /// and only top-level keys are unpacked.
    /// </summary>
    private Dictionary<string, object?> UnpackDotProps(Dictionary<string, object?> props)
    {
        var dotKeys = props.Keys.Where(k => k.Contains('.')).ToList();
        if (dotKeys.Count == 0)
            return props;

        foreach (var key in dotKeys)
        {
            var value = props[key];

            if (value is Func<object?> f)
                value = f.ResolveAsync().GetAwaiter().GetResult();

            EnsurePathIsTraversable(props, key);
            DotNotationHelper.Set(props, key, value);
            props.Remove(key);
        }

        return props;
    }

    /// <summary>
    /// Walk the intermediate segments of a dot-notation path, ensuring each
    /// exists as a traversable dictionary so <see cref="DotNotationHelper.Set"/>
    /// can nest into it without clobbering existing values.
    /// </summary>
    private static void EnsurePathIsTraversable(Dictionary<string, object?> props, string dotKey)
    {
        var segments = dotKey.Split('.');
        var current = props;
        for (var i = 0; i < segments.Length - 1; i++)
        {
            var segment = segments[i];
            if (!current.TryGetValue(segment, out var next))
                return;

            if (next is Func<object?> f)
            {
                next = f.ResolveAsync().GetAwaiter().GetResult();
                current[segment] = next;
            }

            if (next is not Dictionary<string, object?> nextDict)
                return;

            current = nextDict;
        }
    }

    /// <summary>
    /// Parse a comma-separated header value into a list, or null if absent/empty.
    /// </summary>
    private List<string>? ParseHeader(string headerName)
    {
        var raw = _request.Headers[headerName].ToString();
        if (string.IsNullOrEmpty(raw))
            return null;

        var parts = raw
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToList();

        return parts.Count > 0 ? parts : null;
    }
}
