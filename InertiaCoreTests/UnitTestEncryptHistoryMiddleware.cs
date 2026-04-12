using InertiaCore;
using Microsoft.AspNetCore.Http;
using Moq;

namespace InertiaCoreTests;

public class UnitTestEncryptHistoryMiddleware
{
    private IResponseFactory _originalFactory = null!;

    [SetUp]
    public void SetUp()
    {
        // Preserve whatever factory was installed so we can restore it after the test.
        _originalFactory = GetCurrentFactory();
    }

    [TearDown]
    public void TearDown()
    {
        Inertia.UseFactory(_originalFactory);
    }

    [Test]
    [Description("Middleware invokes the next delegate in the pipeline.")]
    public async Task TestMiddlewareCallsNext()
    {
        var factoryMock = new Mock<IResponseFactory>();
        Inertia.UseFactory(factoryMock.Object);

        var called = false;
        RequestDelegate next = _ =>
        {
            called = true;
            return Task.CompletedTask;
        };

        var middleware = new EncryptHistoryMiddleware(next);
        await middleware.InvokeAsync(new DefaultHttpContext());

        Assert.That(called, Is.True);
    }

    [Test]
    [Description("Middleware calls Inertia.EncryptHistory() exactly once per request.")]
    public async Task TestMiddlewareCallsEncryptHistory()
    {
        var factoryMock = new Mock<IResponseFactory>();
        Inertia.UseFactory(factoryMock.Object);

        RequestDelegate next = _ => Task.CompletedTask;

        var middleware = new EncryptHistoryMiddleware(next);
        await middleware.InvokeAsync(new DefaultHttpContext());

        factoryMock.Verify(f => f.EncryptHistory(true), Times.Once);
    }

    private static IResponseFactory GetCurrentFactory()
    {
        var field = typeof(Inertia).GetField("_factory",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        return (IResponseFactory)field!.GetValue(null)!;
    }
}
