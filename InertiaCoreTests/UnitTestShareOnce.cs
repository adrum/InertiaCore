using InertiaCore;
using InertiaCore.Models;
using InertiaCore.Props;
using InertiaCore.Ssr;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Moq;

namespace InertiaCoreTests;

public partial class Tests
{
    /// <summary>
    ///     Builds a ResponseFactory whose IHttpContextAccessor returns a real HttpContext,
    ///     so methods like Share() which read/write FeatureCollection work end-to-end.
    /// </summary>
    private static (IResponseFactory factory, ActionContext context) BuildFactoryWithContext(
        HeaderDictionary? headers = null)
    {
        var request = new Mock<HttpRequest>();
        request.SetupGet(r => r.Headers).Returns(headers ?? new HeaderDictionary());

        var response = new Mock<HttpResponse>();
        response.SetupGet(r => r.Headers).Returns(new HeaderDictionary());

        var features = new FeatureCollection();

        var httpContext = new Mock<HttpContext>();
        httpContext.SetupGet(c => c.Request).Returns(request.Object);
        httpContext.SetupGet(c => c.Response).Returns(response.Object);
        httpContext.SetupGet(c => c.Features).Returns(features);

        var contextAccessor = new Mock<IHttpContextAccessor>();
        contextAccessor.SetupGet(a => a.HttpContext).Returns(httpContext.Object);

        var httpClientFactory = new Mock<IHttpClientFactory>();
        var gateway = new Gateway(httpClientFactory.Object);
        var options = new Mock<IOptions<InertiaOptions>>();
        options.SetupGet(x => x.Value).Returns(new InertiaOptions());

        var factory = new ResponseFactory(contextAccessor.Object, gateway, options.Object);
        var ctx = new ActionContext(httpContext.Object, new RouteData(), new ActionDescriptor());
        return (factory, ctx);
    }

    [Test]
    [Description("Test that ShareOnce returns a OnceProp instance.")]
    public void TestShareOnceReturnsOnceProp()
    {
        var (factory, _) = BuildFactoryWithContext();

        var prop = factory.ShareOnce("user", () => "Alice");

        Assert.That(prop, Is.Not.Null);
        Assert.That(prop, Is.InstanceOf<OnceProp>());
    }

    [Test]
    [Description("Test that ShareOnce allows chaining As()/Until()/Fresh() on the returned OnceProp.")]
    public void TestShareOnceChainable()
    {
        var (factory, _) = BuildFactoryWithContext();

        var flags = new Dictionary<string, bool> { { "beta", true } };

        Assert.DoesNotThrow(() =>
        {
            factory.ShareOnce("feature", () => flags)
                .As("featureFlags")
                .Until(TimeSpan.FromHours(1))
                .Fresh();
        });
    }

    [Test]
    [Description("Test that a ShareOnce prop is merged into the page props at render time.")]
    public async Task TestShareOncePropIsSharedAndRendered()
    {
        var (factory, context) = BuildFactoryWithContext();

        factory.ShareOnce("sharedOnceUser", () => "Alice");

        var response = factory.Render("Test/Page", new { Test = "Test" });

        response.SetContext(context);
        await response.ProcessResponse();

        var page = response.GetJson().Value as Page;

        Assert.That(page?.Props, Does.ContainKey("test"));
        Assert.That(page?.Props, Does.ContainKey("sharedOnceUser"));
        Assert.That(page?.Props!["sharedOnceUser"], Is.EqualTo("Alice"));
    }

    [Test]
    [Description("Test the async overload of ShareOnce returns a OnceProp and resolves at render time.")]
    public async Task TestShareOnceAsyncOverload()
    {
        var (factory, context) = BuildFactoryWithContext();

        var prop = factory.ShareOnce("data", async () => await Task.FromResult<object?>("hello"));
        Assert.That(prop, Is.InstanceOf<OnceProp>());

        var response = factory.Render("Test/Page", new { Test = "Test" });

        response.SetContext(context);
        await response.ProcessResponse();

        var page = response.GetJson().Value as Page;

        Assert.That(page?.Props, Does.ContainKey("data"));
        Assert.That(page?.Props!["data"], Is.EqualTo("hello"));
    }
}
