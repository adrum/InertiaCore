using System.Net;
using System.Text;
using InertiaCore.Models;
using InertiaCore.Ssr;
using InertiaCore.Utils;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;

namespace InertiaCoreTests;

public partial class Tests
{
    [Test]
    [Description("Test SSR dispatch should not dispatch by default when no bundle exists and bundle is required")]
    public void TestSsrDispatchDefaultBehaviorWithoutBundle()
    {
        var httpClientFactory = new Mock<IHttpClientFactory>();
        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(x => x.ContentRootPath).Returns(Path.GetTempPath());

        var options = new Mock<IOptions<InertiaOptions>>();
        options.SetupGet(x => x.Value).Returns(new InertiaOptions { SsrEnsureBundleExists = true });

        var gateway = new Gateway(httpClientFactory.Object, Mock.Of<IInertiaSerializer>(), options.Object, environment.Object);

        Assert.That(gateway.ShouldDispatch(), Is.False);
    }

    [Test]
    [Description("Test SSR dispatch should dispatch when SsrEnsureBundleExists is disabled")]
    public void TestSsrDispatchWithoutBundleEnabled()
    {
        var httpClientFactory = new Mock<IHttpClientFactory>();
        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(x => x.ContentRootPath).Returns(Path.GetTempPath());

        var options = new Mock<IOptions<InertiaOptions>>();
        options.SetupGet(x => x.Value).Returns(new InertiaOptions { SsrEnsureBundleExists = false });

        var gateway = new Gateway(httpClientFactory.Object, Mock.Of<IInertiaSerializer>(), options.Object, environment.Object);

        Assert.That(gateway.ShouldDispatch(), Is.True);
    }

    [Test]
    [Description("Test SSR dispatch should dispatch when bundle exists")]
    public void TestSsrDispatchWithBundleExists()
    {
        var tempDir = Path.GetTempPath();
        var bundleDir = Path.Combine(tempDir, "wwwroot", "js");
        Directory.CreateDirectory(bundleDir);

        var bundlePath = Path.Combine(bundleDir, "ssr.js");
        File.WriteAllText(bundlePath, "// SSR bundle");

        try
        {
            var httpClientFactory = new Mock<IHttpClientFactory>();
            var environment = new Mock<IWebHostEnvironment>();
            environment.SetupGet(x => x.ContentRootPath).Returns(tempDir);

            var options = new Mock<IOptions<InertiaOptions>>();
            options.SetupGet(x => x.Value).Returns(new InertiaOptions { SsrEnsureBundleExists = true });

            var gateway = new Gateway(httpClientFactory.Object, Mock.Of<IInertiaSerializer>(), options.Object, environment.Object);

            Assert.That(gateway.ShouldDispatch(), Is.True);
        }
        finally
        {
            if (File.Exists(bundlePath))
                File.Delete(bundlePath);
            if (Directory.Exists(bundleDir))
                Directory.Delete(bundleDir, true);
        }
    }

    [Test]
    [Description("Test SSR dispatch should dispatch when either bundle exists or SsrEnsureBundleExists is disabled")]
    public void TestSsrDispatchWithBundleAndDispatchWithoutBundleEnabled()
    {
        var tempDir = Path.GetTempPath();
        var bundleDir = Path.Combine(tempDir, "build");
        Directory.CreateDirectory(bundleDir);

        var bundlePath = Path.Combine(bundleDir, "ssr.js");
        File.WriteAllText(bundlePath, "// SSR bundle");

        try
        {
            var httpClientFactory = new Mock<IHttpClientFactory>();
            var environment = new Mock<IWebHostEnvironment>();
            environment.SetupGet(x => x.ContentRootPath).Returns(tempDir);

            var options = new Mock<IOptions<InertiaOptions>>();
            options.SetupGet(x => x.Value).Returns(new InertiaOptions { SsrEnsureBundleExists = false });

            var gateway = new Gateway(httpClientFactory.Object, Mock.Of<IInertiaSerializer>(), options.Object, environment.Object);

            Assert.That(gateway.ShouldDispatch(), Is.True);
        }
        finally
        {
            if (File.Exists(bundlePath))
                File.Delete(bundlePath);
            if (Directory.Exists(bundleDir))
                Directory.Delete(bundleDir, true);
        }
    }

    [Test]
    [Description("Test SSR dispatch with SsrBundlePath override pointing at an existing file")]
    public void TestSsrDispatchWithBundlePathOverrideExists()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"inertia-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var bundlePath = Path.Combine(tempDir, "custom-ssr.mjs");
        File.WriteAllText(bundlePath, "// custom SSR bundle");

