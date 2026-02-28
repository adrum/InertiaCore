using InertiaCore.Utils;
using Microsoft.AspNetCore.Http;

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
        await _next(context);

        context.Response.Headers["Vary"] = InertiaHeader.Inertia;
    }
}
