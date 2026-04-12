using System.Text.Json;
using System.Text.Json.Serialization;
using InertiaCore.Extensions;
using InertiaCore.Models;
using InertiaCore.Props;
using InertiaCore.Utils;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace InertiaCore;

public class Response : IActionResult
{
    private readonly string _component;
    private readonly Dictionary<string, object?> _props;
    private readonly string _rootView;
    private readonly string? _version;
    private readonly bool _encryptHistory;
    private readonly bool _clearHistory;

    private ActionContext? _context;
    private Page? _page;
    private IDictionary<string, object>? _viewData;

    internal Response(string component, Dictionary<string, object?> props, string rootView, string? version, bool encryptHistory, bool clearHistory)
        => (_component, _props, _rootView, _version, _encryptHistory, _clearHistory) = (component, props, rootView, version, encryptHistory, clearHistory);

    public async Task ExecuteResultAsync(ActionContext context)
    {
        SetContext(context);
        await ProcessResponse();
        await GetResult().ExecuteResultAsync(_context!);
    }

    protected internal async Task ProcessResponse()
    {
        var props = _props;

        props = ResolveSharedProps(props);
        props = ResolvePartialProperties(props);
        props = ResolveOnceProperties(props);
        props = ResolveAlways(props);

        // Build the once-props metadata from the merged dictionary BEFORE the
        // IOnceable instances are replaced with their resolved values below.
        // This ensures shared once props (registered via Share()/ShareOnce())
        // are included in the metadata, matching the behavior of the Laravel
        // adapter's resolveOnceProps().
        var onceProps = ResolveOnceProps(props);

        props = await ResolvePropertyInstances(props);

        var page = new Page
        {
            Component = _component,
            Version = _version,
            Url = _context!.RequestedUri(),
            Props = props,
            EncryptHistory = _encryptHistory,
            ClearHistory = _clearHistory,
            OnceProps = onceProps,
        };

        page.Props["errors"] = GetErrors();

        SetPage(page);
    }

    /// <summary>
    /// Resolve `shared` props stored in the current request context.
    /// </summary>
    private Dictionary<string, object?> ResolveSharedProps(Dictionary<string, object?> props)
    {
        var shared = _context!.HttpContext.Features.Get<InertiaSharedProps>();
        if (shared != null)
            props = shared.GetMerged(props);

        return props;
    }

    /// <summary>
    /// Resolve the `only` and `except` partial request props.
    /// </summary>
    private Dictionary<string, object?> ResolvePartialProperties(Dictionary<string, object?> props)
    {
        var isPartial = _context!.IsInertiaPartialComponent(_component);

        if (!isPartial)
            return props
                .Where(kv => kv.Value is not LazyProp)
                .ToDictionary(kv => kv.Key, kv => kv.Value);

        props = props.ToDictionary(kv => kv.Key, kv => kv.Value);

        if (_context!.HttpContext.Request.Headers.ContainsKey(InertiaHeader.PartialOnly))
            props = ResolveOnly(props);

        if (_context!.HttpContext.Request.Headers.ContainsKey(InertiaHeader.PartialExcept))
            props = ResolveExcept(props);

        return props;
    }

