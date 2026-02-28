using InertiaCore;
using InertiaCore.Models;
using InertiaCore.Ssr;
using InertiaCore.Utils;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Moq;

namespace InertiaCoreTests;

public partial class Tests
{
    [Test]
    [Description("Test that EncryptHistoryAttribute sets encrypt_history in HttpContext.Items.")]
    public void TestEncryptHistoryAttributeSetsContextItem()
    {
        var httpContext = new DefaultHttpContext();
        var contextAccessor = new Mock<IHttpContextAccessor>();
        contextAccessor.SetupGet(x => x.HttpContext).Returns(httpContext);

        var httpClientFactory = new Mock<IHttpClientFactory>();
        var gateway = new Gateway(httpClientFactory.Object);
        var options = new Mock<IOptions<InertiaOptions>>();
        options.SetupGet(x => x.Value).Returns(new InertiaOptions());

        var factory = new ResponseFactory(contextAccessor.Object, gateway, options.Object);
        Inertia.UseFactory(factory);

        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        var filters = new List<IFilterMetadata>();
        var actionExecutingContext = new ActionExecutingContext(
            actionContext,
            filters,
            new Dictionary<string, object?>(),
            new object()
        );

        var attribute = new EncryptHistoryAttribute();
        attribute.OnActionExecuting(actionExecutingContext);

        Assert.That(httpContext.Items.ContainsKey("inertia.encrypt_history"), Is.True);
        Assert.That(httpContext.Items["inertia.encrypt_history"], Is.EqualTo(true));
    }

    [Test]
    [Description("Test that concurrent contexts have independent encrypt history state.")]
    public async Task TestConcurrentEncryptHistoryIsolation()
    {
        var httpContext1 = new DefaultHttpContext();
        var httpContext2 = new DefaultHttpContext();

        var contextAccessor = new Mock<IHttpContextAccessor>();
        var httpClientFactory = new Mock<IHttpClientFactory>();
        var gateway = new Gateway(httpClientFactory.Object);
        var options = new Mock<IOptions<InertiaOptions>>();
        options.SetupGet(x => x.Value).Returns(new InertiaOptions());

        var factory = new ResponseFactory(contextAccessor.Object, gateway, options.Object);

        // Simulate request 1: encrypt history enabled
        contextAccessor.SetupGet(x => x.HttpContext).Returns(httpContext1);
        factory.EncryptHistory(true);

        // Simulate request 2: encrypt history not set (should use default)
        contextAccessor.SetupGet(x => x.HttpContext).Returns(httpContext2);

        var response2 = factory.Render("Test/Page", new { Test = "Test" });

        var headers = new HeaderDictionary
        {
            { "X-Inertia", "true" }
        };
        var context2 = PrepareContext(headers);
        response2.SetContext(context2);
        await response2.ProcessResponse();

        var result2 = response2.GetResult();
        var json2 = (result2 as JsonResult)?.Value as Page;

        // Request 2 should NOT have encrypt history (default is false)
        Assert.That(json2?.EncryptHistory, Is.EqualTo(false));

        // Switch back to request 1 context and render
        contextAccessor.SetupGet(x => x.HttpContext).Returns(httpContext1);

        var response1 = factory.Render("Test/Page", new { Test = "Test" });

        var context1 = PrepareContext(headers);
        response1.SetContext(context1);
        await response1.ProcessResponse();

        var result1 = response1.GetResult();
        var json1 = (result1 as JsonResult)?.Value as Page;

        // Request 1 SHOULD have encrypt history enabled
        Assert.That(json1?.EncryptHistory, Is.EqualTo(true));
    }
}
