using InertiaCore;
using InertiaCore.Extensions;
using InertiaCore.Utils;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Routing;
using NUnit.Framework;

namespace InertiaCoreTests;

public partial class Tests
{
    [Test]
    public async Task Middleware_ShouldSetVaryHeader_OnAllResponses()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        // Write something to prevent empty response handling
        var bytes = System.Text.Encoding.UTF8.GetBytes("test content");
        await context.Response.Body.WriteAsync(bytes);
        context.Response.Body.Position = 0;
        context.Response.ContentLength = bytes.Length;

        var middleware = new Middleware(async (ctx) =>
        {
            ctx.Response.StatusCode = 200;
            await ctx.Response.Body.WriteAsync(System.Text.Encoding.UTF8.GetBytes("OK"));
        });

        await middleware.InvokeAsync(context);

        Assert.That(context.Response.Headers["Vary"].ToString(), Is.EqualTo("X-Inertia"));
    }

    [Test]
    public async Task Middleware_ShouldSetVaryHeader_OnNonInertiaResponses()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        // Not an Inertia request - no X-Inertia header

        var middleware = new Middleware(async (ctx) =>
        {
            ctx.Response.StatusCode = 200;
            await ctx.Response.Body.WriteAsync(System.Text.Encoding.UTF8.GetBytes("OK"));
        });

        await middleware.InvokeAsync(context);

        Assert.That(context.Response.Headers["Vary"].ToString(), Is.EqualTo("X-Inertia"));
    }
}
