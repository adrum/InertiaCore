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

        page.MergeProps = ResolveMergeProps(props);
        page.DeepMergeProps = ResolveDeepMergeProps(props);
        page.MatchPropsOn = ResolveMatchPropsOn(props);
        page.DeferredProps = ResolveDeferredProps(props);
        page.Props["errors"] = ResolveValidationErrors();

        SetPage(page);
    }

    /// <summary>
    /// Resolve the properties for the response.
    /// </summary>
    private async Task<Dictionary<string, object?>> ResolveProperties()
    {
        var props = _props;

        props = ResolveSharedProps(props);
        props = ResolveInertiaPropertyProviders(props);
        props = ResolvePartialProperties(props);
        props = ResolveAlways(props);
        props = await ResolvePropertyInstances(props, _context!.HttpContext.Request);

        return props;
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
    /// Resolve `merge` properties that should be appended to the existing values by the front-end.
    /// </summary>
    private List<string>? ResolveMergeProps(Dictionary<string, object?> props)
    {
        // Parse the "RESET" header into a collection of keys to reset
        var resetProps = new HashSet<string>(
           _context!.HttpContext.Request.Headers[InertiaHeader.Reset]
               .ToString()
               .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
               .Select(s => s.Trim()),
           StringComparer.OrdinalIgnoreCase
       );

        // Parse the "PARTIAL_ONLY" header into a collection of keys to include
        var onlyProps = _context!.HttpContext.Request.Headers[InertiaHeader.PartialOnly]
            .ToString()
            .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrEmpty(s))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Parse the "PARTIAL_EXCEPT" header into a collection of keys to exclude
        var exceptProps = new HashSet<string>(
            _context!.HttpContext.Request.Headers[InertiaHeader.PartialExcept]
                .ToString()
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim()),
            StringComparer.OrdinalIgnoreCase
        );

        var resolvedProps = props
            .Select(kv => kv.Key.ToCamelCase()) // Convert property name to camelCase
            .ToList();

        // Filter the props that are Mergeable and should be merged
        var mergeProps = _props.Where(o => o.Value is Mergeable mergeable && mergeable.ShouldMerge()) // Check if value is Mergeable and should merge
            .Where(kv => !resetProps.Contains(kv.Key)) // Exclude reset keys
            .Where(kv => onlyProps.Count == 0 || onlyProps.Contains(kv.Key)) // Include only specified keys if any
            .Where(kv => !exceptProps.Contains(kv.Key)) // Exclude specified keys
            .Select(kv => kv.Key.ToCamelCase()) // Convert property name to camelCase
            .Where(resolvedProps.Contains) // Filter only the props that are in the resolved props
            .ToList();

        if (mergeProps.Count == 0)
        {
            return null;
        }

        // Return the result
        return mergeProps;
    }


    /// <summary>
    /// Resolve match props on for properties that should be matched on specific keys.
    /// </summary>
    private Dictionary<string, string[]>? ResolveMatchPropsOn(Dictionary<string, object?> props)
    {
        // Parse the "RESET" header into a collection of keys to reset
        var resetProps = new HashSet<string>(
           _context!.HttpContext.Request.Headers[InertiaHeader.Reset]
               .ToString()
               .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
               .Select(s => s.Trim()),
           StringComparer.OrdinalIgnoreCase
       );

        // Parse the "PARTIAL_ONLY" header into a collection of keys to include
        var onlyProps = _context!.HttpContext.Request.Headers[InertiaHeader.PartialOnly]
            .ToString()
            .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrEmpty(s))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Parse the "PARTIAL_EXCEPT" header into a collection of keys to exclude
        var exceptProps = new HashSet<string>(
            _context!.HttpContext.Request.Headers[InertiaHeader.PartialExcept]
                .ToString()
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim()),
            StringComparer.OrdinalIgnoreCase
        );

        var resolvedProps = props
            .Select(kv => kv.Key.ToCamelCase()) // Convert property name to camelCase
            .ToList();

        // Filter the props that have match on keys
        var matchPropsOn = _props.Where(o => o.Value is Mergeable mergeable && mergeable.ShouldMerge() && mergeable.GetMatchOn() != null)
            .Where(kv => !resetProps.Contains(kv.Key)) // Exclude reset keys
            .Where(kv => onlyProps.Count == 0 || onlyProps.Contains(kv.Key)) // Include only specified keys if any
            .Where(kv => !exceptProps.Contains(kv.Key)) // Exclude specified keys
            .Where(kv => resolvedProps.Contains(kv.Key.ToCamelCase())) // Filter only the props that are in the resolved props
            .ToDictionary(
                kv => kv.Key.ToCamelCase(), // Convert property name to camelCase
                kv => ((Mergeable)kv.Value!).GetMatchOn()!
            );

        if (matchPropsOn.Count == 0)
        {
            return null;
        }

        // Return the result
        return matchPropsOn;
    }

    /// <summary>
    /// Resolve deep merge properties that should be deeply merged with existing values by the front-end.
    /// </summary>
    private List<string>? ResolveDeepMergeProps(Dictionary<string, object?> props)
    {
        // Parse the "RESET" header into a collection of keys to reset
        var resetProps = new HashSet<string>(
           _context!.HttpContext.Request.Headers[InertiaHeader.Reset]
               .ToString()
               .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
               .Select(s => s.Trim()),
           StringComparer.OrdinalIgnoreCase
       );

        // Parse the "PARTIAL_ONLY" header into a collection of keys to include
        var onlyProps = _context!.HttpContext.Request.Headers[InertiaHeader.PartialOnly]
            .ToString()
            .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrEmpty(s))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Parse the "PARTIAL_EXCEPT" header into a collection of keys to exclude
        var exceptProps = new HashSet<string>(
            _context!.HttpContext.Request.Headers[InertiaHeader.PartialExcept]
                .ToString()
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim()),
            StringComparer.OrdinalIgnoreCase
        );

        var resolvedProps = props
            .Select(kv => kv.Key.ToCamelCase()) // Convert property name to camelCase
            .ToList();

        // Filter the props that are DeepMergeable and should be deeply merged
        var deepMergeProps = _props.Where(o => o.Value is DeepMergeProp deepMergeable && deepMergeable.ShouldDeepMerge()) // Check if value is DeepMergeProp and should deep merge
            .Where(kv => !resetProps.Contains(kv.Key)) // Exclude reset keys
            .Where(kv => onlyProps.Count == 0 || onlyProps.Contains(kv.Key)) // Include only specified keys if any
            .Where(kv => !exceptProps.Contains(kv.Key)) // Exclude specified keys
            .Select(kv => kv.Key.ToCamelCase()) // Convert property name to camelCase
            .Where(resolvedProps.Contains) // Filter only the props that are in the resolved props
            .ToList();

        if (deepMergeProps.Count == 0)
        {
            return null;
        }

        // Return the result
        return deepMergeProps;
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
