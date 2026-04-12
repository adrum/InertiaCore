using InertiaCore.Models;
using InertiaCore.Utils;
using Microsoft.AspNetCore.Http;

namespace InertiaCoreTests;

public partial class Tests
{
    [Test]
    [Description("Top-level dot-notation keys are unpacked into nested dictionaries.")]
    public async Task TestDotNotationTopLevelKeyUnpacks()
    {
        var response = _factory.Render("Test/Page", new Dictionary<string, object?>
        {
            ["user.name"] = "Alice",
            ["user.email"] = "alice@example.com",
            ["other"] = "value"
        });

        var context = PrepareContext();
        response.SetContext(context);
        await response.ProcessResponse();

        var page = response.GetJson().Value as Page;

        Assert.That(page?.Props, Does.ContainKey("user"));
        var user = page?.Props["user"] as Dictionary<string, object?>;
        Assert.That(user, Is.Not.Null);
        Assert.That(user!["name"], Is.EqualTo("Alice"));
        Assert.That(user["email"], Is.EqualTo("alice@example.com"));
        Assert.That(page?.Props["other"], Is.EqualTo("value"));
    }

    [Test]
    [Description("Nested dot-notation keys unpack under an existing nested dictionary.")]
    public async Task TestDotNotationUnpacksIntoExistingDict()
    {
        var response = _factory.Render("Test/Page", new Dictionary<string, object?>
        {
            ["user"] = new Dictionary<string, object?> { ["name"] = "Alice" },
            ["user.email"] = "alice@example.com",
        });

        var context = PrepareContext();
        response.SetContext(context);
        await response.ProcessResponse();

        var page = response.GetJson().Value as Page;
        var user = page?.Props["user"] as Dictionary<string, object?>;
        Assert.That(user, Is.Not.Null);
        Assert.That(user!["name"], Is.EqualTo("Alice"));
        Assert.That(user["email"], Is.EqualTo("alice@example.com"));
    }

    [Test]
    [Description("Shared prop top-level keys are emitted as Page.SharedProps metadata.")]
    public async Task TestSharedPropsMetadata()
    {
        var shared = new InertiaSharedProps();
        shared.Set("user", "Alice");
        shared.Set("flash", new Dictionary<string, object?> { ["ok"] = true });

        var response = _factory.Render("Test/Page", new { Page = "Home" });

        var context = PrepareContext(null, shared);
        response.SetContext(context);
        await response.ProcessResponse();

        var page = response.GetJson().Value as Page;

        Assert.That(page?.SharedProps, Is.Not.Null);
        Assert.That(page!.SharedProps, Does.Contain("user"));
        Assert.That(page.SharedProps, Does.Contain("flash"));
        // Page-level props should NOT be in sharedProps metadata.
        Assert.That(page.SharedProps, Does.Not.Contain("page"));
    }

    [Test]
    [Description("A MergeProp nested inside a plain dictionary contributes metadata with the correct dot-path.")]
    public async Task TestMergePropAtNestedPath()
    {
        var response = _factory.Render("Test/Page", new Dictionary<string, object?>
        {
            ["data"] = new Dictionary<string, object?>
            {
                ["users"] = _factory.Merge(() => new[] { "Alice", "Bob" })
            }
        });

        var context = PrepareContext();
        response.SetContext(context);
        await response.ProcessResponse();

        var page = response.GetJson().Value as Page;

        Assert.That(page?.MergeProps, Is.Not.Null);
        Assert.That(page!.MergeProps, Does.Contain("data.users"));

        var data = page.Props["data"] as Dictionary<string, object?>;
        Assert.That(data, Is.Not.Null);
        Assert.That(data!["users"], Is.EqualTo(new[] { "Alice", "Bob" }));
    }

    [Test]
    [Description("A OnceProp nested inside a plain dictionary contributes once metadata with the correct dot-path.")]
    public async Task TestNestedOncePropWorksAtDepth()
    {
        var response = _factory.Render("Test/Page", new Dictionary<string, object?>
        {
            ["data"] = new Dictionary<string, object?>
            {
                ["stats"] = _factory.Once(() => "cached-stats")
            }
        });

        var context = PrepareContext();
        response.SetContext(context);
        await response.ProcessResponse();

        var page = response.GetJson().Value as Page;

        Assert.That(page?.OnceProps, Is.Not.Null);
        Assert.That(page!.OnceProps, Does.ContainKey("data.stats"));

        var metadata = page.OnceProps!["data.stats"] as Dictionary<string, object?>;
        Assert.That(metadata, Is.Not.Null);
        Assert.That(metadata!["prop"], Is.EqualTo("data.stats"));

        var data = page.Props["data"] as Dictionary<string, object?>;
        Assert.That(data, Is.Not.Null);
        Assert.That(data!["stats"], Is.EqualTo("cached-stats"));
    }

    [Test]
    [Description("Partial reload with dot-notation 'only' header walks to a nested leaf.")]
    public async Task TestPartialReloadBidirectionalPrefixMatch()
    {
        var response = _factory.Render("Test/Page", new Dictionary<string, object?>
        {
            ["user"] = new Dictionary<string, object?>
            {
                ["profile"] = new Dictionary<string, object?>
                {
                    ["name"] = "Alice",
                    ["email"] = "alice@example.com"
                },
                ["id"] = 1
            },
            ["other"] = "value"
        });

        var headers = new HeaderDictionary
        {
            { "X-Inertia-Partial-Data", "user.profile.name" },
            { "X-Inertia-Partial-Component", "Test/Page" }
        };

        var context = PrepareContext(headers);
        response.SetContext(context);
        await response.ProcessResponse();

        var page = response.GetJson().Value as Page;

        var user = page?.Props["user"] as Dictionary<string, object?>;
        Assert.That(user, Is.Not.Null);
        var profile = user!["profile"] as Dictionary<string, object?>;
        Assert.That(profile, Is.Not.Null);
        Assert.That(profile!["name"], Is.EqualTo("Alice"));
        Assert.That(profile.ContainsKey("email"), Is.False);
        Assert.That(user.ContainsKey("id"), Is.False);
        Assert.That(page!.Props.ContainsKey("other"), Is.False);
    }

    [Test]
    [Description("Wire-format regression: plain props render identically to the pre-refactor output.")]
    public async Task TestPlainPropsWireFormatRegression()
    {
        var response = _factory.Render("Home", new { test = "value", Count = 42 });

        var context = PrepareContext();
        response.SetContext(context);
        await response.ProcessResponse();

        var page = response.GetJson().Value as Page;

        Assert.That(page?.Props, Is.EqualTo(new Dictionary<string, object?>
        {
            { "test", "value" },
            { "count", 42 },
            { "errors", new Dictionary<string, string>(0) }
        }));
        Assert.That(page?.MergeProps, Is.Null);
        Assert.That(page?.DeferredProps, Is.Null);
        Assert.That(page?.OnceProps, Is.Null);
        Assert.That(page?.SharedProps, Is.Null);
    }
}
