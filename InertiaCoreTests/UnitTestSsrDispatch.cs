using System.Net;
using System.Text;
using InertiaCore.Models;
using InertiaCore.Ssr;
using InertiaCore.Utils;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;

namespace InertiaCoreTests;

public partial class Tests
{
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

        return new Gateway(clientFactory.Object, new DefaultInertiaSerializer(), options.Object);
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