        try
        {
            var httpClientFactory = new Mock<IHttpClientFactory>();
            var environment = new Mock<IWebHostEnvironment>();
            environment.SetupGet(x => x.ContentRootPath).Returns(tempDir);

            var options = new Mock<IOptions<InertiaOptions>>();
            options.SetupGet(x => x.Value).Returns(new InertiaOptions
            {
                SsrEnsureBundleExists = true,
                SsrBundlePath = bundlePath
            });

            var gateway = new Gateway(httpClientFactory.Object, Mock.Of<IInertiaSerializer>(), options.Object, environment.Object);

            Assert.That(gateway.ShouldDispatch(), Is.True);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Test]
    [Description("Test SSR dispatch with SsrBundlePath override missing and no common paths returns false")]
    public void TestSsrDispatchWithBundlePathOverrideMissingNoFallback()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"inertia-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var httpClientFactory = new Mock<IHttpClientFactory>();
            var environment = new Mock<IWebHostEnvironment>();
            environment.SetupGet(x => x.ContentRootPath).Returns(tempDir);

            var options = new Mock<IOptions<InertiaOptions>>();
            options.SetupGet(x => x.Value).Returns(new InertiaOptions
            {
                SsrEnsureBundleExists = true,
                SsrBundlePath = Path.Combine(tempDir, "does-not-exist.mjs")
            });

            var gateway = new Gateway(httpClientFactory.Object, Mock.Of<IInertiaSerializer>(), options.Object, environment.Object);

