using InertiaCore;
using InertiaCore.Models;
using InertiaCore.Utils;
using Microsoft.AspNetCore.Http;
using NUnit.Framework;

namespace InertiaCoreTests;

public partial class Tests
{
    [Test]
    public async Task PrependProp_ShouldAppearInPrependProps()
    {
        var headers = new HeaderDictionary
        {
            { "X-Inertia", "true" },
            { "X-Inertia-Partial-Component", "TestComponent" },
            { "X-Inertia-Partial-Data", "items" }
        };

        var context = PrepareContext(headers);

        var mergeProp = _factory.Merge(() => new[] { 1, 2, 3 });
        ((Mergeable)mergeProp).Prepend();

        var response = _factory.Render("TestComponent", new Dictionary<string, object?>
        {
            { "items", mergeProp }
        });

        response.SetContext(context);
        await response.ProcessResponse();

        var result = response.GetJson();
        var page = result.Value as Page;

        Assert.That(page, Is.Not.Null);
        Assert.That(page!.PrependProps, Is.Not.Null);
        Assert.That(page.PrependProps, Does.Contain("items"));
        // Should NOT be in mergeProps (which is for append)
        Assert.That(page.MergeProps, Is.Null.Or.Not.Contain("items"));
    }

    [Test]
    public async Task AppendProp_ShouldAppearInMergeProps()
    {
        var headers = new HeaderDictionary
        {
            { "X-Inertia", "true" },
            { "X-Inertia-Partial-Component", "TestComponent" },
            { "X-Inertia-Partial-Data", "items" }
        };

        var context = PrepareContext(headers);

        var mergeProp = _factory.Merge(() => new[] { 1, 2, 3 });
        // Default is append, no need to call anything

        var response = _factory.Render("TestComponent", new Dictionary<string, object?>
        {
            { "items", mergeProp }
        });

        response.SetContext(context);
        await response.ProcessResponse();

        var result = response.GetJson();
        var page = result.Value as Page;

        Assert.That(page, Is.Not.Null);
        Assert.That(page!.MergeProps, Is.Not.Null);
        Assert.That(page.MergeProps, Does.Contain("items"));
    }

    [Test]
    public async Task MatchPropsOn_ShouldBeFlatList()
    {
        var headers = new HeaderDictionary
        {
            { "X-Inertia", "true" },
            { "X-Inertia-Partial-Component", "TestComponent" },
            { "X-Inertia-Partial-Data", "items" }
        };

        var context = PrepareContext(headers);

        var mergeProp = _factory.Merge(() => new[] { 1, 2, 3 });
        ((Mergeable)mergeProp).MatchesOn("id");

        var response = _factory.Render("TestComponent", new Dictionary<string, object?>
        {
            { "items", mergeProp }
        });

        response.SetContext(context);
        await response.ProcessResponse();

        var result = response.GetJson();
        var page = result.Value as Page;

        Assert.That(page, Is.Not.Null);
        Assert.That(page!.MatchPropsOn, Is.Not.Null);
        // Should be flat format: "items.id"
        Assert.That(page.MatchPropsOn, Does.Contain("items.id"));
    }

    [Test]
    public async Task AppendAtPath_ShouldAppearInMergePropsWithPath()
    {
        var headers = new HeaderDictionary
        {
            { "X-Inertia", "true" },
            { "X-Inertia-Partial-Component", "TestComponent" },
            { "X-Inertia-Partial-Data", "pagination" }
        };

        var context = PrepareContext(headers);

        var mergeProp = _factory.Merge(() => new { data = new[] { 1, 2, 3 } });
        ((Mergeable)mergeProp).AppendAt("data");

        var response = _factory.Render("TestComponent", new Dictionary<string, object?>
        {
            { "pagination", mergeProp }
        });

        response.SetContext(context);
        await response.ProcessResponse();

        var result = response.GetJson();
        var page = result.Value as Page;

        Assert.That(page, Is.Not.Null);
        Assert.That(page!.MergeProps, Is.Not.Null);
        Assert.That(page.MergeProps, Does.Contain("pagination.data"));
    }
}
