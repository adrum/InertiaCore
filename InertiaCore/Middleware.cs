using System.Net;
using InertiaCore.Extensions;
using InertiaCore.Utils;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;

namespace InertiaCore;

internal class Middleware
{
    private readonly RequestDelegate _next;
    private readonly IApplicationBuilder _app;

    public Middleware(RequestDelegate next, IApplicationBuilder app)
    {
        _next = next;
        _app = app;
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

        // Reflash TempData on redirect responses
        if (context.Response.StatusCode >= 300 && context.Response.StatusCode < 400)
        {
            try
            {
                var tempData = context.RequestServices.GetRequiredService<ITempDataDictionaryFactory>()
                    .GetTempData(context);
                if (tempData.Any()) tempData.Keep();
            }
            catch
            {
                // TempData services not available
            }
        }
    }

    private async Task OnVersionChange(HttpContext context)
    {
        var tempData = _app.ApplicationServices.GetRequiredService<ITempDataDictionaryFactory>()
            .GetTempData(context);

        if (tempData.Any()) tempData.Keep();

        context.Response.Headers.Override(InertiaHeader.Location, context.RequestedUri());
        context.Response.StatusCode = (int)HttpStatusCode.Conflict;

        await context.Response.CompleteAsync();
    }
}
