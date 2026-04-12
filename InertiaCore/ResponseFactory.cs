using System.Net;
using InertiaCore.Extensions;
using InertiaCore.Models;
using InertiaCore.Props;
using InertiaCore.Ssr;
using InertiaCore.Utils;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Hosting;

namespace InertiaCore;

internal interface IResponseFactory
{
    public Response Render(string component, object? props = null);
    public Response Render(Enum component, object? props = null);
    public Task<IHtmlContent> Head(dynamic model);
    public Task<IHtmlContent> Html(dynamic model, string id = "app");
    public void Version(string? version);
    public void Version(Func<string?> version);
    public string? GetVersion();
    public LocationResult Location(string url);
    public BackResult Back(string? fallbackUrl = null, HttpStatusCode statusCode = HttpStatusCode.SeeOther);
    public void Share(string key, object? value);
    public void Share(IDictionary<string, object?> data);
    public void FlushShared();
    public object? GetShared(string? key = null, object? defaultValue = null);
    public void ClearHistory(bool clear = true);
    public void EncryptHistory(bool encrypt = true);
    public void ResolveUrlUsing(Func<ActionContext, string> urlResolver);
    public AlwaysProp Always(object? value);
    public AlwaysProp Always(Func<object?> callback);
    public AlwaysProp Always(Func<Task<object?>> callback);
    public LazyProp Lazy(Func<object?> callback);
    public LazyProp Lazy(Func<Task<object?>> callback);
    public DeferProp Defer(Func<object?> callback, string group = "default");
    public DeferProp Defer(Func<Task<object?>> callback, string group = "default");
    public MergeProp Merge(object? value);
    public MergeProp Merge(Func<object?> callback);
    public MergeProp Merge(Func<Task<object?>> callback);
    public DeepMergeProp DeepMerge(object? value);
    public DeepMergeProp DeepMerge(Func<object?> callback);
    public DeepMergeProp DeepMerge(Func<Task<object?>> callback);
    public OptionalProp Optional(Func<object?> callback);
    public OptionalProp Optional(Func<Task<object?>> callback);
    public ScrollProp Scroll(object? value, string wrapper = "data", IScrollMetadata? metadata = null);
    public ScrollProp Scroll(Func<object?> callback, string wrapper = "data", IScrollMetadata? metadata = null);
    public void Flash(string key, object? value);
    public void Flash(IDictionary<string, object?> data);
    public Dictionary<string, object?> GetFlashed();
    public OnceProp Once(Func<object?> callback);
    public OnceProp Once(Func<Task<object?>> callback);
    public OnceProp ShareOnce(string key, Func<object?> callback);
    public OnceProp ShareOnce(string key, Func<Task<object?>> callback);
    public void TransformComponentUsing(Func<string, string?>? componentTransformer);
}

internal class ResponseFactory : IResponseFactory
{
    private readonly IHttpContextAccessor _contextAccessor;
    private readonly IGateway _gateway;
    private readonly IInertiaSerializer _serializer;
    private readonly IOptions<InertiaOptions> _options;
    private readonly IWebHostEnvironment _environment;

    private object? _version;
    private Func<ActionContext, string>? _urlResolver;
    private Func<string, string?>? _componentTransformer;

    public ResponseFactory(IHttpContextAccessor contextAccessor, IGateway gateway, IInertiaSerializer serializer,
        IOptions<InertiaOptions> options, IWebHostEnvironment environment)
        => (_contextAccessor, _gateway, _serializer, _options, _environment) = (contextAccessor, gateway, serializer, options, environment);

    public Response Render(string component, object? props = null)
    {
        if (_componentTransformer != null)
        {
            component = _componentTransformer(component) ?? component;
        }

        if (_options.Value.EnsurePagesExist)
        {
            FindComponentOrFail(component);
        }

        props ??= new { };
        var dictProps = props switch
        {
            Dictionary<string, object?> dict => dict,
            _ => props.GetType().GetProperties()
                .ToDictionary(o => o.Name, o => o.GetValue(props))
        };

        return new Response(component, dictProps, _options.Value.RootView, GetVersion(),
            GetEncryptHistory(), GetClearHistory(), _serializer, _urlResolver, _options.Value.WithAllErrors);
    }

    public Response Render(Enum component, object? props = null)
        => Render(component.ToString(), props);

