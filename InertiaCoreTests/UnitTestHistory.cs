using InertiaCore;
using InertiaCore.Models;
using InertiaCore.Ssr;
using InertiaCore.Utils;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Hosting;
using Moq;

namespace InertiaCoreTests;

public partial class Tests
{
    [Test]
    [Description("Test if history encryption is sent correctly.")]
    public async Task TestHistoryEncryptionResult()
    {
        _factory.EncryptHistory();

        var response = _factory.Render("Test/Page", new
        {
            Test = "Test"
        });

        var headers = new HeaderDictionary
        {
            { "X-Inertia", "true" }
        };

        var context = PrepareContext(headers);

        response.SetContext(context);
        await response.ProcessResponse();

        var result = response.GetResult();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.InstanceOf<JsonResult>());

            var json = (result as JsonResult)?.Value;
            Assert.That(json, Is.InstanceOf<Page>());

            Assert.That((json as Page)?.ClearHistory, Is.EqualTo(false));
            Assert.That((json as Page)?.EncryptHistory, Is.EqualTo(true));
            Assert.That((json as Page)?.Component, Is.EqualTo("Test/Page"));
            Assert.That((json as Page)?.Props, Is.EqualTo(new Dictionary<string, object?>
            {
                { "test", "Test" },
                { "errors", new Dictionary<string, string>(0) }
            }));
        });
    }

    [Test]
    [Description("Test if clear history is sent correctly.")]
    public async Task TestClearHistoryResult()
    {
        var (factory, httpContext) = CreateFactoryWithDefaultContext();

        factory.ClearHistory();

        var response = factory.Render("Test/Page", new
        {
            Test = "Test"
        });

        httpContext.Request.Headers["X-Inertia"] = "true";
        response.SetContext(new ActionContext(httpContext, new Microsoft.AspNetCore.Routing.RouteData(), new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor()));
        await response.ProcessResponse();

        var result = response.GetResult();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.InstanceOf<JsonResult>());

            var json = (result as JsonResult)?.Value;
            Assert.That(json, Is.InstanceOf<Page>());

            Assert.That((json as Page)?.ClearHistory, Is.EqualTo(true));
            Assert.That((json as Page)?.EncryptHistory, Is.EqualTo(false));
            Assert.That((json as Page)?.Component, Is.EqualTo("Test/Page"));
            Assert.That((json as Page)?.Props, Is.EqualTo(new Dictionary<string, object?>
            {
                { "test", "Test" },
                { "errors", new Dictionary<string, string>(0) }
            }));
        });
    }

    [Test]
    [Description("Test if clear history persists when redirecting.")]
    public async Task TestClearHistoryWithRedirect()
    {
        var (factory, httpContext) = CreateFactoryWithDefaultContext();

        // Simulate first request: set clearHistory
        factory.ClearHistory();

        // Render the response that follows the redirect
        var response = factory.Render("User/Edit", new { });

        httpContext.Request.Headers["X-Inertia"] = "true";
        response.SetContext(new ActionContext(httpContext, new Microsoft.AspNetCore.Routing.RouteData(), new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor()));
        await response.ProcessResponse();

        var result = response.GetResult();

        // Assert: clearHistory should be set on the page
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.InstanceOf<JsonResult>());

            var json = (result as JsonResult)?.Value;
            Assert.That(json, Is.InstanceOf<Page>());

            Assert.That((json as Page)?.ClearHistory, Is.EqualTo(true));
            Assert.That((json as Page)?.EncryptHistory, Is.EqualTo(false));
            Assert.That((json as Page)?.Component, Is.EqualTo("User/Edit"));
        });
    }

    /// <summary>
    /// Builds a ResponseFactory whose HttpContextAccessor is wired to a single DefaultHttpContext,
    /// so state set via ClearHistory / EncryptHistory is visible when Render() is called.
    /// </summary>
    private static (IResponseFactory factory, HttpContext httpContext) CreateFactoryWithDefaultContext()
    {
        var httpContext = new DefaultHttpContext();
        var contextAccessor = new Mock<IHttpContextAccessor>();
        contextAccessor.SetupGet(a => a.HttpContext).Returns(httpContext);

        var gateway = new Mock<IGateway>();
        var options = new Mock<IOptions<InertiaOptions>>();
        options.SetupGet(x => x.Value).Returns(new InertiaOptions());
        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(x => x.ContentRootPath).Returns(Path.GetTempPath());

        var factory = new ResponseFactory(contextAccessor.Object, gateway.Object, new DefaultInertiaSerializer(), options.Object, environment.Object);
        return (factory, httpContext);
    }

    /// <summary>
    /// Prepares ActionContext with session support for testing redirect scenarios.
    /// </summary>
    private static ActionContext PrepareContextWithSession(HeaderDictionary? headers, ISession session)
    {
        var request = new Mock<HttpRequest>();
        request.SetupGet(r => r.Headers).Returns(headers ?? new HeaderDictionary());

        var response = new Mock<HttpResponse>();
        response.SetupGet(r => r.Headers).Returns(new HeaderDictionary());

        var features = new Microsoft.AspNetCore.Http.Features.FeatureCollection();

        var httpContext = new Mock<HttpContext>();
        httpContext.SetupGet(c => c.Request).Returns(request.Object);
        httpContext.SetupGet(c => c.Response).Returns(response.Object);
        httpContext.SetupGet(c => c.Features).Returns(features);
        httpContext.SetupGet(c => c.Session).Returns(session);

        return new ActionContext(httpContext.Object, new Microsoft.AspNetCore.Routing.RouteData(), new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor());
    }
}
