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
    private readonly bool _clearHistory;
    private readonly bool _preserveFragment;
    private readonly Func<ActionContext, string>? _urlResolver;
    private readonly IInertiaSerializer _serializer;
    private readonly bool _withAllErrors;

    private ActionContext? _context;
    private Page? _page;
    private IDictionary<string, object>? _viewData;
    private List<int> _cacheFor = new();

    internal Response(string component, Dictionary<string, object?> props, string rootView, string? version,
        bool encryptHistory, bool clearHistory, IInertiaSerializer serializer,
        Func<ActionContext, string>? urlResolver = null, bool withAllErrors = false, bool preserveFragment = false)
        => (_component, _props, _rootView, _version, _encryptHistory, _clearHistory, _serializer, _urlResolver, _withAllErrors, _preserveFragment) =
            (component, props, rootView, version, encryptHistory, clearHistory, serializer, urlResolver, withAllErrors, preserveFragment);

    public async Task ExecuteResultAsync(ActionContext context)
    {
        SetContext(context);
        await ProcessResponse();
        await GetResult().ExecuteResultAsync(_context!);
    }

    protected internal async Task ProcessResponse()
    {
        var shared = _context!.HttpContext.Features.Get<InertiaSharedProps>()?.GetAll()
            ?? new Dictionary<string, object?>();

        var resolver = new PropsResolver(_context!.HttpContext.Request, _component);
        var (resolvedProps, metadata) = await resolver.Resolve(shared, _props);

        resolvedProps["errors"] = ResolveValidationErrors();

        var page = new Page
        {
            Component = _component,
            Version = _version,
            Url = _urlResolver?.Invoke(_context!) ?? _context!.RequestedUri(),
            Props = resolvedProps,
            EncryptHistory = _encryptHistory,
            ClearHistory = _clearHistory,
            PreserveFragment = _preserveFragment ? true : null,
            SharedProps = metadata.SharedProps,
            MergeProps = metadata.MergeProps,
            PrependProps = metadata.PrependProps,
            DeepMergeProps = metadata.DeepMergeProps,
            MatchPropsOn = metadata.MatchPropsOn,
            DeferredProps = metadata.DeferredProps,
            ScrollProps = metadata.ScrollProps,
            OnceProps = metadata.OnceProps,
            Cache = ResolveCacheDirections(),
            Flash = ResolveFlashData(),
        };

        SetPage(page);
    }

    private List<int>? ResolveCacheDirections()
    {
        return _cacheFor.Count == 0 ? null : _cacheFor;
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

    private Dictionary<string, object> GetErrors()
    {
        var errors = new Dictionary<string, object>();

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

        // Laravel's logic: If a default bag exists, return its contents directly
        // (mirrors Laravel's Middleware::resolveValidationErrors pipe)
        if (processedBags.ContainsKey("default"))
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
    private Dictionary<string, object> GetCurrentModelStateErrors()
    {
        if (!_context!.ModelState.IsValid)
            return _context!.ModelState.ToDictionary(o => o.Key.ToCamelCase(),
                o => _withAllErrors
                    ? (object)(o.Value?.Errors.Select(e => e.ErrorMessage).ToArray() ?? Array.Empty<string>())
                    : (object)(o.Value?.Errors.FirstOrDefault()?.ErrorMessage ?? ""));

        return new Dictionary<string, object>(0);
    }


    protected internal void SetContext(ActionContext context) => _context = context;

    private void SetPage(Page page) => _page = page;

    /// <summary>
    /// Set the cache duration for the response.
    /// </summary>
    public Response Cache(params int[] seconds)
    {
        _cacheFor.AddRange(seconds);
        return this;
    }

    /// <summary>
    /// Set the cache duration using TimeSpan.
    /// </summary>
    public Response Cache(params TimeSpan[] durations)
    {
        _cacheFor.AddRange(durations.Select(d => (int)d.TotalSeconds));
        return this;
    }

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

    /// <summary>
    /// Add flash data to the response.
    /// </summary>
    public Response Flash(string key, object? value)
    {
        Inertia.Flash(key, value);
        return this;
    }

    /// <summary>
    /// Add flash data to the response from a dictionary.
    /// </summary>
    public Response Flash(IDictionary<string, object?> data)
    {
        Inertia.Flash(data);
        return this;
    }

    /// <summary>
    /// Resolve flash data for the page object.
    /// </summary>
    private Dictionary<string, object?>? ResolveFlashData()
    {
        try
        {
            var httpContext = _context?.HttpContext;
            if (httpContext == null) return null;

            var flash = new Dictionary<string, object?>();

            // Check request-scoped items (set during this request)
            if (httpContext.Items.TryGetValue("inertia.flash_data", out var existing) &&
                existing is Dictionary<string, object?> itemsFlash)
            {
                foreach (var kvp in itemsFlash)
                    flash[kvp.Key] = kvp.Value;
            }

            // Also check TempData for flash data from previous request (reflashed on redirect)
            try
            {
                var tempDataFactory =
                    httpContext.RequestServices?.GetService(typeof(ITempDataDictionaryFactory))
                        as ITempDataDictionaryFactory;
                if (tempDataFactory != null)
                {
                    var tempData = tempDataFactory.GetTempData(httpContext);
                    if (tempData.ContainsKey("inertia.flash_data") && tempData["inertia.flash_data"] is string json &&
                        !string.IsNullOrEmpty(json))
                    {
                        var stored =
                            System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(json);
                        if (stored != null)
                        {
                            foreach (var kvp in stored)
                            {
                                if (!flash.ContainsKey(kvp.Key))
                                    flash[kvp.Key] = kvp.Value;
                            }
                        }
                    }
                }
            }
            catch
            {
                // TempData not available
            }

            return flash.Count > 0 ? flash : null;
        }
        catch
        {
            return null;
        }
    }
}
