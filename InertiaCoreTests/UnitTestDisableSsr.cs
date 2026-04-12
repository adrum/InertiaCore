using InertiaCore;
using InertiaCore.Models;
using InertiaCore.Ssr;
using InertiaCore.Utils;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Moq;

namespace InertiaCoreTests;

public class UnitTestDisableSsr
{
    private static (Gateway gateway, DefaultHttpContext context, Mock<IHttpContextAccessor> accessor) BuildGateway(
        string requestPath = "/",
        bool ensureBundleExists = false)
    {
        var httpClientFactory = new Mock<IHttpClientFactory>();
        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(x => x.ContentRootPath).Returns(Path.GetTempPath());

        var options = new Mock<IOptions<InertiaOptions>>();
        options.SetupGet(x => x.Value).Returns(new InertiaOptions { SsrEnsureBundleExists = ensureBundleExists });

        var context = new DefaultHttpContext();
        context.Request.Path = requestPath;
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.SetupGet(x => x.HttpContext).Returns(context);

        var gateway = new Gateway(
            httpClientFactory.Object,
            new DefaultInertiaSerializer(),
            options.Object,
            environment.Object,
            accessor.Object);

        return (gateway, context, accessor);
    }

    [Test]
    public void Disable_True_ShortCircuitsShouldDispatch()
    {
        var (gateway, _, _) = BuildGateway();
        Assert.That(gateway.ShouldDispatch(), Is.True);

        gateway.Disable();
        Assert.That(gateway.ShouldDispatch(), Is.False);
    }

    [Test]
    public void Disable_False_LeavesDispatchEnabled()
    {
        var (gateway, _, _) = BuildGateway();
        gateway.Disable(false);
        Assert.That(gateway.ShouldDispatch(), Is.True);
    }

    [Test]
    public void Disable_ClosureReturningTrue_IsEvaluatedAtDispatchTime()
    {
        var (gateway, _, _) = BuildGateway();
        var called = 0;
        gateway.Disable(() => { called++; return true; });

        Assert.That(called, Is.EqualTo(0), "closure should not run at Disable() time");
        Assert.That(gateway.ShouldDispatch(), Is.False);
        Assert.That(called, Is.EqualTo(1));
    }

    [Test]
    public void Disable_ClosureReturningFalse_LeavesDispatchEnabled()
    {
        var (gateway, _, _) = BuildGateway();
        gateway.Disable(() => false);
        Assert.That(gateway.ShouldDispatch(), Is.True);
    }

    [Test]
    public void Except_WildcardMatchesDescendants()
    {
        var (gateway, _, _) = BuildGateway(requestPath: "/admin/users");
        gateway.Except("admin/*");
        Assert.That(gateway.ShouldDispatch(), Is.False);
    }

    [Test]
    public void Except_WildcardDoesNotMatchUnrelatedPath()
    {
        var (gateway, _, _) = BuildGateway(requestPath: "/public/home");
        gateway.Except("admin/*");
        Assert.That(gateway.ShouldDispatch(), Is.True);
    }

    [Test]
    public void Except_MultiplePatterns_MatchesAny()
    {
        var (gateway, _, _) = BuildGateway(requestPath: "/auth/login");
        gateway.Except("admin/*", "auth/*");
        Assert.That(gateway.ShouldDispatch(), Is.False);
    }

    [Test]
    public void Except_DeepPathMatchesWildcard()
    {
        var (gateway, _, _) = BuildGateway(requestPath: "/admin/users/5");
        gateway.Except("admin/*");
        Assert.That(gateway.ShouldDispatch(), Is.False);
    }

    [Test]
    public void Except_ExactPatternMatchesExactPath()
    {
        var (gateway, _, _) = BuildGateway(requestPath: "/login");
        gateway.Except("login");
        Assert.That(gateway.ShouldDispatch(), Is.False);
    }

    [Test]
    public void Except_ExactPatternDoesNotMatchDescendant()
    {
        var (gateway, _, _) = BuildGateway(requestPath: "/login/confirm");
        gateway.Except("login");
        Assert.That(gateway.ShouldDispatch(), Is.True);
    }

    [Test]
    public void Disable_State_IsPerRequestNotShared()
    {
        // Same gateway singleton, but two HttpContexts. Disabling SSR for
        // one must not leak into the other.
        var httpClientFactory = new Mock<IHttpClientFactory>();
        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(x => x.ContentRootPath).Returns(Path.GetTempPath());

        var options = new Mock<IOptions<InertiaOptions>>();
        options.SetupGet(x => x.Value).Returns(new InertiaOptions { SsrEnsureBundleExists = false });

        HttpContext current = new DefaultHttpContext();
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.SetupGet(x => x.HttpContext).Returns(() => current);

        var gateway = new Gateway(
            httpClientFactory.Object,
            new DefaultInertiaSerializer(),
            options.Object,
            environment.Object,
            accessor.Object);

        // Disable SSR on request A.
        var requestA = new DefaultHttpContext();
        requestA.Request.Path = "/admin";
        current = requestA;
        gateway.Disable();
        Assert.That(gateway.ShouldDispatch(), Is.False);

        // Switch to a fresh request B - must still dispatch.
        var requestB = new DefaultHttpContext();
        requestB.Request.Path = "/home";
        current = requestB;
        Assert.That(gateway.ShouldDispatch(), Is.True);
    }

    [Test]
    public void Facade_DisableSsr_DelegatesToGateway()
    {
        var (gateway, context, accessor) = BuildGateway();
        var factory = new ResponseFactory(
            accessor.Object,
            gateway,
            new DefaultInertiaSerializer(),
            Mock.Of<IOptions<InertiaOptions>>(o => o.Value == new InertiaOptions()),
            Mock.Of<IWebHostEnvironment>());

        Inertia.UseFactory(factory);
        try
        {
            Inertia.DisableSsr();
            Assert.That(gateway.ShouldDispatch(), Is.False);
        }
        finally
        {
            Inertia.ResetFactory();
        }
    }

    [Test]
    public void Facade_WithoutSsr_DelegatesToGateway()
    {
        var (gateway, context, accessor) = BuildGateway(requestPath: "/admin/users");
        var factory = new ResponseFactory(
            accessor.Object,
            gateway,
            new DefaultInertiaSerializer(),
            Mock.Of<IOptions<InertiaOptions>>(o => o.Value == new InertiaOptions()),
            Mock.Of<IWebHostEnvironment>());

        Inertia.UseFactory(factory);
        try
        {
            Inertia.WithoutSsr("admin/*");
            Assert.That(gateway.ShouldDispatch(), Is.False);
        }
        finally
        {
            Inertia.ResetFactory();
        }
    }
}
