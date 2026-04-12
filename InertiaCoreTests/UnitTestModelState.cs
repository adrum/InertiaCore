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
    [Description("Test if the model state dictionary is passed to the props correctly.")]
    public async Task TestModelState()
    {
        var response = _factory.Render("Test/Page", new
        {
            Test = "Test"
        });

        var context = PrepareContext(null, null, new Dictionary<string, string>
        {
            { "Field", "Error" }
        });

        response.SetContext(context);
        await response.ProcessResponse();

        var page = response.GetJson().Value as Page;

        Assert.That(page?.Props, Is.EqualTo(new Dictionary<string, object?>
        {
            { "test", "Test" },
            {
                "errors", new Dictionary<string, object>
                {
                    { "field", "Error" }
                }
            }
        }));
    }

    [Test]
    [Description("Test that with the default WithAllErrors=false, only the first error message for a field is returned.")]
    public async Task TestModelStateDefault_ReturnsFirstErrorOnly()
    {
        var response = _factory.Render("Test/Page", new { Test = "Test" });

        var context = PrepareContext();
        context.ModelState.AddModelError("email", "first");
        context.ModelState.AddModelError("email", "second");

        response.SetContext(context);
        await response.ProcessResponse();

        var page = response.GetJson().Value as Page;
        var errors = page?.Props["errors"] as Dictionary<string, object>;

        Assert.That(errors, Is.Not.Null);
        Assert.That(errors!["email"], Is.TypeOf<string>());
        Assert.That(errors["email"], Is.EqualTo("first"));
    }

    [Test]
    [Description("Test that with WithAllErrors=true, every error message for a field is returned as an array.")]
    public async Task TestModelStateWithAllErrors_ReturnsAllMessages()
    {
        var contextAccessor = new Mock<IHttpContextAccessor>();
        var httpClientFactory = new Mock<IHttpClientFactory>();
        var serializer = new DefaultInertiaSerializer();
        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(x => x.ContentRootPath).Returns(Path.GetTempPath());
        var options = new Mock<IOptions<InertiaOptions>>();
        options.SetupGet(x => x.Value).Returns(new InertiaOptions { WithAllErrors = true });
        var gateway = new Gateway(httpClientFactory.Object, serializer, options.Object, environment.Object);

        var factory = new ResponseFactory(contextAccessor.Object, gateway, serializer, options.Object, environment.Object);

        var response = factory.Render("Test/Page", new { Test = "Test" });

        var context = PrepareContext();
        context.ModelState.AddModelError("email", "first");
        context.ModelState.AddModelError("email", "second");

        response.SetContext(context);
        await response.ProcessResponse();

        var page = response.GetJson().Value as Page;
        var errors = page?.Props["errors"] as Dictionary<string, object>;

        Assert.That(errors, Is.Not.Null);
        Assert.That(errors!["email"], Is.TypeOf<string[]>());
        Assert.That((string[])errors["email"], Is.EqualTo(new[] { "first", "second" }));
    }
}
