using System.Text.Json;
using InertiaCore.Extensions;
using InertiaCore.Models;
using InertiaCore.Props;
using InertiaCore.Utils;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;

namespace InertiaCore;

public class Response : IActionResult
{
    private readonly string _component;
    private readonly Dictionary<string, object?> _props;
    private readonly string _rootView;
    private readonly string? _version;
    private readonly bool _encryptHistory;
    private readonly Func<ActionContext, string>? _urlResolver;
    private readonly IInertiaSerializer _serializer;

    private ActionContext? _context;
    private Page? _page;
    private IDictionary<string, object>? _viewData;

    internal Response(string component, Dictionary<string, object?> props, string rootView, string? version,
        bool encryptHistory, IInertiaSerializer serializer, Func<ActionContext, string>? urlResolver = null)
        => (_component, _props, _rootView, _version, _encryptHistory, _serializer, _urlResolver) = (component, props, rootView, version, encryptHistory, serializer, urlResolver);

    public async Task ExecuteResultAsync(ActionContext context)
    {
        SetContext(context);
        await ProcessResponse();
        await GetResult().ExecuteResultAsync(_context!);
    }

    protected internal async Task ProcessResponse()
    {
        var props = await ResolveProperties();

        // Pull clearHistory from session storage
        var clearHistory = false;

        try
        {
            var session = _context!.HttpContext.Session;
            if (session != null && session.TryGetValue("inertia.clear_history", out _))
            {
                clearHistory = true;
                session.Remove("inertia.clear_history");
            }
        }
        catch
        {
            // Session not available, clearHistory will remain false
        }

        var page = new Page
        {
            Component = _component,
            Version = _version,
            Url = _urlResolver?.Invoke(_context!) ?? _context!.RequestedUri(),
            Props = props,
            EncryptHistory = _encryptHistory,
            ClearHistory = clearHistory,
        };

        var mergeable = GetMergeablePropsForRequest();
        page.MergeProps = ResolveMergeProps(mergeable);
        page.PrependProps = ResolvePrependProps(mergeable);
        page.DeepMergeProps = ResolveDeepMergeProps(mergeable);
        page.MatchPropsOn = ResolveMatchPropsOn(mergeable);
        page.DeferredProps = ResolveDeferredProps(props);
        page.ScrollProps = ResolveScrollProps(props);
        page.Props["errors"] = ResolveValidationErrors();

        SetPage(page);
    }

    /// <summary>
    /// Resolve the properties for the response.
    /// </summary>
    private async Task<Dictionary<string, object?>> ResolveProperties()
    {
        var props = _props;

        ConfigureScrollProps();
        props = ResolveSharedProps(props);
        props = ResolveInertiaPropertyProviders(props);
        props = ResolvePartialProperties(props);
        props = ResolveAlways(props);
        props = await ResolvePropertyInstances(props, _context!.HttpContext.Request);

        return props;
    }

