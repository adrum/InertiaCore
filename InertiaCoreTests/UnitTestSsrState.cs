using InertiaCore.Ssr;
using Microsoft.AspNetCore.Http;

namespace InertiaCoreTests;

public partial class Tests
{
    [Test]
    [Description("SsrState.ForRequest returns the same instance for repeat calls within a single HttpContext")]
    public void TestSsrStateForRequestReturnsSameInstanceWithinContext()
    {
        var context = new DefaultHttpContext();

        var first = SsrState.ForRequest(context);
        var second = SsrState.ForRequest(context);

        Assert.That(second, Is.SameAs(first));
    }

    [Test]
    [Description("SsrState.ForRequest returns different instances across distinct HttpContexts")]
    public void TestSsrStateForRequestIsolatedAcrossContexts()
    {
        var ctxA = new DefaultHttpContext();
        var ctxB = new DefaultHttpContext();

        var a = SsrState.ForRequest(ctxA);
        var b = SsrState.ForRequest(ctxB);

        Assert.That(b, Is.Not.SameAs(a));
    }

    [Test]
    [Description("SsrState stores its instance on HttpContext.Items under the well-known key")]
    public void TestSsrStateStoredOnHttpContextItems()
    {
        var context = new DefaultHttpContext();
        var state = SsrState.ForRequest(context);

        Assert.That(context.Items[SsrState.ItemsKey], Is.SameAs(state));
    }

    [Test]
    [Description("SsrState.DispatchOnce runs the dispatcher only on the first call and caches the result")]
    public async Task TestSsrStateDispatchOnceRunsDispatcherOnlyOnce()
    {
        var state = new SsrState();
        var calls = 0;
        var cached = new SsrResponse { Head = new List<string> { "h" }, Body = "b" };

        var first = await state.DispatchOnce(() =>
        {
            calls++;
            return Task.FromResult<SsrResponse?>(cached);
        });

        var second = await state.DispatchOnce(() =>
        {
            calls++;
            return Task.FromResult<SsrResponse?>(new SsrResponse { Head = new List<string> { "other" }, Body = "other" });
        });

        Assert.Multiple(() =>
        {
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(first, Is.SameAs(cached));
            Assert.That(second, Is.SameAs(cached));
            Assert.That(state.Response, Is.SameAs(cached));
        });
    }

    [Test]
    [Description("SsrState.DispatchOnce caches null results too so retries don't hammer the gateway")]
    public async Task TestSsrStateDispatchOnceCachesNullResult()
    {
        var state = new SsrState();
        var calls = 0;

        var first = await state.DispatchOnce(() =>
        {
            calls++;
            return Task.FromResult<SsrResponse?>(null);
        });

        var second = await state.DispatchOnce(() =>
        {
            calls++;
            return Task.FromResult<SsrResponse?>(new SsrResponse { Head = new List<string> { "x" }, Body = "y" });
        });

        Assert.Multiple(() =>
        {
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(first, Is.Null);
            Assert.That(second, Is.Null);
        });
    }
}