            Assert.That(gateway.ShouldDispatch(), Is.False);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Test]
    [Description("Test SSR dispatch falls through to common paths when SsrBundlePath override is missing (Laravel parity)")]
    public void TestSsrDispatchWithBundlePathOverrideMissingFallsThroughToCommonPaths()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"inertia-test-{Guid.NewGuid():N}");
        var commonDir = Path.Combine(tempDir, "wwwroot", "js");
        Directory.CreateDirectory(commonDir);
        var commonBundle = Path.Combine(commonDir, "ssr.js");
        File.WriteAllText(commonBundle, "// fallback SSR bundle");

        try
        {
            var httpClientFactory = new Mock<IHttpClientFactory>();
            var environment = new Mock<IWebHostEnvironment>();
            environment.SetupGet(x => x.ContentRootPath).Returns(tempDir);

            var options = new Mock<IOptions<InertiaOptions>>();
            options.SetupGet(x => x.Value).Returns(new InertiaOptions
            {
                SsrEnsureBundleExists = true,
                SsrBundlePath = Path.Combine(tempDir, "bogus-override.mjs")
            });

            var gateway = new Gateway(httpClientFactory.Object, Mock.Of<IInertiaSerializer>(), options.Object, environment.Object);

            Assert.That(gateway.ShouldDispatch(), Is.True);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Test]
    [Description("Test SSR dispatch unchanged when SsrBundlePath is null and a common path matches")]
    public void TestSsrDispatchWithNullOverrideUsesCommonPaths()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"inertia-test-{Guid.NewGuid():N}");
        var commonDir = Path.Combine(tempDir, "public", "js");
        Directory.CreateDirectory(commonDir);
        var commonBundle = Path.Combine(commonDir, "ssr.js");
        File.WriteAllText(commonBundle, "// public SSR bundle");

        try
        {
            var httpClientFactory = new Mock<IHttpClientFactory>();
            var environment = new Mock<IWebHostEnvironment>();
            environment.SetupGet(x => x.ContentRootPath).Returns(tempDir);

            var options = new Mock<IOptions<InertiaOptions>>();
            options.SetupGet(x => x.Value).Returns(new InertiaOptions
            {
                SsrEnsureBundleExists = true,
                SsrBundlePath = null
            });

            var gateway = new Gateway(httpClientFactory.Object, Mock.Of<IInertiaSerializer>(), options.Object, environment.Object);

            Assert.That(gateway.ShouldDispatch(), Is.True);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Test]
    [Description("Test SSR dispatch checks multiple common bundle paths")]
    public void TestSsrDispatchChecksMultipleBundlePaths()
    {
        var tempDir = Path.GetTempPath();
        var bundleDir = Path.Combine(tempDir, "dist");
        Directory.CreateDirectory(bundleDir);

        var bundlePath = Path.Combine(bundleDir, "ssr.js");
        File.WriteAllText(bundlePath, "// SSR bundle in dist");

        try
        {
            var httpClientFactory = new Mock<IHttpClientFactory>();
            var environment = new Mock<IWebHostEnvironment>();
            environment.SetupGet(x => x.ContentRootPath).Returns(tempDir);

            var options = new Mock<IOptions<InertiaOptions>>();
            options.SetupGet(x => x.Value).Returns(new InertiaOptions { SsrEnsureBundleExists = true });

            var gateway = new Gateway(httpClientFactory.Object, Mock.Of<IInertiaSerializer>(), options.Object, environment.Object);

            Assert.That(gateway.ShouldDispatch(), Is.True);
        }
        finally
        {
            if (File.Exists(bundlePath))
                File.Delete(bundlePath);
            if (Directory.Exists(bundleDir))
                Directory.Delete(bundleDir, true);
        }
    }

    private const string SsrUrl = "http://127.0.0.1:13714/render";

    private static Gateway BuildGateway(HttpResponseMessage? response, Exception? throwOnSend, bool throwOnError)
    {
        var handler = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        var setup = handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>());

        if (throwOnSend != null)
            setup.ThrowsAsync(throwOnSend);
        else
            setup.ReturnsAsync(response!);

        var client = new HttpClient(handler.Object);
        var clientFactory = new Mock<IHttpClientFactory>();
        clientFactory.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(client);

        var options = new Mock<IOptions<InertiaOptions>>();
        options.SetupGet(x => x.Value).Returns(new InertiaOptions { SsrThrowOnError = throwOnError });

        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(x => x.ContentRootPath).Returns(Path.GetTempPath());

        return new Gateway(clientFactory.Object, new DefaultInertiaSerializer(), options.Object, environment.Object);
    }

    [Test]
    [Description("With SsrThrowOnError off, a 500 from the SSR server returns null without throwing.")]
    public async Task TestSsrDispatchSilentlySwallowsServerErrorByDefault()
    {
        var gateway = BuildGateway(
            new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("boom") },
            throwOnSend: null, throwOnError: false);

        var result = await gateway.Dispatch(new { component = "Test" }, SsrUrl);
        Assert.That(result, Is.Null);
    }

    [Test]
    [Description("With SsrThrowOnError off, an unreachable SSR server returns null without throwing.")]
    public async Task TestSsrDispatchSilentlySwallowsTransportErrorByDefault()
    {
        var gateway = BuildGateway(null, throwOnSend: new HttpRequestException("connection refused"),
            throwOnError: false);

        var result = await gateway.Dispatch(new { component = "Test" }, SsrUrl);
        Assert.That(result, Is.Null);
    }

    [Test]
    [Description("With SsrThrowOnError on, a 500 from the SSR server throws SsrException wrapping HttpRequestException.")]
    public void TestSsrDispatchThrowsOnServerError()
    {
        var gateway = BuildGateway(
            new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("boom") },
            throwOnSend: null, throwOnError: true);

        var ex = Assert.ThrowsAsync<SsrException>(async () =>
            await gateway.Dispatch(new { component = "Test" }, SsrUrl));
        Assert.That(ex!.InnerException, Is.InstanceOf<HttpRequestException>());
    }

    [Test]
    [Description("With SsrThrowOnError on, an unreachable SSR server throws SsrException with the inner exception preserved.")]
    public void TestSsrDispatchThrowsOnTransportError()
    {
        var inner = new HttpRequestException("connection refused");
        var gateway = BuildGateway(null, throwOnSend: inner, throwOnError: true);

        var ex = Assert.ThrowsAsync<SsrException>(async () =>
            await gateway.Dispatch(new { component = "Test" }, SsrUrl));
        Assert.That(ex!.InnerException, Is.SameAs(inner));
    }

    [Test]
    [Description("With SsrThrowOnError on, a malformed JSON SSR response throws SsrException.")]
    public void TestSsrDispatchThrowsOnMalformedJson()
    {
        var gateway = BuildGateway(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("not json at all", Encoding.UTF8, "application/json")
            },
            throwOnSend: null, throwOnError: true);

        var ex = Assert.ThrowsAsync<SsrException>(async () =>
            await gateway.Dispatch(new { component = "Test" }, SsrUrl));
        Assert.That(ex!.InnerException, Is.Not.Null);
    }

    [Test]
    [Description("With SsrThrowOnError on, a successful SSR response is returned normally without throwing.")]
    public async Task TestSsrDispatchSucceedsWhenFlagOn()
    {
        var body = "{\"head\":[\"<title>ok</title>\"],\"body\":\"<div id=\\\"app\\\"></div>\"}";
        var gateway = BuildGateway(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            },
            throwOnSend: null, throwOnError: true);

        var result = await gateway.Dispatch(new { component = "Test" }, SsrUrl);
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Body, Does.Contain("app"));
    }
}
