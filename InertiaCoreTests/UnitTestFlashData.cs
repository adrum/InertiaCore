using InertiaCore;
using InertiaCore.Models;
using InertiaCore.Ssr;
using InertiaCore.Utils;
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
    /// Creates a ResponseFactory with a working HttpContext that supports Items,
    /// and configures the static Inertia facade to use it.
    /// Returns both the factory and the HttpContext for assertions.
    /// </summary>
    private static (IResponseFactory factory, HttpContext httpContext) PrepareFlashFactory()
    {
        var httpContext = new DefaultHttpContext();

        var contextAccessor = new Mock<IHttpContextAccessor>();
        contextAccessor.SetupGet(x => x.HttpContext).Returns(httpContext);

        var httpClientFactory = new Mock<IHttpClientFactory>();
        var gateway = new Gateway(httpClientFactory.Object);
        var options = new Mock<IOptions<InertiaOptions>>();
        options.SetupGet(x => x.Value).Returns(new InertiaOptions());

        var factory = new ResponseFactory(contextAccessor.Object, gateway, options.Object);
        Inertia.UseFactory(factory);

        return (factory, httpContext);
    }

    /// <summary>
    /// Prepares an ActionContext that shares the same HttpContext used by the factory,
    /// so flash data written via the factory is visible during response processing.
    /// </summary>
    private static ActionContext PrepareContextForFlash(HttpContext httpContext)
    {
        return new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
    }

    [Test]
    [Description("Test that flash data is null when no flash data is set.")]
    public async Task TestFlashDataIsNullWhenNotSet()
    {
        var (factory, httpContext) = PrepareFlashFactory();

        var response = factory.Render("Test/Page", new
        {
            Test = "Test"
        });

        var context = PrepareContextForFlash(httpContext);

        response.SetContext(context);
        await response.ProcessResponse();

        var page = response.GetJson().Value as Page;

        Assert.That(page?.Flash, Is.Null);
    }

    [Test]
    [Description("Test that flash data on Response is chainable.")]
    public async Task TestFlashDataIsChainable()
    {
        var (factory, httpContext) = PrepareFlashFactory();

        var response = factory.Render("Test/Page", new
        {
            Test = "Test"
        })
        .Flash("success", "Item created!")
        .Flash("info", "Welcome back");

        var context = PrepareContextForFlash(httpContext);

        response.SetContext(context);
        await response.ProcessResponse();

        var page = response.GetJson().Value as Page;

        Assert.Multiple(() =>
        {
            Assert.That(page?.Flash, Is.Not.Null);
            Assert.That(page?.Flash, Has.Count.EqualTo(2));
            Assert.That(page?.Flash!["success"], Is.EqualTo("Item created!"));
            Assert.That(page?.Flash!["info"], Is.EqualTo("Welcome back"));
        });
    }

    [Test]
    [Description("Test that flash data set directly in HttpContext.Items is included in the page.")]
    public async Task TestFlashDataFromItemsIncludedInPage()
    {
        var (factory, httpContext) = PrepareFlashFactory();

        // Set flash data directly in Items (simulating what Inertia.Flash does internally)
        httpContext.Items["inertia.flash_data"] = new Dictionary<string, object?> { { "message", "Success!" } };

        var response = factory.Render("TestComponent", new Dictionary<string, object?>
        {
            { "data", "value" }
        });

        var context = PrepareContextForFlash(httpContext);

        response.SetContext(context);
        await response.ProcessResponse();

        var page = response.GetJson().Value as Page;

        Assert.Multiple(() =>
        {
            Assert.That(page, Is.Not.Null);
            Assert.That(page!.Flash, Is.Not.Null);
            Assert.That(page.Flash!["message"], Is.EqualTo("Success!"));
        });
    }

    [Test]
    [Description("Test that flash data dictionary overload merges correctly.")]
    public async Task TestFlashDataDictionaryOverload()
    {
        var (factory, httpContext) = PrepareFlashFactory();

        var response = factory.Render("Test/Page", new
        {
            Test = "Test"
        })
        .Flash(new Dictionary<string, object?>
        {
            { "success", "Created!" },
            { "warning", "Check details" }
        });

        var context = PrepareContextForFlash(httpContext);

        response.SetContext(context);
        await response.ProcessResponse();

        var page = response.GetJson().Value as Page;

        Assert.Multiple(() =>
        {
            Assert.That(page?.Flash, Is.Not.Null);
            Assert.That(page?.Flash, Has.Count.EqualTo(2));
            Assert.That(page?.Flash!["success"], Is.EqualTo("Created!"));
            Assert.That(page?.Flash!["warning"], Is.EqualTo("Check details"));
        });
    }
}