    public async Task<IHtmlContent> Head(dynamic model)
    {
        if (!_options.Value.SsrEnabled || !_gateway.ShouldDispatch()) return new HtmlString("");

        var context = _contextAccessor.HttpContext!;

        var response = context.Features.Get<SsrResponse>();
        response ??= await _gateway.Dispatch(model, _options.Value.SsrUrl);

        if (response == null) return new HtmlString("");

        context.Features.Set(response);
        return response.GetHead();
    }

    public async Task<IHtmlContent> Html(dynamic model, string id = "app")
    {
        if (_options.Value.SsrEnabled && _gateway.ShouldDispatch())
        {
            var context = _contextAccessor.HttpContext!;

            var response = context.Features.Get<SsrResponse>();
            response ??= await _gateway.Dispatch(model, _options.Value.SsrUrl);

            if (response != null)
            {
                context.Features.Set(response);
                return response.GetBody();
            }
        }

        var data = _serializer.Serialize(model);

        if (_options.Value.UseScriptTagForInitialPage)
        {
            // Escape any closing script tags in the JSON payload so they don't
            // prematurely terminate the surrounding <script> element.
            var safeJson = data.Replace("</", "<\\/");

            return new HtmlString(
                $"<script data-page=\"{id}\" type=\"application/json\">{safeJson}</script><div id=\"{id}\"></div>");
        }

        var encoded = WebUtility.HtmlEncode(data);

        return new HtmlString($"<div id=\"{id}\" data-page=\"{encoded}\"></div>");
    }

    public void Version(string? version) => _version = version;

    public void Version(Func<string?> version) => _version = version;

    public string? GetVersion() => _version switch
    {
        Func<string> func => func.Invoke(),
        string s => s,
        _ => null,
    };

    public LocationResult Location(string url) => new(url);

    public BackResult Back(string? fallbackUrl = null, HttpStatusCode statusCode = HttpStatusCode.SeeOther) =>
        new(fallbackUrl, statusCode);

    public void Share(string key, object? value)
    {
        var context = _contextAccessor.HttpContext!;

        var sharedData = context.Features.Get<InertiaSharedProps>();
        sharedData ??= new InertiaSharedProps();
        sharedData.Set(key, value);

        context.Features.Set(sharedData);
    }

    public void Share(IDictionary<string, object?> data)
    {
        var context = _contextAccessor.HttpContext!;

        var sharedData = context.Features.Get<InertiaSharedProps>();
        sharedData ??= new InertiaSharedProps();
        sharedData.Merge(data);

        context.Features.Set(sharedData);
    }

    public void FlushShared()
    {
        var context = _contextAccessor.HttpContext!;

        var sharedData = context.Features.Get<InertiaSharedProps>();
        if (sharedData != null)
        {
            sharedData.Clear();
        }
    }

    public object? GetShared(string? key = null, object? defaultValue = null)
    {
        var context = _contextAccessor.HttpContext!;

        var sharedData = context.Features.Get<InertiaSharedProps>();

        if (key == null)
        {
            return sharedData?.GetAll() ?? new Dictionary<string, object?>();
        }

        if (sharedData == null)
        {
            return defaultValue;
        }

        if (!key.Contains('.'))
        {
            return sharedData.TryGet(key, out var value) ? value : defaultValue;
        }

        var normalizedKey = string.Join('.', key.Split('.').Select(segment => segment.ToCamelCase()));
        var all = new Dictionary<string, object?>(sharedData.GetAll());
        var nested = DotNotationHelper.Get(all, normalizedKey);
        return nested ?? defaultValue;
    }

    public void ClearHistory(bool clear = true)
    {
        var context = _contextAccessor.HttpContext;
        if (context != null)
        {
            context.Items["inertia.clear_history"] = clear;
        }
    }

    public void EncryptHistory(bool encrypt = true)
    {
        var context = _contextAccessor.HttpContext;
        if (context != null)
        {
            context.Items["inertia.encrypt_history"] = encrypt;
        }
    }

    private bool GetClearHistory()
    {
        var context = _contextAccessor.HttpContext;
        if (context?.Items.TryGetValue("inertia.clear_history", out var value) == true && value is bool clear)
            return clear;
        return false;
    }

    private bool GetEncryptHistory()
    {
        var context = _contextAccessor.HttpContext;
        if (context?.Items.TryGetValue("inertia.encrypt_history", out var value) == true && value is bool encrypt)
            return encrypt;
        return _options.Value.EncryptHistory;
    }

    public void ResolveUrlUsing(Func<ActionContext, string> urlResolver) => _urlResolver = urlResolver;

    public void TransformComponentUsing(Func<string, string?>? componentTransformer) =>
        _componentTransformer = componentTransformer;

