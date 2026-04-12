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
    [Description("Test if the generated HTML contains valid page data.")]
    public async Task TestHtml()
    {
        var html = await _factory.Html(new { Test = "Test" });

        Assert.That(html.ToString(),
            Is.EqualTo("<div id=\"app\" data-page=\"{&quot;test&quot;:&quot;Test&quot;}\"></div>"));
    }

    private static IResponseFactory BuildFactoryWithOptions(InertiaOptions inertiaOptions)
    {
        var contextAccessor = new Mock<IHttpContextAccessor>();
        var httpClientFactory = new Mock<IHttpClientFactory>();
        var serializer = new DefaultInertiaSerializer();
        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(x => x.ContentRootPath).Returns(Path.GetTempPath());

        var options = new Mock<IOptions<InertiaOptions>>();
        options.SetupGet(x => x.Value).Returns(inertiaOptions);

        var gateway = new Gateway(httpClientFactory.Object, serializer, options.Object, environment.Object, Mock.Of<IHttpContextAccessor>());
        return new ResponseFactory(contextAccessor.Object, gateway, serializer, options.Object, environment.Object);
    }

    [Test]
    [Description("Test that the script-tag mode emits raw JSON inside a <script> element followed by the mount div.")]
    public async Task TestHtmlScriptTagMode()
    {
        var factory = BuildFactoryWithOptions(new InertiaOptions { UseScriptTagForInitialPage = true });

        var html = (await factory.Html(new { Component = "Home" })).ToString()!;

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.StartWith("<script data-page=\"app\" type=\"application/json\">"));
            Assert.That(html, Does.Contain("\"component\":\"Home\""));
            Assert.That(html, Does.Contain("</script><div id=\"app\"></div>"));
            Assert.That(html, Does.Not.Contain("&quot;"));
        });
    }

    [Test]
    [Description("Test that a payload containing </script> is escaped so it cannot terminate the script element early.")]
    public async Task TestHtmlScriptTagModeEscapesClosingTag()
    {
        var factory = BuildFactoryWithOptions(new InertiaOptions { UseScriptTagForInitialPage = true });

        var html = (await factory.Html(new { Payload = "</script>" })).ToString()!;

        Assert.Multiple(() =>
        {
            // The raw sequence </script> must not appear inside the script body -
            // only the terminating </script> that closes the initial page element.
            var firstClose = html.IndexOf("</script>", StringComparison.Ordinal);
            var lastClose = html.LastIndexOf("</script>", StringComparison.Ordinal);

            Assert.That(firstClose, Is.GreaterThan(0));
            Assert.That(firstClose, Is.EqualTo(lastClose),
                "Only one </script> should appear - the closing tag of the initial page script element.");
            Assert.That(html, Does.EndWith("</script><div id=\"app\"></div>"));
        });
    }

    [Test]
    [Description("Test that a custom root element id is honoured in the default (encoded-attribute) mode.")]
    public async Task TestHtmlCustomIdDefaultMode()
    {
        var factory = BuildFactoryWithOptions(new InertiaOptions());

        var html = (await factory.Html(new { Test = "Test" }, "custom-id")).ToString()!;

        Assert.That(html,
            Is.EqualTo("<div id=\"custom-id\" data-page=\"{&quot;test&quot;:&quot;Test&quot;}\"></div>"));
    }

    [Test]
    [Description("Test that a custom root element id is honoured in script-tag mode on both the script and div elements.")]
    public async Task TestHtmlCustomIdScriptTagMode()
    {
        var factory = BuildFactoryWithOptions(new InertiaOptions { UseScriptTagForInitialPage = true });

        var html = (await factory.Html(new { Component = "Home" }, "custom-id")).ToString()!;

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.StartWith("<script data-page=\"custom-id\" type=\"application/json\">"));
            Assert.That(html, Does.Contain("\"component\":\"Home\""));
            Assert.That(html, Does.EndWith("</script><div id=\"custom-id\"></div>"));
        });
    }
}
