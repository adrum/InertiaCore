using InertiaCore;
using InertiaCore.Models;
using InertiaCore.Props;
using InertiaCore.Utils;
using Microsoft.AspNetCore.Http;
using NUnit.Framework;

namespace InertiaCoreTests;

public partial class Tests
{
    [Test]
    public async Task ScrollProp_ShouldBeExcludedOnFirstLoad()
    {
        var context = PrepareContext();

        var metadata = new ScrollMetadata("page", null, 2, 1);
        var response = _factory.Render("TestComponent", new Dictionary<string, object?>
        {
            { "items", _factory.Scroll(() => (object?)new[] { 1, 2, 3 }, "data", metadata) },
            { "other", "value" }
        });

        response.SetContext(context);
        await response.ProcessResponse();

        var result = response.GetJson();
        var page = result.Value as Page;

        Assert.That(page, Is.Not.Null);
        // ScrollProp implements IIgnoresFirstLoad, so excluded on first load
        Assert.That(page!.Props.ContainsKey("items"), Is.False);
        Assert.That(page.Props.ContainsKey("other"), Is.True);
    }

    [Test]
    public async Task ScrollProp_ShouldBeIncludedOnPartialReload()
    {
        var headers = new HeaderDictionary
        {
            { "X-Inertia", "true" },
            { "X-Inertia-Partial-Component", "TestComponent" },
            { "X-Inertia-Partial-Data", "items" }
        };
        var context = PrepareContext(headers);

        var metadata = new ScrollMetadata("page", null, 2, 1);
        var response = _factory.Render("TestComponent", new Dictionary<string, object?>
        {
            { "items", _factory.Scroll(() => (object?)new[] { 1, 2, 3 }, "data", metadata) }
        });

        response.SetContext(context);
        await response.ProcessResponse();

        var result = response.GetJson();
        var page = result.Value as Page;

        Assert.That(page, Is.Not.Null);
        Assert.That(page!.Props.ContainsKey("items"), Is.True);
    }

    [Test]
    public async Task ScrollProp_ShouldGenerateScrollMetadata()
    {
        var headers = new HeaderDictionary
        {
            { "X-Inertia", "true" },
            { "X-Inertia-Partial-Component", "TestComponent" },
            { "X-Inertia-Partial-Data", "items" }
        };
        var context = PrepareContext(headers);

        var metadata = new ScrollMetadata("page", null, 2, 1);
        var response = _factory.Render("TestComponent", new Dictionary<string, object?>
        {
            { "items", _factory.Scroll(() => (object?)new[] { 1, 2, 3 }, "data", metadata) }
        });

        response.SetContext(context);
        await response.ProcessResponse();

        var result = response.GetJson();
        var page = result.Value as Page;

        Assert.That(page, Is.Not.Null);
        Assert.That(page!.ScrollProps, Is.Not.Null);
        Assert.That(page.ScrollProps!.ContainsKey("items"), Is.True);
    }

    [Test]
    public async Task ScrollProp_ShouldAppendByDefault()
    {
        var headers = new HeaderDictionary
        {
            { "X-Inertia", "true" },
            { "X-Inertia-Partial-Component", "TestComponent" },
            { "X-Inertia-Partial-Data", "items" }
        };
        var context = PrepareContext(headers);

        var metadata = new ScrollMetadata("page", null, 2, 1);
        var response = _factory.Render("TestComponent", new Dictionary<string, object?>
        {
            { "items", _factory.Scroll(() => (object?)new[] { 1, 2, 3 }, "data", metadata) }
        });

        response.SetContext(context);
        await response.ProcessResponse();

        var result = response.GetJson();
        var page = result.Value as Page;

        Assert.That(page, Is.Not.Null);
        // Default is append, so should be in MergeProps (append list)
        Assert.That(page!.MergeProps, Is.Not.Null);
        Assert.That(page.MergeProps, Does.Contain("items.data"));
    }

    [Test]
    public async Task ScrollProp_PrependIntent_ShouldUsePrependStrategy()
    {
        var headers = new HeaderDictionary
        {
            { "X-Inertia", "true" },
            { "X-Inertia-Partial-Component", "TestComponent" },
            { "X-Inertia-Partial-Data", "items" },
            { "X-Inertia-Infinite-Scroll-Merge-Intent", "prepend" }
        };
        var context = PrepareContext(headers);

        var metadata = new ScrollMetadata("page", 1, null, 2);
        var response = _factory.Render("TestComponent", new Dictionary<string, object?>
        {
            { "items", _factory.Scroll(() => (object?)new[] { 4, 5, 6 }, "data", metadata) }
        });

        response.SetContext(context);
        await response.ProcessResponse();

        var result = response.GetJson();
        var page = result.Value as Page;

        Assert.That(page, Is.Not.Null);
        // Prepend intent should put it in PrependProps
        Assert.That(page!.PrependProps, Is.Not.Null);
        Assert.That(page.PrependProps, Does.Contain("items.data"));
    }
}
