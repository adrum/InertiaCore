using System.Net;
using InertiaCore.Extensions;
using InertiaCore.Utils;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;

namespace InertiaCore;

public class Middleware
{
    private readonly RequestDelegate _next;

    public Middleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.IsInertiaRequest()
            && context.Request.Method == "GET"
            && context.Request.Headers[InertiaHeader.Version] != Inertia.GetVersion())
        {
            await OnVersionChange(context);
            return;
        }

        await _next(context);

        // Convert 302 to 303 for PUT/PATCH/DELETE Inertia requests
        if (context.IsInertiaRequest()
            && context.Response.StatusCode == 302
            && new[] { "PUT", "PATCH", "DELETE" }.Contains(context.Request.Method))
        {
            context.Response.StatusCode = 303;
        }
    }

    private static async Task OnVersionChange(HttpContext context)
    {
        var tempData = context.RequestServices.GetRequiredService<ITempDataDictionaryFactory>()
            .GetTempData(context);

        if (tempData.Any()) tempData.Keep();

        context.Response.Headers.Override(InertiaHeader.Location, context.RequestedUri());
        context.Response.StatusCode = (int)HttpStatusCode.Conflict;

        await context.Response.CompleteAsync();
    }
}
