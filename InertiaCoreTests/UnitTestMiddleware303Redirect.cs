using InertiaCore;
using InertiaCore.Models;
using InertiaCore.Ssr;
using InertiaCore.Utils;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Moq;

namespace InertiaCoreTests;

public class UnitTestMiddleware303Redirect
{
    [SetUp]
    public void Setup()
    {
        var contextAccessor = new Mock<IHttpContextAccessor>();
        var httpClientFactory = new Mock<IHttpClientFactory>();
        var serializer = new DefaultInertiaSerializer();
        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(x => x.ContentRootPath).Returns(Path.GetTempPath());
        var options = new Mock<IOptions<InertiaOptions>>();
        options.SetupGet(x => x.Value).Returns(new InertiaOptions());
        var gateway = new Gateway(httpClientFactory.Object, serializer, options.Object, environment.Object, Mock.Of<IHttpContextAccessor>());

        var factory = new ResponseFactory(contextAccessor.Object, gateway, serializer, options.Object, environment.Object);
        Inertia.UseFactory(factory);
    }

    /// <summary>
    ///     Creates a Middleware instance and invokes it with the given context.
    ///     The next delegate sets the response status code to the specified value.
    /// </summary>
    private static async Task InvokeMiddleware(HttpContext context, int responseStatusCode)
    {
        var middleware = new Middleware(_ =>
        {
            context.Response.StatusCode = responseStatusCode;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);
    }

    private static DefaultHttpContext CreateHttpContext(string method, bool isInertia)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;

        if (isInertia)
            context.Request.Headers[InertiaHeader.Inertia] = "true";

        return context;
    }

    [Test]
    public async Task PutInertiaRequest_302_ShouldConvertTo303()
    {
        var context = CreateHttpContext("PUT", isInertia: true);
        await InvokeMiddleware(context, 302);
        Assert.That(context.Response.StatusCode, Is.EqualTo(303));
    }

    [Test]
    public async Task PatchInertiaRequest_302_ShouldConvertTo303()
    {
        var context = CreateHttpContext("PATCH", isInertia: true);
        await InvokeMiddleware(context, 302);
        Assert.That(context.Response.StatusCode, Is.EqualTo(303));
    }

    [Test]
    public async Task DeleteInertiaRequest_302_ShouldConvertTo303()
    {
        var context = CreateHttpContext("DELETE", isInertia: true);
        await InvokeMiddleware(context, 302);
        Assert.That(context.Response.StatusCode, Is.EqualTo(303));
    }

    [Test]
    public async Task GetInertiaRequest_302_ShouldNotConvert()
    {
        var context = CreateHttpContext("GET", isInertia: true);
        await InvokeMiddleware(context, 302);
        Assert.That(context.Response.StatusCode, Is.EqualTo(302));
    }

    [Test]
    public async Task NonInertia_PutRequest_302_ShouldNotConvert()
    {
        var context = CreateHttpContext("PUT", isInertia: false);
        await InvokeMiddleware(context, 302);
        Assert.That(context.Response.StatusCode, Is.EqualTo(302));
    }
}
