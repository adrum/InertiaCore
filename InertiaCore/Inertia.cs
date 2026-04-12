using System.Net;
using System.Runtime.CompilerServices;
using InertiaCore.Props;
using InertiaCore.Utils;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc;

[assembly: InternalsVisibleTo("InertiaCoreTests")]

namespace InertiaCore;

public static class Inertia
{
    private static IResponseFactory _factory = default!;

    internal static void UseFactory(IResponseFactory factory) => _factory = factory;

    internal static void ResetFactory() => _factory = default!;

    public static Response Render(string component, object? props = null) => _factory.Render(component, props);

    public static Task<IHtmlContent> Head(dynamic model) => _factory.Head(model);

    public static Task<IHtmlContent> Html(dynamic model, string id = "app") => _factory.Html(model, id);

    public static void Version(string? version) => _factory.Version(version);

    public static void Version(Func<string?> version) => _factory.Version(version);

    public static string? GetVersion() => _factory.GetVersion();

    public static LocationResult Location(string url) => _factory.Location(url);

    public static BackResult Back(string? fallbackUrl = null, HttpStatusCode statusCode = HttpStatusCode.SeeOther) =>
        _factory.Back(fallbackUrl, statusCode);

    public static void Share(string key, object? value) => _factory.Share(key, value);

    public static void Share(IDictionary<string, object?> data) => _factory.Share(data);

    public static void FlushShared() => _factory.FlushShared();

    public static object? GetShared(string? key = null, object? defaultValue = null) =>
        _factory.GetShared(key, defaultValue);

    public static void ResolveUrlUsing(Func<ActionContext, string> urlResolver) => _factory.ResolveUrlUsing(urlResolver);

    public static void ClearHistory(bool clear = true) => _factory.ClearHistory(clear);

    public static void EncryptHistory(bool encrypt = true) => _factory.EncryptHistory(encrypt);

    public static AlwaysProp Always(string value) => _factory.Always(value);

    public static AlwaysProp Always(Func<string> callback) => _factory.Always(callback);

    public static AlwaysProp Always(Func<Task<object?>> callback) => _factory.Always(callback);

    public static DeferProp Defer(Func<object?> callback, string group = "default") => _factory.Defer(callback, group);

    public static DeferProp Defer(Func<Task<object?>> callback, string group = "default") => _factory.Defer(callback, group);

    public static LazyProp Lazy(Func<object?> callback) => _factory.Lazy(callback);

    public static LazyProp Lazy(Func<Task<object?>> callback) => _factory.Lazy(callback);

    public static OptionalProp Optional(Func<object?> callback) => _factory.Optional(callback);

    public static OptionalProp Optional(Func<Task<object?>> callback) => _factory.Optional(callback);

    public static MergeProp Merge(object? value) => _factory.Merge(value);

    public static MergeProp Merge(Func<object?> callback) => _factory.Merge(callback);

    public static MergeProp Merge(Func<Task<object?>> callback) => _factory.Merge(callback);

    public static DeepMergeProp DeepMerge(object? value) => _factory.DeepMerge(value);

    public static DeepMergeProp DeepMerge(Func<object?> callback) => _factory.DeepMerge(callback);

    public static DeepMergeProp DeepMerge(Func<Task<object?>> callback) => _factory.DeepMerge(callback);

    public static ScrollProp Scroll(object? value, string wrapper = "data", IScrollMetadata? metadata = null) => _factory.Scroll(value, wrapper, metadata);

    public static ScrollProp Scroll(Func<object?> callback, string wrapper = "data", IScrollMetadata? metadata = null) => _factory.Scroll(callback, wrapper, metadata);

    public static void Flash(string key, object? value) => _factory.Flash(key, value);

    public static void Flash(IDictionary<string, object?> data) => _factory.Flash(data);

    public static Dictionary<string, object?> GetFlashed() => _factory.GetFlashed();

    public static OnceProp Once(Func<object?> callback) => _factory.Once(callback);

    public static OnceProp Once(Func<Task<object?>> callback) => _factory.Once(callback);
}
