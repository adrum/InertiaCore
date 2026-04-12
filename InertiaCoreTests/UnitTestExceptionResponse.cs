using InertiaCore;
using InertiaCore.Extensions;
using InertiaCore.Utils;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using System.Net;

namespace InertiaCoreTests;

[TestFixture]
public class UnitTestExceptionResponse
{
    private class BoomException : Exception
    {
        public BoomException(string message) : base(message) { }
    }

    private static TestServer BuildServer(Action<IApplicationBuilder> configureBeforeEndpoints)
    {
        var builder = new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddInertia(o => o.EnsurePagesExist = false);
                    services.AddMvc();
                    services.AddDistributedMemoryCache();
                    services.AddSession();
                });
                webHost.Configure(app =>
                {
                    app.UseInertia();
                    app.UseSession();
                    configureBeforeEndpoints(app);
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/boom", (HttpContext _) => throw new BoomException("kaboom"));
                    });
                });
            });

        var host = builder.Start();
        return host.GetTestServer();
    }

    [TearDown]
    public void TearDown()
    {
        Inertia.ResetFactory();
    }

    [Test]
    public async Task HandlerNotRegistered_ExceptionPropagates()
    {
        using var server = BuildServer(app => app.UseInertiaExceptionHandler());
        var client = server.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/boom");
        request.Headers.Add(InertiaHeader.Inertia, "true");

        Assert.ThrowsAsync<BoomException>(async () => await client.SendAsync(request));
    }

    [Test]
    public async Task HandlerRegisteredButDoesNotRender_ExceptionPropagates()
    {
        using var server = BuildServer(app =>
        {
            app.UseInertiaExceptionHandler(response =>
            {
                // deliberately do nothing
                _ = response.Exception;
            });
        });
        var client = server.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/boom");
        request.Headers.Add(InertiaHeader.Inertia, "true");

        Assert.ThrowsAsync<BoomException>(async () => await client.SendAsync(request));
    }

    [Test]
    public async Task HandlerRendersComponent_WritesInertiaJson()
    {
        using var server = BuildServer(app =>
        {
            app.UseInertiaExceptionHandler(response =>
            {
                response.Render("ErrorPage", new { status = 500, message = response.Exception.Message });
            });
        });
        var client = server.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/boom");
        request.Headers.Add(InertiaHeader.Inertia, "true");

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.That((int)response.StatusCode, Is.EqualTo(500));
        Assert.That(body, Does.Contain("\"component\":\"ErrorPage\""));
        Assert.That(body, Does.Contain("\"status\":500"));
        Assert.That(body, Does.Contain("\"message\":\"kaboom\""));
    }

    [Test]
    public async Task HandlerWithSharedData_IncludesSharedProps()
    {
        using var server = BuildServer(app =>
        {
            // Stage shared data via middleware so it lives on the request scope
            app.Use(async (ctx, next) =>
            {
                Inertia.Share("user", new Dictionary<string, object?> { ["name"] = "Alice" });
                await next(ctx);
            });

            app.UseInertiaExceptionHandler(response =>
            {
                response.Render("ErrorPage", new { status = 500 }).WithSharedData();
            });
        });
        var client = server.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/boom");
        request.Headers.Add(InertiaHeader.Inertia, "true");

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.That((int)response.StatusCode, Is.EqualTo(500));
        Assert.That(body, Does.Contain("\"component\":\"ErrorPage\""));
        Assert.That(body, Does.Contain("\"user\""));
        Assert.That(body, Does.Contain("\"name\":\"Alice\""));
    }

    [Test]
    public async Task NonInertiaRequest_HandlerNotInvoked_ExceptionPropagates()
    {
        var handlerInvoked = false;
        using var server = BuildServer(app =>
        {
            app.UseInertiaExceptionHandler(response =>
            {
                handlerInvoked = true;
                response.Render("ErrorPage");
            });
        });
        var client = server.CreateClient();

        // No X-Inertia header
        var request = new HttpRequestMessage(HttpMethod.Get, "/boom");

        Assert.ThrowsAsync<BoomException>(async () => await client.SendAsync(request));
        Assert.That(handlerInvoked, Is.False);
    }

    [Test]
    public async Task StatusCodeOverride_IsHonoredOnResponse()
    {
        using var server = BuildServer(app =>
        {
            app.UseInertiaExceptionHandler(response =>
            {
                response.StatusCode(503).Render("ErrorPage", new { status = 503 });
            });
        });
        var client = server.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/boom");
        request.Headers.Add(InertiaHeader.Inertia, "true");

        var response = await client.SendAsync(request);
        Assert.That((int)response.StatusCode, Is.EqualTo(503));
    }

    [Test]
    public void ExceptionResponse_DefaultStatusCodeIs500()
    {
        var ctx = new DefaultHttpContext();
        var er = new ExceptionResponse(new InvalidOperationException("x"), ctx);
        Assert.That(er.StatusCode(), Is.EqualTo(500));
        Assert.That(er.HasComponent, Is.False);

        er.Render("Foo", new { a = 1 });
        Assert.That(er.HasComponent, Is.True);
    }
}
