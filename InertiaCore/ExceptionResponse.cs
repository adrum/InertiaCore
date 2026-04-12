using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Routing;

namespace InertiaCore;

/// <summary>
/// Represents the in-progress Inertia response that the user's exception
/// handler callback can configure. Mirrors the Laravel adapter's
/// <c>ExceptionResponse</c> class, which is passed to the closure registered
/// via <c>Inertia::handleExceptionsUsing()</c>.
/// </summary>
public class ExceptionResponse
{
    /// <summary>
    /// The original exception that was caught by the Inertia exception handler
    /// middleware.
    /// </summary>
    public Exception Exception { get; }

    /// <summary>
    /// The <see cref="HttpContext"/> for the in-flight request.
    /// </summary>
    public HttpContext HttpContext { get; }

    private string? _component;
    private object? _props;
    private bool _includeSharedData;
    private int? _statusCode;

    internal ExceptionResponse(Exception exception, HttpContext httpContext)
    {
        Exception = exception;
        HttpContext = httpContext;
        _statusCode = ResolveStatusCodeFromException(exception);
    }

    /// <summary>
    /// Configure this response to render the given Inertia component with the
    /// supplied props. Until this method is called, the response is a no-op
    /// and the exception will propagate to the default ASP.NET Core exception
    /// handler.
    /// </summary>
    /// <param name="component">The Inertia component name to render.</param>
    /// <param name="props">
    /// Optional props, passed through to <see cref="Inertia.Render(string, object?)"/>.
    /// Accepts an anonymous object or a <see cref="IDictionary{TKey, TValue}"/>.
    /// </param>
    public ExceptionResponse Render(string component, object? props = null)
    {
        _component = component;
        _props = props;
        return this;
    }

    /// <summary>
    /// Opt in to including the current request's shared props on the rendered
    /// error page. Shared props registered via <see cref="Inertia.Share(string, object?)"/>
    /// are already applied by the normal <c>Response</c> pipeline, so this
    /// simply flags intent for parity with the Laravel API.
    /// </summary>
    public ExceptionResponse WithSharedData()
    {
        _includeSharedData = true;
        return this;
    }

    /// <summary>
    /// Override the HTTP status code used when writing the rendered response.
    /// Defaults to 500 (or a value derived from the exception type).
    /// </summary>
    public ExceptionResponse StatusCode(int statusCode)
    {
        _statusCode = statusCode;
        return this;
    }

    /// <summary>
    /// Get the HTTP status code that will be written when the exception
    /// response is executed. Defaults to 500 unless the exception carries a
    /// recognizable status (e.g. a <c>StatusCode</c> property).
    /// </summary>
    public int StatusCode() => _statusCode ?? 500;

    /// <summary>
    /// True once the callback has configured a component via
    /// <see cref="Render(string, object?)"/>.
    /// </summary>
    internal bool HasComponent => _component != null;

    /// <summary>
    /// Write the configured Inertia page to the current response. Assumes the
    /// caller has already verified that <see cref="HasComponent"/> is true.
    /// </summary>
    internal async Task ExecuteAsync(IResponseFactory factory)
    {
        if (_component == null)
            return;

        // Shared props live on HttpContext.Features and are merged into the
        // response by Response.ResolveSharedProps during ProcessResponse, so
        // no extra plumbing is needed here for WithSharedData() to take
        // effect. The flag is kept for API parity with Laravel and in case
        // future work needs to gate shared-data inclusion explicitly.
        _ = _includeSharedData;

        var response = factory.Render(_component, _props);

        var status = StatusCode();

        // Response.GetJson() forces StatusCode = 200 while writing the Inertia
        // JSON payload. Hook OnStarting so we can override it back to the
        // configured error status right before the body is flushed to the
        // client, matching Laravel's setStatusCode() behaviour.
        HttpContext.Response.OnStarting(() =>
        {
            HttpContext.Response.StatusCode = status;
            return Task.CompletedTask;
        });

        HttpContext.Response.StatusCode = status;

        var actionContext = new ActionContext(HttpContext, HttpContext.GetRouteData() ?? new RouteData(),
            new ActionDescriptor());

        await response.ExecuteResultAsync(actionContext);
    }

    private static int ResolveStatusCodeFromException(Exception ex)
    {
        // Best-effort: look for a StatusCode property on the exception (covers
        // common custom types and Microsoft.AspNetCore.Http.BadHttpRequestException).
        var prop = ex.GetType().GetProperty("StatusCode");
        if (prop != null && prop.PropertyType == typeof(int))
        {
            var value = prop.GetValue(ex);
            if (value is int code && code >= 400 && code < 600)
                return code;
        }

        return 500;
    }
}