    /// <summary>
    /// Resolve the `only` partial request props.
    /// </summary>
    private Dictionary<string, object?> ResolveOnly(Dictionary<string, object?> props)
    {
        var onlyKeys = _context!.HttpContext.Request.Headers[InertiaHeader.PartialOnly]
            .ToString().Split(',')
            .Select(k => k.Trim())
            .Where(k => !string.IsNullOrEmpty(k))
            .ToList();

        return props.Where(kv => onlyKeys.Contains(kv.Key, StringComparer.OrdinalIgnoreCase))
            .ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    /// <summary>
    /// Resolve the `except` partial request props.
    /// </summary>
    private Dictionary<string, object?> ResolveExcept(Dictionary<string, object?> props)
    {
        var exceptKeys = _context!.HttpContext.Request.Headers[InertiaHeader.PartialExcept]
            .ToString().Split(',')
            .Select(k => k.Trim())
            .Where(k => !string.IsNullOrEmpty(k))
            .ToList();

        return props.Where(kv => exceptKeys.Contains(kv.Key, StringComparer.OrdinalIgnoreCase) == false)
            .ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    /// <summary>
    /// Resolve `always` properties that should always be included on all visits, regardless of "only" or "except" requests.
    /// </summary>
    private Dictionary<string, object?> ResolveAlways(Dictionary<string, object?> props)
    {
        var alwaysProps = _props.Where(o => o.Value is AlwaysProp);

        return props
            .Where(kv => kv.Value is not AlwaysProp)
            .Concat(alwaysProps).ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    /// <summary>
    /// Filter out once props that have already been loaded by the client.
    /// </summary>
    private Dictionary<string, object?> ResolveOnceProperties(Dictionary<string, object?> props)
    {
        if (!_context!.HttpContext.IsInertiaRequest() || _context!.IsInertiaPartialComponent(_component))
            return props;

        var header = _context!.HttpContext.Request.Headers[InertiaHeader.ExceptOnceProps].ToString();
        if (string.IsNullOrEmpty(header)) return props;

        var exceptOnceProps = header.Split(',').Select(k => k.Trim()).Where(k => !string.IsNullOrEmpty(k)).ToList();
        if (exceptOnceProps.Count == 0) return props;

        return props.Where(kv =>
        {
            if (kv.Value is not IOnceable onceable) return true;
            if (!onceable.ShouldResolveOnce()) return true;
            if (onceable.ShouldBeRefreshed()) return true;
            return !exceptOnceProps.Contains(onceable.GetOnceKey() ?? kv.Key, StringComparer.OrdinalIgnoreCase);
        }).ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    /// <summary>
    /// Build metadata for once props included in the response.
    /// </summary>
    private Dictionary<string, object>? ResolveOnceProps(Dictionary<string, object?> props)
    {
        var onlyProps = _context!.HttpContext.Request.Headers[InertiaHeader.PartialOnly]
            .ToString().Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var exceptProps = new HashSet<string>(
            _context!.HttpContext.Request.Headers[InertiaHeader.PartialExcept]
                .ToString().Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim()), StringComparer.OrdinalIgnoreCase);

        var onceProps = props
            .Where(kv => kv.Value is IOnceable onceable && onceable.ShouldResolveOnce())
            .Where(kv => onlyProps.Count == 0 || onlyProps.Contains(kv.Key))
            .Where(kv => !exceptProps.Contains(kv.Key))
            .ToDictionary(
                kv => ((IOnceable)kv.Value!).GetOnceKey() ?? kv.Key.ToCamelCase(),
                kv => (object)new Dictionary<string, object?> {
                    { "prop", kv.Key.ToCamelCase() },
                    { "expiresAt", ((IOnceable)kv.Value!).ExpiresAt() }
                });

        return onceProps.Count == 0 ? null : onceProps;
    }

    /// <summary>
    /// Resolve all necessary class instances in the given props.
    /// </summary>
    private static async Task<Dictionary<string, object?>> ResolvePropertyInstances(Dictionary<string, object?> props)
    {
        return (await Task.WhenAll(props.Select(async pair =>
        {
            var key = pair.Key.ToCamelCase();

            var value = pair.Value switch
            {
                Func<object?> f => (key, await f.ResolveAsync()),
                Task t => (key, await t.ResolveResult()),
                InvokableProp p => (key, await p.Invoke()),
                _ => (key, pair.Value)
            };

            if (value.Item2 is Dictionary<string, object?> dict)
            {
                value = (key, await ResolvePropertyInstances(dict));
            }

            return value;
        }))).ToDictionary(pair => pair.key, pair => pair.Item2);
    }

    protected internal JsonResult GetJson()
    {
        _context!.HttpContext.Response.Headers.Override(InertiaHeader.Inertia, "true");
        _context!.HttpContext.Response.Headers.Override("Vary", InertiaHeader.Inertia);
        _context!.HttpContext.Response.StatusCode = 200;

        return new JsonResult(_page, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            ReferenceHandler = ReferenceHandler.IgnoreCycles
        });
    }

    private ViewResult GetView()
    {
        var viewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), _context!.ModelState)
        {
            Model = _page
        };

        if (_viewData == null) return new ViewResult { ViewName = _rootView, ViewData = viewData };

        foreach (var (key, value) in _viewData)
            viewData[key] = value;

        return new ViewResult { ViewName = _rootView, ViewData = viewData };
    }

    protected internal IActionResult GetResult() => _context!.IsInertiaRequest() ? GetJson() : GetView();

    private Dictionary<string, string> GetErrors()
    {
        if (!_context!.ModelState.IsValid)
            return _context!.ModelState.ToDictionary(o => o.Key.ToCamelCase(),
                o => o.Value?.Errors.FirstOrDefault()?.ErrorMessage ?? "");

        return new Dictionary<string, string>(0);
    }

    protected internal void SetContext(ActionContext context) => _context = context;

    private void SetPage(Page page) => _page = page;

    public Response WithViewData(IDictionary<string, object> viewData)
    {
        _viewData = viewData;
        return this;
    }
}
