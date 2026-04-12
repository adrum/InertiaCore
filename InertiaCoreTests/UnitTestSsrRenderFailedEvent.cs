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
    private const string RenderFailedSsrUrl = "http://127.0.0.1:13714/render";

    private static Gateway BuildRenderFailedGateway(HttpResponseMessage? response, Exception? throwOnSend,
        bool throwOnError = false)
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
    [Description("Transport failure raises RenderFailed with SsrErrorType.Connection")]
    public async Task TestSsrRenderFailedConnectionError()
    {
        var gateway = BuildRenderFailedGateway(null,
            throwOnSend: new HttpRequestException("connection refused"));

        SsrRenderFailed? captured = null;
        ((IGateway)gateway).RenderFailed += (_, e) => captured = e;

        var result = await gateway.Dispatch(new { component = "Dashboard", url = "/dashboard" }, RenderFailedSsrUrl);

        Assert.That(result, Is.Null);
        Assert.That(captured, Is.Not.Null);
        Assert.That(captured!.Type, Is.EqualTo(SsrErrorType.Connection));
        Assert.That(captured.Error, Is.EqualTo("connection refused"));
        Assert.That(captured.Component, Is.EqualTo("Dashboard"));
        Assert.That(captured.Url, Is.EqualTo("/dashboard"));
    }

    [Test]
    [Description("Non-2xx with JSON error body raises RenderFailed with type parsed from the body")]
    public async Task TestSsrRenderFailedJsonErrorBodyClassifiesType()
    {
        var body =
            "{\"error\":\"Could not resolve component\",\"type\":\"component-resolution\",\"hint\":\"check the resolver\",\"sourceLocation\":\"app.tsx:12:3\"}";

        var gateway = BuildRenderFailedGateway(
            new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            },
            throwOnSend: null);

        SsrRenderFailed? captured = null;
        ((IGateway)gateway).RenderFailed += (_, e) => captured = e;

        var result = await gateway.Dispatch(new { component = "Home", url = "/" }, RenderFailedSsrUrl);

        Assert.That(result, Is.Null);
        Assert.That(captured, Is.Not.Null);
        Assert.That(captured!.Type, Is.EqualTo(SsrErrorType.ComponentResolution));
        Assert.That(captured.Error, Is.EqualTo("Could not resolve component"));
        Assert.That(captured.Hint, Is.EqualTo("check the resolver"));
        Assert.That(captured.SourceLocation, Is.EqualTo("app.tsx:12:3"));
        Assert.That(captured.Component, Is.EqualTo("Home"));
    }

    [Test]
    [Description("Non-2xx with browser-api type parses BrowserApi field from body")]
    public async Task TestSsrRenderFailedBrowserApiType()
    {
        var body =
            "{\"error\":\"window is not defined\",\"type\":\"browser-api\",\"browserApi\":\"window\"}";

        var gateway = BuildRenderFailedGateway(
            new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            },
            throwOnSend: null);

        SsrRenderFailed? captured = null;
        ((IGateway)gateway).RenderFailed += (_, e) => captured = e;

        await gateway.Dispatch(new { component = "X", url = "/x" }, RenderFailedSsrUrl);

        Assert.That(captured, Is.Not.Null);
        Assert.That(captured!.Type, Is.EqualTo(SsrErrorType.BrowserApi));
        Assert.That(captured.BrowserApi, Is.EqualTo("window"));
    }

    [Test]
    [Description("Non-2xx with plain-text body raises RenderFailed with SsrErrorType.Unknown")]
    public async Task TestSsrRenderFailedPlainTextBodyFallsBackToUnknown()
    {
        var gateway = BuildRenderFailedGateway(
            new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("boom", Encoding.UTF8, "text/plain")
            },
            throwOnSend: null);

        SsrRenderFailed? captured = null;
        ((IGateway)gateway).RenderFailed += (_, e) => captured = e;

        await gateway.Dispatch(new { component = "Y", url = "/y" }, RenderFailedSsrUrl);

        Assert.That(captured, Is.Not.Null);
        Assert.That(captured!.Type, Is.EqualTo(SsrErrorType.Unknown));
        Assert.That(captured.Error, Does.Contain("500"));
    }

    [Test]
    [Description("Malformed JSON in the SSR response body raises RenderFailed with SsrErrorType.Unknown")]
    public async Task TestSsrRenderFailedMalformedJsonFallsBackToUnknown()
    {
        var gateway = BuildRenderFailedGateway(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("not json at all", Encoding.UTF8, "application/json")
            },
            throwOnSend: null);

        SsrRenderFailed? captured = null;
        ((IGateway)gateway).RenderFailed += (_, e) => captured = e;

        var result = await gateway.Dispatch(new { component = "Z", url = "/z" }, RenderFailedSsrUrl);

        Assert.That(result, Is.Null);
        Assert.That(captured, Is.Not.Null);
        Assert.That(captured!.Type, Is.EqualTo(SsrErrorType.Unknown));
    }

    [Test]
    [Description("Successful SSR dispatch does not raise RenderFailed")]
    public async Task TestSsrRenderFailedDoesNotFireOnSuccess()
    {
        var body = "{\"head\":[\"<title>ok</title>\"],\"body\":\"<div id=\\\"app\\\"></div>\"}";
        var gateway = BuildRenderFailedGateway(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            },
            throwOnSend: null);

        var fired = false;
        ((IGateway)gateway).RenderFailed += (_, _) => fired = true;

        var result = await gateway.Dispatch(new { component = "Ok", url = "/ok" }, RenderFailedSsrUrl);

        Assert.That(result, Is.Not.Null);
        Assert.That(fired, Is.False);
    }

    [Test]
    [Description("Event fires even when SsrThrowOnError is true, before the SsrException is thrown")]
    public void TestSsrRenderFailedFiresEvenWhenThrowOnErrorIsOn()
    {
        var body = "{\"error\":\"boom\",\"type\":\"render\"}";
        var gateway = BuildRenderFailedGateway(
            new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            },
            throwOnSend: null, throwOnError: true);

        SsrRenderFailed? captured = null;
        ((IGateway)gateway).RenderFailed += (_, e) => captured = e;

        Assert.ThrowsAsync<SsrException>(async () =>
            await gateway.Dispatch(new { component = "Err", url = "/err" }, RenderFailedSsrUrl));

        Assert.That(captured, Is.Not.Null);
        Assert.That(captured!.Type, Is.EqualTo(SsrErrorType.Render));
        Assert.That(captured.Error, Is.EqualTo("boom"));
    }
}
