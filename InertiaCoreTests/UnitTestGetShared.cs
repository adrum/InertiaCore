using InertiaCore;
using InertiaCore.Models;
using InertiaCore.Ssr;
using InertiaCore.Utils;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using Moq;

namespace InertiaCoreTests;

public class GetSharedTests
{
    private IResponseFactory _factory = null!;
    private HttpContext _httpContext = null!;

    [SetUp]
    public void Setup()
    {
        var request = new Mock<HttpRequest>();
        request.SetupGet(r => r.Headers).Returns(new HeaderDictionary());

        var response = new Mock<HttpResponse>();
        response.SetupGet(r => r.Headers).Returns(new HeaderDictionary());

        var features = new FeatureCollection();

        var httpContextMock = new Mock<HttpContext>();
        httpContextMock.SetupGet(c => c.Request).Returns(request.Object);
        httpContextMock.SetupGet(c => c.Response).Returns(response.Object);
        httpContextMock.SetupGet(c => c.Features).Returns(features);
        _httpContext = httpContextMock.Object;

        var contextAccessor = new Mock<IHttpContextAccessor>();
        contextAccessor.SetupGet(a => a.HttpContext).Returns(_httpContext);

        var httpClientFactory = new Mock<IHttpClientFactory>();
        var serializer = new DefaultInertiaSerializer();
        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(x => x.ContentRootPath).Returns(Path.GetTempPath());
        var options = new Mock<IOptions<InertiaOptions>>();
        options.SetupGet(x => x.Value).Returns(new InertiaOptions());
        var gateway = new Gateway(httpClientFactory.Object, serializer, options.Object, environment.Object, Mock.Of<IHttpContextAccessor>());

        _factory = new ResponseFactory(contextAccessor.Object, gateway, serializer, options.Object, environment.Object);
    }

    [Test]
    [Description("GetShared with no shared data returns an empty dictionary.")]
    public void TestGetSharedReturnsEmptyWhenNothingShared()
    {
        var result = _factory.GetShared();

        Assert.That(result, Is.InstanceOf<IReadOnlyDictionary<string, object?>>());
        Assert.That((IReadOnlyDictionary<string, object?>)result!, Is.Empty);
    }

    [Test]
    [Description("GetShared without a key returns the entire shared props dictionary.")]
    public void TestGetSharedReturnsAllSharedProps()
    {
        _factory.Share("foo", "bar");
        _factory.Share("baz", 42);

        var result = _factory.GetShared() as IReadOnlyDictionary<string, object?>;

        Assert.That(result, Is.Not.Null);
        Assert.That(result!["foo"], Is.EqualTo("bar"));
        Assert.That(result["baz"], Is.EqualTo(42));
    }

    [Test]
    [Description("GetShared with a key returns the shared value at that key.")]
    public void TestGetSharedReturnsValueByKey()
    {
        _factory.Share("foo", "bar");

        var result = _factory.GetShared("foo");

        Assert.That(result, Is.EqualTo("bar"));
    }

    [Test]
    [Description("GetShared returns the default value when the key is missing.")]
    public void TestGetSharedReturnsDefaultForMissingKey()
    {
        _factory.Share("foo", "bar");

        var result = _factory.GetShared("missing", "fallback");

        Assert.That(result, Is.EqualTo("fallback"));
    }

    [Test]
    [Description("GetShared returns null when the key is missing and no default is provided.")]
    public void TestGetSharedReturnsNullForMissingKeyWithoutDefault()
    {
        var result = _factory.GetShared("missing");

        Assert.That(result, Is.Null);
    }

    [Test]
    [Description("GetShared supports nested dot-notation lookup into shared props.")]
    public void TestGetSharedSupportsDotNotation()
    {
        _factory.Share("user", new Dictionary<string, object?>
        {
            ["name"] = "Alice",
        });

        Assert.That(_factory.GetShared("user.name"), Is.EqualTo("Alice"));
        Assert.That(_factory.GetShared("user.email", "none@example.com"), Is.EqualTo("none@example.com"));
        Assert.That(_factory.GetShared("user.profile.missing"), Is.Null);
    }
}
