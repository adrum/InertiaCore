using System.Net;
using InertiaCore.Models;
using InertiaCore.Props;
using InertiaCore.Ssr;
using InertiaCore.Utils;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Hosting;

namespace InertiaCore;

internal interface IResponseFactory
{
    public Response Render(string component, object? props = null);
    public Task<IHtmlContent> Head(dynamic model);
    public Task<IHtmlContent> Html(dynamic model);
    public void Version(string? version);
    public void Version(Func<string?> version);
    public string? GetVersion();
    public LocationResult Location(string url);
    public BackResult Back(string? fallbackUrl = null, HttpStatusCode statusCode = HttpStatusCode.SeeOther);
    public void Share(string key, object? value);
    public void Share(IDictionary<string, object?> data);
    public void FlushShared();
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
}

internal class ResponseFactory : IResponseFactory
{
    private readonly IHttpContextAccessor _contextAccessor;
    private readonly IGateway _gateway;
    private readonly IInertiaSerializer _serializer;
    private readonly IOptions<InertiaOptions> _options;
    private readonly IWebHostEnvironment _environment;

    private object? _version;
    private bool _clearHistory;
    private bool? _encryptHistory;
    private Func<ActionContext, string>? _urlResolver;

    public ResponseFactory(IHttpContextAccessor contextAccessor, IGateway gateway, IInertiaSerializer serializer,
        IOptions<InertiaOptions> options, IWebHostEnvironment environment)
        => (_contextAccessor, _gateway, _serializer, _options, _environment) = (contextAccessor, gateway, serializer, options, environment);

    public Response Render(string component, object? props = null)
    {
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

        return new Response(component, dictProps, _options.Value.RootView, GetVersion(), _encryptHistory ?? _options.Value.EncryptHistory, _serializer, _urlResolver);
    }

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

    public async Task<IHtmlContent> Html(dynamic model)
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
        var encoded = WebUtility.HtmlEncode(data);

        return new HtmlString($"<div id=\"app\" data-page=\"{encoded}\"></div>");
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

    public void ClearHistory(bool clear = true)
    {
        var context = _contextAccessor.HttpContext;

        // Try to use session first (preferred for production to survive redirects)
        if (context?.Session != null)
        {
            if (clear)
            {
                context.Session.SetString("inertia.clear_history", "true");
            }
            else
            {
                context.Session.Remove("inertia.clear_history");
            }
        }
        else if (context?.Features != null)
        {
            // Fallback for test scenarios: store in request features
            if (clear)
            {
                context.Features.Set<bool>(true);
                context.Items["inertia.clear_history"] = true;
            }
            else
            {
                context.Features.Set<bool>(false);
                context.Items.Remove("inertia.clear_history");
            }
        }

        // Always set the instance variable as fallback
        _clearHistory = clear;
    }

    public void EncryptHistory(bool encrypt = true) => _encryptHistory = encrypt;

    public void ResolveUrlUsing(Func<ActionContext, string> urlResolver) => _urlResolver = urlResolver;

    public LazyProp Lazy(Func<object?> callback) => new(callback);
    public LazyProp Lazy(Func<Task<object?>> callback) => new(callback);
    public AlwaysProp Always(object? value) => new(value);
    public AlwaysProp Always(Func<object?> callback) => new(callback);
    public AlwaysProp Always(Func<Task<object?>> callback) => new(callback);
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
    public ScrollProp Scroll(object? value, string wrapper = "data", IScrollMetadata? metadata = null) => new(value, wrapper, metadata);
    public ScrollProp Scroll(Func<object?> callback, string wrapper = "data", IScrollMetadata? metadata = null) => new(callback, wrapper, metadata);
}