    /// <summary>
    /// Configure scroll props merge intent from the request header.
    /// </summary>
    private void ConfigureScrollProps()
    {
        var request = _context!.HttpContext.Request;
        foreach (var kv in _props)
        {
            if (kv.Value is ScrollProp scrollProp)
            {
                scrollProp.ConfigureMergeIntent(request);
            }
        }
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
    /// Resolve properties from objects implementing ProvidesInertiaProperties.
    /// </summary>
    private Dictionary<string, object?> ResolveInertiaPropertyProviders(Dictionary<string, object?> props)
    {
        var context = new RenderContext(_component, _context!.HttpContext.Request);

        foreach (var pair in props.ToList())
        {
            if (pair.Value is ProvidesInertiaProperties provider)
            {
                // Remove the provider object itself
                props.Remove(pair.Key);

                // Add the properties it provides
                var providedProps = provider.ToInertiaProperties(context);
                foreach (var providedProp in providedProps)
                {
                    props[providedProp.Key] = providedProp.Value;
                }
            }
        }

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
                .Where(kv => kv.Value is not IIgnoresFirstLoad)
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

        var result = new Dictionary<string, object?>();
        foreach (var key in onlyKeys)
        {
            if (key.Contains('.'))
            {
                var value = DotNotationHelper.Get(props, key);
                DotNotationHelper.Set(result, key, value);
            }
            else
            {
                var match = props.FirstOrDefault(kv =>
                    string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase));
                if (match.Key != null)
                    result[match.Key] = match.Value;
            }
        }
        return result;
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

        var result = props.ToDictionary(kv => kv.Key, kv => kv.Value);
        foreach (var key in exceptKeys)
        {
            if (key.Contains('.'))
            {
                DotNotationHelper.Forget(result, key);
            }
            else
            {
                var match = result.Keys.FirstOrDefault(k =>
                    string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                    result.Remove(match);
            }
        }
        return result;
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
    /// Get the Mergeable props for the current request, filtered by the Reset,
    /// Partial-Only, and Partial-Except headers. Mirrors Laravel's
    /// getMergePropsForRequest helper.
    /// </summary>
    private Dictionary<string, object?> GetMergeablePropsForRequest(bool rejectResetProps = true)
    {
        var headers = _context!.HttpContext.Request.Headers;
        var resetProps = ParseHeaderList(headers[InertiaHeader.Reset].ToString());
        var onlyProps = ParseHeaderList(headers[InertiaHeader.PartialOnly].ToString());
        var exceptProps = ParseHeaderList(headers[InertiaHeader.PartialExcept].ToString());

        var result = new Dictionary<string, object?>();

        foreach (var kv in _props)
        {
            if (kv.Value is not Mergeable m || !m.ShouldMerge()) continue;
            if (rejectResetProps && resetProps.Contains(kv.Key)) continue;
            if (onlyProps.Count > 0 && !onlyProps.Contains(kv.Key)) continue;
            if (exceptProps.Contains(kv.Key)) continue;

            result[kv.Key] = kv.Value;
        }

        return result;
    }

    private static HashSet<string> ParseHeaderList(string headerValue)
    {
        return new HashSet<string>(
            headerValue
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s)),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Resolve merge props that should be appended (excludes deep merge and prepend props).
    /// Returns a flat list of prop keys or key.path entries.
    /// </summary>
    private static List<string>? ResolveMergeProps(Dictionary<string, object?> mergeProps)
    {
        var mergeableProps = mergeProps
            .Where(kv => kv.Value is Mergeable m && !m.ShouldDeepMerge())
            .ToList();

        if (mergeableProps.Count == 0) return null;

        var result = new List<string>();

        foreach (var kv in mergeableProps)
        {
            var m = (Mergeable)kv.Value!;
            var key = kv.Key.ToCamelCase();

            if (m.AppendsAtRoot())
            {
                result.Add(key);
            }

            foreach (var path in m.AppendsAtPaths)
            {
                result.Add($"{key}.{path}");
            }
        }

        return result.Count > 0 ? result : null;
    }

    /// <summary>
    /// Resolve props that should be prepended during merging.
    /// Returns a flat list of prop keys or key.path entries.
    /// </summary>
    private static List<string>? ResolvePrependProps(Dictionary<string, object?> mergeProps)
    {
        var mergeableProps = mergeProps
            .Where(kv => kv.Value is Mergeable m && !m.ShouldDeepMerge())
            .ToList();

        if (mergeableProps.Count == 0) return null;

        var result = new List<string>();

        foreach (var kv in mergeableProps)
        {
            var m = (Mergeable)kv.Value!;
            var key = kv.Key.ToCamelCase();

            if (m.PrependsAtRoot())
            {
                result.Add(key);
            }

            foreach (var path in m.PrependsAtPaths)
            {
                result.Add($"{key}.{path}");
            }
        }

        return result.Count > 0 ? result : null;
    }

    /// <summary>
    /// Resolve props that should be deep merged.
    /// </summary>
    private static List<string>? ResolveDeepMergeProps(Dictionary<string, object?> mergeProps)
    {
        var deepMergeProps = mergeProps
            .Where(kv => kv.Value is Mergeable m && m.ShouldDeepMerge())
            .Select(kv => kv.Key.ToCamelCase())
            .ToList();

        return deepMergeProps.Count > 0 ? deepMergeProps : null;
    }

    /// <summary>
    /// Resolve the match-on keys for merge props as a flat list.
    /// Returns entries like "propKey.strategy" matching Laravel's format.
    /// </summary>
    private static List<string>? ResolveMatchPropsOn(Dictionary<string, object?> mergeProps)
    {
        var result = new List<string>();

        foreach (var kv in mergeProps)
        {
            if (kv.Value is not Mergeable m) continue;

            var matchOnKeys = m.GetMatchOn();
            if (matchOnKeys == null || matchOnKeys.Length == 0) continue;

            var key = kv.Key.ToCamelCase();
            foreach (var matchOnItem in matchOnKeys)
            {
                result.Add($"{key}.{matchOnItem}");
            }
        }

        return result.Count > 0 ? result : null;
    }

    /// <summary>
    /// Resolve scroll props metadata for the page object.
    /// </summary>
    private Dictionary<string, object>? ResolveScrollProps(Dictionary<string, object?> props)
    {
        var resetProps = new HashSet<string>(
            _context!.HttpContext.Request.Headers[InertiaHeader.Reset]
                .ToString()
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim()),
            StringComparer.OrdinalIgnoreCase);

        bool isPartial = _context!.IsInertiaPartialComponent(_component);

        var scrollProps = _props
            .Where(kv => kv.Value is ScrollProp)
            .Where(kv =>
            {
                var sp = (ScrollProp)kv.Value!;
                // On non-partial, exclude deferred scroll props
                if (!isPartial && sp.ShouldDefer()) return false;
                return true;
            })
            .ToDictionary(
                kv => kv.Key.ToCamelCase(),
                kv =>
                {
                    var sp = (ScrollProp)kv.Value!;
                    var metadata = sp.GetMetadata();
                    metadata["reset"] = resetProps.Contains(kv.Key);
                    return (object)metadata;
                });

        return scrollProps.Count == 0 ? null : scrollProps;
    }

    /// <summary>
    /// Resolve `deferred` properties that should be fetched after the initial page load.
    /// </summary>
    private Dictionary<string, List<string>>? ResolveDeferredProps(Dictionary<string, object?> props)
    {

        bool isPartial = _context!.IsInertiaPartialComponent(_component);
        if (isPartial)
        {
            return null;
        }

        var deferredProps = _props.Where(o => o.Value is DeferProp) // Filter props that are instances of DeferProp
            .Select(kv => new
            {
                Key = kv.Key,
                Group = ((DeferProp)kv.Value!).Group()
            }) // Map each prop to a new object with Key and Group

            .GroupBy(x => x.Group) // Group by 'Group'
            .ToDictionary(
                g => g.Key!,
                g => g.Select(x => x.Key.ToCamelCase()).ToList() // Extract 'Key' for each group
            );

        if (deferredProps.Count == 0)
        {
            return null;
        }

        // Return the result
        return deferredProps;
    }

    /// <summary>
    /// Resolve all necessary class instances in the given props.
    /// </summary>
    private static async Task<Dictionary<string, object?>> ResolvePropertyInstances(Dictionary<string, object?> props, HttpRequest request)
    {
        return (await Task.WhenAll(props.Select(async pair =>
        {
            var key = pair.Key.ToCamelCase();

            var value = pair.Value switch
            {
                Func<object?> f => (key, await f.ResolveAsync()),
                Task t => (key, await t.ResolveResult()),
                InvokableProp p => (key, await p.Invoke()),
                ProvidesInertiaProperty pip => (key, pip.ToInertiaProperty(new PropertyContext(key, props, request))),
                _ => (key, pair.Value)
            };

            if (value.Item2 is Dictionary<string, object?> dict)
            {
                value = (key, await ResolvePropertyInstances(dict, request));
            }

            return value;
        }))).ToDictionary(pair => pair.key, pair => pair.Item2);
    }

    protected internal JsonResult GetJson()
    {
        _context!.HttpContext.Response.Headers.Override(InertiaHeader.Inertia, "true");
        _context!.HttpContext.Response.StatusCode = 200;

        return _serializer.SerializeResult(_page);
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
        var errors = new Dictionary<string, string>();

        // First check current ModelState
        if (!_context!.ModelState.IsValid)
        {
            foreach (var kvp in _context!.ModelState)
            {
                var error = kvp.Value?.Errors.FirstOrDefault()?.ErrorMessage;
                if (!string.IsNullOrEmpty(error))
                {
                    errors[kvp.Key.ToCamelCase()] = error;
                }
            }
        }

        // Then check TempData for stored validation errors
        var requestServices = _context!.HttpContext.RequestServices;

        var tempDataFactory = requestServices.GetService<ITempDataDictionaryFactory>();
        if (tempDataFactory == null) return errors;

        var tempData = tempDataFactory.GetTempData(_context!.HttpContext);
        var storedErrors = tempData.GetAndClearValidationErrors(_context!.HttpContext.Request);

        // Merge stored errors with current errors, converting keys to camelCase
        foreach (var kvp in storedErrors)
        {
            errors[kvp.Key.ToCamelCase()] = kvp.Value;
        }

        return errors;
    }

    /// <summary>
    /// Resolves and prepares validation errors in such a way that they are easier to use client-side.
    /// Handles error bags from TempData and formats them according to Inertia specifications.
    /// Matches Laravel's error bag resolution logic.
    /// </summary>
    private object ResolveValidationErrors()
    {
        var tempData = _context!.HttpContext.GetTempData();

        // Check if there are any validation errors in TempData
        if (tempData == null || !tempData.ContainsKey("__ValidationErrors"))
        {
            // Fall back to current ModelState errors
            var modelStateErrors = GetCurrentModelStateErrors();
            if (modelStateErrors.Count == 0)
            {
                return new Dictionary<string, string>(0);
            }

            // Check for error bag header
            var errorBagHeader = _context.HttpContext.Request.Headers[InertiaHeader.ErrorBag].ToString();
            if (!string.IsNullOrEmpty(errorBagHeader))
            {
                return new Dictionary<string, object> { [errorBagHeader] = modelStateErrors };
            }

            return modelStateErrors;
        }

        // Deserialize error bags from TempData
        Dictionary<string, Dictionary<string, string>> errorBags;
        if (tempData["__ValidationErrors"] is string jsonString && !string.IsNullOrEmpty(jsonString))
        {
            try
            {
                errorBags = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(jsonString) ??
                            new Dictionary<string, Dictionary<string, string>>();
            }
            catch (JsonException)
            {
                return new Dictionary<string, string>(0);
            }
        }
        else
        {
            return new Dictionary<string, string>(0);
        }

        if (errorBags.Count == 0)
        {
            return new Dictionary<string, string>(0);
        }

        // Clear the temp data after reading (one-time use)
        tempData.Remove("__ValidationErrors");

        // Convert to camelCase for client-side consistency
        var processedBags = errorBags.ToDictionary(
            bag => bag.Key,
            bag => bag.Value.ToDictionary(
                error => error.Key.ToCamelCase(),
                error => error.Value
            )
        );

        var requestedErrorBag = _context.HttpContext.Request.Headers[InertiaHeader.ErrorBag].ToString();

        // Laravel's logic: If there's only default bag AND a specific bag is requested
        if (processedBags.ContainsKey("default") && !string.IsNullOrEmpty(requestedErrorBag))
        {
            return new Dictionary<string, object> { [requestedErrorBag] = processedBags["default"] };
        }

        // Laravel's logic: If there's only default bag, return its contents directly
        if (processedBags.ContainsKey("default") && processedBags.Count == 1)
        {
            return processedBags["default"];
        }

        // Laravel's logic: Return all bags
        return processedBags.ToDictionary(
            bag => bag.Key,
            bag => (object)bag.Value
        );
    }

    /// <summary>
    /// Get only current ModelState errors (not TempData)
    /// Matches the original GetErrors() logic exactly
    /// </summary>
    private Dictionary<string, string> GetCurrentModelStateErrors()
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

    /// <summary>
    /// Add additional properties to the page.
    /// </summary>
    /// <param name="key">The property key, a dictionary of properties, or a ProvidesInertiaProperties object</param>
    /// <param name="value">The property value (only used when key is a string)</param>
    /// <returns>The Response instance for method chaining</returns>
    public Response With(string key, object? value)
    {
        _props[key] = value;
        return this;
    }

    /// <summary>
    /// Add additional properties to the page from a dictionary.
    /// </summary>
    /// <param name="properties">Dictionary of properties to add</param>
    /// <returns>The Response instance for method chaining</returns>
    public Response With(IDictionary<string, object?> properties)
    {
        foreach (var kvp in properties)
        {
            _props[kvp.Key] = kvp.Value;
        }
        return this;
    }

    /// <summary>
    /// Add additional properties to the page from a ProvidesInertiaProperties object.
    /// </summary>
    /// <param name="provider">The property provider</param>
    /// <returns>The Response instance for method chaining</returns>
    public Response With(ProvidesInertiaProperties provider)
    {
        // Generate a unique key for the provider
        var providerKey = $"__provider_{Guid.NewGuid():N}";
        _props[providerKey] = provider;
        return this;
    }

    /// <summary>
    /// Add additional properties to the page from an anonymous object.
    /// </summary>
    /// <param name="properties">Anonymous object with properties to add</param>
    /// <returns>The Response instance for method chaining</returns>
    public Response With(object properties)
    {
        if (properties == null) return this;

        if (properties is IDictionary<string, object?> dict)
        {
            return With(dict);
        }

        if (properties is ProvidesInertiaProperties provider)
        {
            return With(provider);
        }

        // Convert anonymous object to dictionary
        var props = properties.GetType().GetProperties()
            .ToDictionary(p => p.Name, p => p.GetValue(properties));

        foreach (var kvp in props)
        {
            _props[kvp.Key] = kvp.Value;
        }

        return this;
    }
}
