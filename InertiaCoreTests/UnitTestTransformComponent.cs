using InertiaCore;
using InertiaCore.Models;
using InertiaCore.Ssr;
using InertiaCore.Utils;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Moq;

namespace InertiaCoreTests;

public partial class Tests
{
    [Test]
    [Description("Test that without a component transformer, component names are passed through unchanged.")]
    public async Task TestTransformComponentDefault()
    {
        var response = _factory.Render("Home");

        var context = PrepareContext();
        response.SetContext(context);
        await response.ProcessResponse();

        var page = response.GetJson().Value as Page;
        Assert.That(page?.Component, Is.EqualTo("Home"));
    }

    [Test]
    [Description("Test that a registered transformer rewrites the component name.")]
    public async Task TestTransformComponentRewrites()
    {
        _factory.TransformComponentUsing(name => $"v2/{name}");

        var response = _factory.Render("Home");

        var context = PrepareContext();
        response.SetContext(context);
        await response.ProcessResponse();

        var page = response.GetJson().Value as Page;
        Assert.That(page?.Component, Is.EqualTo("v2/Home"));
    }

    [Test]
    [Description("Test that passing null to TransformComponentUsing clears a previously registered transformer.")]
    public async Task TestTransformComponentNullClears()
    {
        _factory.TransformComponentUsing(name => $"v2/{name}");

        var response1 = _factory.Render("Home");
        var context1 = PrepareContext();
        response1.SetContext(context1);
        await response1.ProcessResponse();
        var page1 = response1.GetJson().Value as Page;
        Assert.That(page1?.Component, Is.EqualTo("v2/Home"));

        _factory.TransformComponentUsing(null);

        var response2 = _factory.Render("Home");
        var context2 = PrepareContext();
        response2.SetContext(context2);
        await response2.ProcessResponse();
        var page2 = response2.GetJson().Value as Page;
        Assert.That(page2?.Component, Is.EqualTo("Home"));
    }

    [Test]
    [Description("Test that TransformComponentUsing works via the Inertia static facade.")]
    public async Task TestTransformComponentFacade()
    {
        Inertia.UseFactory(_factory);
        try
        {
            Inertia.TransformComponentUsing(name => $"admin/{name}");

            var response = Inertia.Render("Dashboard");

            var context = PrepareContext();
            response.SetContext(context);
            await response.ProcessResponse();

            var page = response.GetJson().Value as Page;
            Assert.That(page?.Component, Is.EqualTo("admin/Dashboard"));
        }
        finally
        {
            Inertia.TransformComponentUsing(null);
            Inertia.ResetFactory();
        }
    }

    [Test]
    [Description("Test EnsurePagesExist runs against the transformed name, not the original.")]
    public void TestTransformComponentEnsurePagesExistOrdering()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"inertia-test-{Guid.NewGuid():N}");
        var pagesDir = Path.Combine(tempDir, "pages", "v2");
        Directory.CreateDirectory(pagesDir);

        var testComponent = Path.Combine(pagesDir, "Home.vue");
        File.WriteAllText(testComponent, "<template><div>Home</div></template>");

        try
        {
            var contextAccessor = new Mock<IHttpContextAccessor>();
            var httpClientFactory = new Mock<IHttpClientFactory>();
            var environment = new Mock<IWebHostEnvironment>();
            environment.SetupGet(x => x.ContentRootPath).Returns(tempDir);

            var options = new Mock<IOptions<InertiaOptions>>();
            options.SetupGet(x => x.Value).Returns(new InertiaOptions
            {
                EnsurePagesExist = true,
                PagePaths = new[] { "~/pages" },
                PageExtensions = new[] { ".vue" }
            });

            var gateway = new Gateway(httpClientFactory.Object, Mock.Of<IInertiaSerializer>(), options.Object, environment.Object);
            var factory = new ResponseFactory(contextAccessor.Object, gateway, Mock.Of<IInertiaSerializer>(), options.Object, environment.Object);

            // Without a transformer, rendering "Home" should fail (no pages/Home.vue exists).
            Assert.Throws<ComponentNotFoundException>(() => factory.Render("Home"));

            // Rendering the fully-qualified name directly should succeed.
            Assert.DoesNotThrow(() => factory.Render("v2/Home"));

            // With a transformer that prepends "v2/", rendering "Home" should now pass validation,
            // proving EnsurePagesExist runs against the TRANSFORMED name.
            factory.TransformComponentUsing(name => $"v2/{name}");
            Assert.DoesNotThrow(() => factory.Render("Home"));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }
}
