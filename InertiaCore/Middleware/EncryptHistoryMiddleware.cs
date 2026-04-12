using Microsoft.AspNetCore.Http;

namespace InertiaCore.Middleware;

public class EncryptHistoryMiddleware
{
    private readonly RequestDelegate _next;

    public EncryptHistoryMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        Inertia.EncryptHistory();
        await _next(context);
    }
}
