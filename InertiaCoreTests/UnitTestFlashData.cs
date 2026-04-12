using InertiaCore;
using InertiaCore.Models;
using InertiaCore.Ssr;
using InertiaCore.Utils;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
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
        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(x => x.ContentRootPath).Returns(Path.GetTempPath());
        var serializer = new DefaultInertiaSerializer();
        var options = new Mock<IOptions<InertiaOptions>>();
        options.SetupGet(x => x.Value).Returns(new InertiaOptions());

        var gateway = new Gateway(httpClientFactory.Object, serializer, options.Object, environment.Object, Mock.Of<IHttpContextAccessor>());
        var factory = new ResponseFactory(contextAccessor.Object, gateway, serializer, options.Object, environment.Object);
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

    /// <summary>
    /// Builds a ResponseFactory whose HttpContext has RequestServices wired up with a
    /// mocked ITempDataDictionaryFactory backed by a simple dictionary, so PullFlashed
    /// can observe both request-scoped and TempData-backed flash stores.
    /// </summary>
    private static (IResponseFactory factory, HttpContext httpContext, Dictionary<string, object> tempDataStore)
        PrepareFlashFactoryWithTempData()
    {
        var httpContext = new DefaultHttpContext();

        var tempDataStore = new Dictionary<string, object>();
        var tempData = new Mock<ITempDataDictionary>();
        tempData.Setup(t => t.ContainsKey(It.IsAny<string>()))
            .Returns<string>(k => tempDataStore.ContainsKey(k));
        tempData.Setup(t => t[It.IsAny<string>()])
            .Returns<string>(k => tempDataStore.TryGetValue(k, out var v) ? v : null);
        tempData.Setup(t => t.Remove(It.IsAny<string>()))
            .Returns<string>(k => tempDataStore.Remove(k));
        tempData.Setup(t => t.Save());

        var tempDataFactory = new Mock<ITempDataDictionaryFactory>();
        tempDataFactory.Setup(f => f.GetTempData(It.IsAny<HttpContext>())).Returns(tempData.Object);

        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider.Setup(s => s.GetService(typeof(ITempDataDictionaryFactory)))
            .Returns(tempDataFactory.Object);

        httpContext.RequestServices = serviceProvider.Object;

        var contextAccessor = new Mock<IHttpContextAccessor>();
        contextAccessor.SetupGet(x => x.HttpContext).Returns(httpContext);

        var httpClientFactory = new Mock<IHttpClientFactory>();
        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(x => x.ContentRootPath).Returns(Path.GetTempPath());
        var serializer = new DefaultInertiaSerializer();
        var options = new Mock<IOptions<InertiaOptions>>();
        options.SetupGet(x => x.Value).Returns(new InertiaOptions());

        var gateway = new Gateway(httpClientFactory.Object, serializer, options.Object, environment.Object, contextAccessor.Object);
        var factory = new ResponseFactory(contextAccessor.Object, gateway, serializer, options.Object, environment.Object);
        Inertia.UseFactory(factory);

        return (factory, httpContext, tempDataStore);
    }

    [Test]
    [Description("PullFlashed returns empty dict when no flash data exists.")]
    public void TestPullFlashedReturnsEmptyWhenNoFlashData()
    {
        var (factory, _) = PrepareFlashFactory();

        var result = factory.PullFlashed();

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.Empty);
    }

    [Test]
    [Description("PullFlashed returns request-scoped flash and clears it so GetFlashed is empty afterward.")]
    public void TestPullFlashedClearsRequestScopedFlash()
    {
        var (factory, _) = PrepareFlashFactory();

        factory.Flash("toast", "Saved!");

        var pulled = factory.PullFlashed();

        Assert.Multiple(() =>
        {
            Assert.That(pulled, Has.Count.EqualTo(1));
            Assert.That(pulled["toast"], Is.EqualTo("Saved!"));
            Assert.That(factory.GetFlashed(), Is.Empty);
        });
    }

    [Test]
    [Description("PullFlashed returns TempData-backed flash and removes the TempData key.")]
    public void TestPullFlashedClearsTempDataFlash()
    {
        var (factory, _, tempDataStore) = PrepareFlashFactoryWithTempData();

        var json = System.Text.Json.JsonSerializer.Serialize(
            new Dictionary<string, object?> { { "toast", "Welcome!" } });
        tempDataStore["inertia.flash_data"] = json;

        var pulled = factory.PullFlashed();

        Assert.Multiple(() =>
        {
            Assert.That(pulled, Has.Count.EqualTo(1));
            Assert.That(pulled["toast"]?.ToString(), Is.EqualTo("Welcome!"));
            Assert.That(tempDataStore.ContainsKey("inertia.flash_data"), Is.False);
        });
    }

    [Test]
    [Description("PullFlashed merges request-scoped and TempData flash, then clears both stores.")]
    public void TestPullFlashedMergesAndClearsBothSources()
    {
        var (factory, httpContext, tempDataStore) = PrepareFlashFactoryWithTempData();

        var json = System.Text.Json.JsonSerializer.Serialize(
            new Dictionary<string, object?> { { "a", "1" } });
        tempDataStore["inertia.flash_data"] = json;

        factory.Flash("b", "2");

        var pulled = factory.PullFlashed();

        Assert.Multiple(() =>
        {
            Assert.That(pulled, Has.Count.EqualTo(2));
            Assert.That(pulled["a"]?.ToString(), Is.EqualTo("1"));
            Assert.That(pulled["b"], Is.EqualTo("2"));
            Assert.That(tempDataStore.ContainsKey("inertia.flash_data"), Is.False);
            Assert.That(httpContext.Items.ContainsKey("inertia.flash_data"), Is.False);
            Assert.That(factory.GetFlashed(), Is.Empty);
        });
    }

    [Test]
    [Description("Inertia.PullFlashed facade mirrors the factory behavior.")]
    public void TestPullFlashedFacade()
    {
        var (_, _) = PrepareFlashFactory();

        Inertia.Flash("toast", "Hello!");

        var pulled = Inertia.PullFlashed();

        Assert.Multiple(() =>
        {
            Assert.That(pulled, Has.Count.EqualTo(1));
            Assert.That(pulled["toast"], Is.EqualTo("Hello!"));
            Assert.That(Inertia.GetFlashed(), Is.Empty);
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
