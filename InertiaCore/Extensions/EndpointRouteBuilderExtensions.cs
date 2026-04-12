using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Routing;

namespace InertiaCore.Extensions;

public static class EndpointRouteBuilderExtensions
{
    /// <summary>
    ///     Register a GET endpoint that renders an Inertia page component with optional static props.
    ///     Mirrors the Laravel adapter's <c>Route::inertia()</c> convenience for controller-less routes.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="pattern">The URL pattern to match.</param>
    /// <param name="component">The Inertia page component name.</param>
    /// <param name="props">Optional static props to pass to the component.</param>
    /// <returns>An <see cref="IEndpointConventionBuilder" /> that can be used to further customize the endpoint.</returns>
    public static IEndpointConventionBuilder MapInertia(
        this IEndpointRouteBuilder endpoints,
        string pattern,
        string component,
        object? props = null)
    {
        return endpoints.MapGet(pattern, async (HttpContext context) =>
        {
            var result = Inertia.Render(component, props);

            var routeData = context.GetRouteData();
            var actionContext = new ActionContext(context, routeData, new ActionDescriptor());

            await result.ExecuteResultAsync(actionContext);
        });
    }
}
