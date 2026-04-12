using System.Text;
using InertiaCore;
using InertiaCore.Extensions;
using InertiaCore.Models;
using InertiaCore.Ssr;
using InertiaCore.Utils;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;

namespace InertiaCoreTests;

public class MapInertiaTests
{
    private IResponseFactory _factory = null!;

    [SetUp]
    public void Setup()
    {
        var contextAccessor = new Mock<IHttpContextAccessor>();
        var httpClientFactory = new Mock<IHttpClientFactory>();
        var serializer = new DefaultInertiaSerializer();
        var gateway = new Gateway(httpClientFactory.Object, serializer);
        var options = new Mock<IOptions<InertiaOptions>>();
        options.SetupGet(x => x.Value).Returns(new InertiaOptions());

        _factory = new ResponseFactory(contextAccessor.Object, gateway, serializer, options.Object);
        Inertia.UseFactory(_factory);
    }

    [TearDown]
    public void TearDown()
    {
        // Restore the static factory to its uninitialized state so other test
        // fixtures (e.g. TestConfiguration) that assert on a fresh state still pass.
        Inertia.UseFactory(null!);
    }

    private sealed class StubEndpointRouteBuilder : IEndpointRouteBuilder
    {
        private readonly List<EndpointDataSource> _dataSources = new();
        public IServiceProvider ServiceProvider { get; }
        public ICollection<EndpointDataSource> DataSources => _dataSources;

        public StubEndpointRouteBuilder(IServiceProvider sp) => ServiceProvider = sp;

        public IApplicationBuilder CreateApplicationBuilder() => new ApplicationBuilder(ServiceProvider);
    }

    private static IServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRouting();
        services.AddOptions();
        services.AddMvcCore();
        return services.BuildServiceProvider();
    }

    private static IEndpointRouteBuilder CreateEndpoints(IServiceProvider provider)
    {
        return new StubEndpointRouteBuilder(provider);
    }

    private static RouteEndpoint GetRegisteredEndpoint(IEndpointRouteBuilder endpoints)
    {
        var endpoint = endpoints.DataSources
            .SelectMany(d => d.Endpoints)
            .OfType<RouteEndpoint>()
            .Single();
        return endpoint;
    }

    private static HttpContext BuildHttpContext(IServiceProvider services, string path, IHeaderDictionary? headers = null)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = services,
        };
        context.Request.Method = "GET";
        context.Request.Path = path;
        if (headers != null)
        {
            foreach (var h in headers)
                context.Request.Headers[h.Key] = h.Value;
        }
        context.Response.Body = new MemoryStream();
        context.Features.Set<IRoutingFeature>(new RoutingFeature { RouteData = new RouteData() });
        return context;
    }

    private sealed class RoutingFeature : IRoutingFeature
    {
        public RouteData? RouteData { get; set; }
    }

    [Test]
    [Description("MapInertia returns an IEndpointConventionBuilder so callers can chain metadata.")]
    public void TestMapInertiaReturnsConventionBuilder()
    {
        var endpoints = CreateEndpoints(BuildServices());

        IEndpointConventionBuilder builder = endpoints.MapInertia("/about", "About");

        Assert.That(builder, Is.Not.Null);
    }

    [Test]
    [Description("MapInertia registers a GET endpoint at the given pattern.")]
    public void TestMapInertiaRegistersGetEndpoint()
    {
        var endpoints = CreateEndpoints(BuildServices());

        endpoints.MapInertia("/about", "About");

        var endpoint = GetRegisteredEndpoint(endpoints);
        Assert.That(endpoint.RoutePattern.RawText, Is.EqualTo("/about"));

        var methodMetadata = endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>();
        Assert.That(methodMetadata, Is.Not.Null);
        Assert.That(methodMetadata!.HttpMethods, Does.Contain("GET"));
    }

    [Test]
    [Description("MapInertia renders the component as an Inertia JSON payload for XHR requests.")]
    public async Task TestMapInertiaRendersInertiaJsonResponse()
    {
        var services = BuildServices();
        var endpoints = CreateEndpoints(services);
        endpoints.MapInertia("/about", "About");

        var endpoint = GetRegisteredEndpoint(endpoints);

        var headers = new HeaderDictionary
        {
            { InertiaHeader.Inertia, "true" },
            { InertiaHeader.Version, "" },
        };
        var context = BuildHttpContext(services, "/about", headers);

        await endpoint.RequestDelegate!(context);

        Assert.That(context.Response.StatusCode, Is.EqualTo(200));
        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body, Encoding.UTF8).ReadToEndAsync();
        Assert.That(body, Does.Contain("\"component\":\"About\""));
    }

    [Test]
    [Description("MapInertia passes static props into the rendered Inertia payload.")]
    public async Task TestMapInertiaIncludesStaticProps()
    {
        var services = BuildServices();
        var endpoints = CreateEndpoints(services);
        endpoints.MapInertia("/greet", "Greeting", new { name = "Alice" });

        var endpoint = GetRegisteredEndpoint(endpoints);

        var headers = new HeaderDictionary
        {
            { InertiaHeader.Inertia, "true" },
            { InertiaHeader.Version, "" },
        };
        var context = BuildHttpContext(services, "/greet", headers);

        await endpoint.RequestDelegate!(context);

        Assert.That(context.Response.StatusCode, Is.EqualTo(200));
        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body, Encoding.UTF8).ReadToEndAsync();
        Assert.That(body, Does.Contain("\"component\":\"Greeting\""));
        Assert.That(body, Does.Contain("\"name\":\"Alice\""));
    }
}
