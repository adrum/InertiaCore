using InertiaCore;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace InertiaCoreTests;

public partial class Tests
{
    [Test]
    [Description("Test that TempData.Keep() is called on redirect (302) responses.")]
    public async Task TestTempDataKeepCalledOnRedirect()
    {
        var tempData = new Mock<ITempDataDictionary>();
        tempData.Setup(t => t.Count).Returns(1);

        var tempDataFactory = new Mock<ITempDataDictionaryFactory>();
        tempDataFactory.Setup(f => f.GetTempData(It.IsAny<HttpContext>())).Returns(tempData.Object);

        var services = new ServiceCollection();
        services.AddSingleton(tempDataFactory.Object);
        var serviceProvider = services.BuildServiceProvider();

        var context = new DefaultHttpContext
        {
            RequestServices = serviceProvider
        };

        var middleware = new Middleware(
            _ =>
            {
                context.Response.StatusCode = 302;
                return Task.CompletedTask;
            },
            Mock.Of<Microsoft.AspNetCore.Builder.IApplicationBuilder>()
        );

        await middleware.InvokeAsync(context);

        tempData.Verify(t => t.Keep(), Times.Once);
    }

    [Test]
    [Description("Test that TempData.Keep() is NOT called on 200 responses.")]
    public async Task TestTempDataKeepNotCalledOnSuccess()
    {
        var tempData = new Mock<ITempDataDictionary>();
        tempData.Setup(t => t.Count).Returns(1);

        var tempDataFactory = new Mock<ITempDataDictionaryFactory>();
        tempDataFactory.Setup(f => f.GetTempData(It.IsAny<HttpContext>())).Returns(tempData.Object);

        var services = new ServiceCollection();
        services.AddSingleton(tempDataFactory.Object);
        var serviceProvider = services.BuildServiceProvider();

        var context = new DefaultHttpContext
        {
            RequestServices = serviceProvider
        };

        var middleware = new Middleware(
            _ =>
            {
                context.Response.StatusCode = 200;
                return Task.CompletedTask;
            },
            Mock.Of<Microsoft.AspNetCore.Builder.IApplicationBuilder>()
        );

        await middleware.InvokeAsync(context);

        tempData.Verify(t => t.Keep(), Times.Never);
    }
}