    public LazyProp Lazy(Func<object?> callback) => new(callback);
    public LazyProp Lazy(Func<Task<object?>> callback) => new(callback);
    public AlwaysProp Always(object? value) => new(value);
    public AlwaysProp Always(Func<object?> callback) => new(callback);
    public AlwaysProp Always(Func<Task<object?>> callback) => new(callback);
    public OnceProp Once(Func<object?> callback) => new(callback);
    public OnceProp Once(Func<Task<object?>> callback) => new(callback);

    public OnceProp ShareOnce(string key, Func<object?> callback)
    {
        var prop = Once(callback);
        Share(key, prop);
        return prop;
    }

    public OnceProp ShareOnce(string key, Func<Task<object?>> callback)
    {
        var prop = Once(callback);
        Share(key, prop);
        return prop;
    }

    public DeferProp Defer(Func<object?> callback, string group = "default") => new(callback, group);
    public DeferProp Defer(Func<Task<object?>> callback, string group = "default") => new(callback, group);
    public MergeProp Merge(object? value) => new(value);
    public MergeProp Merge(Func<object?> callback) => new(callback);
    public MergeProp Merge(Func<Task<object?>> callback) => new(callback);
    public DeepMergeProp DeepMerge(object? value) => new(value);
    public DeepMergeProp DeepMerge(Func<object?> callback) => new(callback);
    public DeepMergeProp DeepMerge(Func<Task<object?>> callback) => new(callback);
    public OptionalProp Optional(Func<object?> callback) => new(callback);
    public OptionalProp Optional(Func<Task<object?>> callback) => new(callback);
    public ScrollProp Scroll(object? value, string wrapper = "data", IScrollMetadata? metadata = null) => new(value, wrapper, metadata);
    public ScrollProp Scroll(Func<object?> callback, string wrapper = "data", IScrollMetadata? metadata = null) => new(callback, wrapper, metadata);

    private const string FlashDataKey = "inertia.flash_data";

    public void Flash(string key, object? value)
    {
        var context = _contextAccessor.HttpContext!;
        var flash = GetFlashStore(context);
        flash[key] = value;
        SetFlashStore(context, flash);
    }

    public void Flash(IDictionary<string, object?> data)
    {
        var context = _contextAccessor.HttpContext!;
        var flash = GetFlashStore(context);
        foreach (var kvp in data)
            flash[kvp.Key] = kvp.Value;
        SetFlashStore(context, flash);
    }

    public Dictionary<string, object?> GetFlashed()
    {
        var context = _contextAccessor.HttpContext!;
        var flash = GetFlashStore(context);

        try
        {
            var tempDataFactory = context.RequestServices?.GetService<ITempDataDictionaryFactory>();
            if (tempDataFactory != null)
            {
                var tempData = tempDataFactory.GetTempData(context);
                if (tempData.ContainsKey(FlashDataKey) && tempData[FlashDataKey] is string json && !string.IsNullOrEmpty(json))
                {
                    var stored = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(json);
                    if (stored != null)
                        foreach (var kvp in stored)
                            if (!flash.ContainsKey(kvp.Key))
                                flash[kvp.Key] = kvp.Value;
                }
            }
        }
        catch { }

        return flash;
    }

    private static Dictionary<string, object?> GetFlashStore(HttpContext context)
    {
        if (context.Items.TryGetValue(FlashDataKey, out var existing) && existing is Dictionary<string, object?> flash)
            return flash;
        return new Dictionary<string, object?>();
    }

    private static void SetFlashStore(HttpContext context, Dictionary<string, object?> flash)
    {
        context.Items[FlashDataKey] = flash;
    }

    private void FindComponentOrFail(string component)
    {
        var exists = FindComponent(component);
        if (!exists)
        {
            throw new ComponentNotFoundException(component);
        }
    }

    private bool FindComponent(string component)
    {
        foreach (var path in _options.Value.PagePaths)
        {
            var resolvedPath = ResolvePath(path);
            if (string.IsNullOrEmpty(resolvedPath)) continue;

            foreach (var extension in _options.Value.PageExtensions)
            {
                var normalizedComponent = component.Replace('/', Path.DirectorySeparatorChar);
                var fullPath = Path.Combine(resolvedPath, normalizedComponent + extension);
                if (File.Exists(fullPath))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private string? ResolvePath(string path)
    {
        if (path.StartsWith("~/"))
        {
            return Path.Combine(_environment.ContentRootPath, path[2..]);
        }
        return Path.IsPathRooted(path) ? path : Path.Combine(_environment.ContentRootPath, path);
    }
}
