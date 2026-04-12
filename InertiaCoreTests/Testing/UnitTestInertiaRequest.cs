using InertiaCore.Testing;
using InertiaCore.Utils;

namespace InertiaCoreTests.Testing;

public class UnitTestInertiaRequest
{
    private static string? GetHeader(HttpRequestMessage message, string name)
    {
        if (!message.Headers.TryGetValues(name, out var values)) return null;
        return string.Join(",", values);
    }

    [Test]
    public void Get_produces_base_headers()
    {
        var message = InertiaRequest.Get("/users").Build();

        Assert.That(message.Method, Is.EqualTo(HttpMethod.Get));
        Assert.That(message.RequestUri!.ToString(), Is.EqualTo("/users"));
        Assert.That(GetHeader(message, InertiaHeader.Inertia), Is.EqualTo("true"));
        Assert.That(GetHeader(message, InertiaHeader.Version), Is.EqualTo(""));
        Assert.That(message.Headers.Contains(InertiaHeader.PartialComponent), Is.False);
    }

    [Test]
    public void Partial_sets_partial_component_header()
    {
        var message = InertiaRequest.Partial("/users", "Users/Index").Build();

        Assert.That(GetHeader(message, InertiaHeader.Inertia), Is.EqualTo("true"));
        Assert.That(GetHeader(message, InertiaHeader.PartialComponent),
            Is.EqualTo("Users/Index"));
    }

    [Test]
    public void Only_sets_partial_data_header()
    {
        var message = InertiaRequest.Partial("/users", "Users/Index")
            .Only("users", "filters")
            .Build();

        Assert.That(GetHeader(message, InertiaHeader.PartialOnly),
            Is.EqualTo("users,filters"));
    }

    [Test]
    public void Except_sets_partial_except_header()
    {
        var message = InertiaRequest.Partial("/users", "Users/Index")
            .Except("debug", "internal")
            .Build();

        Assert.That(GetHeader(message, InertiaHeader.PartialExcept),
            Is.EqualTo("debug,internal"));
    }

    [Test]
    public void ErrorBag_sets_error_bag_header()
    {
        var message = InertiaRequest.Get("/users")
            .ErrorBag("register")
            .Build();

        Assert.That(GetHeader(message, InertiaHeader.ErrorBag), Is.EqualTo("register"));
    }

    [Test]
    public void Version_overrides_version_header()
    {
        var message = InertiaRequest.Get("/users")
            .Version("abc123")
            .Build();

        Assert.That(GetHeader(message, InertiaHeader.Version), Is.EqualTo("abc123"));
    }

    [Test]
    public void Reset_sets_reset_header()
    {
        var message = InertiaRequest.Partial("/users", "Users/Index")
            .Reset("users", "filters")
            .Build();

        Assert.That(GetHeader(message, InertiaHeader.Reset),
            Is.EqualTo("users,filters"));
    }

    [Test]
    public void Header_sets_arbitrary_header()
    {
        var message = InertiaRequest.Get("/users")
            .Header("X-Custom", "value")
            .Build();

        Assert.That(GetHeader(message, "X-Custom"), Is.EqualTo("value"));
    }

    [Test]
    public void Header_overrides_existing_header()
    {
        var message = InertiaRequest.Get("/users")
            .Header(InertiaHeader.Version, "override")
            .Build();

        Assert.That(GetHeader(message, InertiaHeader.Version), Is.EqualTo("override"));
    }

    [Test]
    public void Header_throws_on_empty_name()
    {
        Assert.Throws<ArgumentException>(
            () => InertiaRequest.Get("/").Header("", "v"));
    }

    [Test]
    public void Builder_is_chainable_and_composes_all_headers()
    {
        var message = InertiaRequest.Partial("/users", "Users/Index")
            .Only("users", "filters")
            .Except("debug")
            .ErrorBag("register")
            .Version("abc123")
            .Reset("filters")
            .Header("X-Custom", "v")
            .Build();

        Assert.Multiple(() =>
        {
            Assert.That(GetHeader(message, InertiaHeader.Inertia), Is.EqualTo("true"));
            Assert.That(GetHeader(message, InertiaHeader.PartialComponent), Is.EqualTo("Users/Index"));
            Assert.That(GetHeader(message, InertiaHeader.PartialOnly), Is.EqualTo("users,filters"));
            Assert.That(GetHeader(message, InertiaHeader.PartialExcept), Is.EqualTo("debug"));
            Assert.That(GetHeader(message, InertiaHeader.ErrorBag), Is.EqualTo("register"));
            Assert.That(GetHeader(message, InertiaHeader.Version), Is.EqualTo("abc123"));
            Assert.That(GetHeader(message, InertiaHeader.Reset), Is.EqualTo("filters"));
            Assert.That(GetHeader(message, "X-Custom"), Is.EqualTo("v"));
        });
    }

    [Test]
    public void Build_produces_independent_messages()
    {
        var builder = InertiaRequest.Get("/users").Version("v1");
        var first = builder.Build();
        var second = builder.Build();

        Assert.That(first, Is.Not.SameAs(second));
        Assert.That(GetHeader(first, InertiaHeader.Version), Is.EqualTo("v1"));
        Assert.That(GetHeader(second, InertiaHeader.Version), Is.EqualTo("v1"));
    }
}
