using InertiaCore;
using InertiaCore.Models;
using Microsoft.AspNetCore.Http;
using NUnit.Framework;

namespace InertiaCoreTests;

public partial class Tests
{
    [Test]
    public async Task Cache_ShouldSetCacheDirectionsInPage()
    {
        var context = PrepareContext();
        var response = _factory.Render("TestComponent", new Dictionary<string, object?>
        {
            { "data", "value" }
        }).Cache(300);

        response.SetContext(context);
        await response.ProcessResponse();

        var result = response.GetJson();
        var page = result.Value as Page;

        Assert.That(page, Is.Not.Null);
        Assert.That(page!.Cache, Is.Not.Null);
        Assert.That(page.Cache, Does.Contain(300));
    }

    [Test]
    public async Task Cache_ShouldSupportMultipleDurations()
    {
        var context = PrepareContext();
        var response = _factory.Render("TestComponent", new Dictionary<string, object?>
        {
            { "data", "value" }
        }).Cache(300, 600);

        response.SetContext(context);
        await response.ProcessResponse();

        var result = response.GetJson();
        var page = result.Value as Page;

        Assert.That(page, Is.Not.Null);
        Assert.That(page!.Cache, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task Cache_ShouldSupportTimeSpan()
    {
        var context = PrepareContext();
        var response = _factory.Render("TestComponent", new Dictionary<string, object?>
        {
            { "data", "value" }
        }).Cache(TimeSpan.FromMinutes(5));

        response.SetContext(context);
        await response.ProcessResponse();

        var result = response.GetJson();
        var page = result.Value as Page;

        Assert.That(page, Is.Not.Null);
        Assert.That(page!.Cache, Does.Contain(300));
    }

    [Test]
    public async Task Cache_ShouldBeNullWhenNotSet()
    {
        var context = PrepareContext();
        var response = _factory.Render("TestComponent", new Dictionary<string, object?>
        {
            { "data", "value" }
        });

        response.SetContext(context);
        await response.ProcessResponse();

        var result = response.GetJson();
        var page = result.Value as Page;

        Assert.That(page, Is.Not.Null);
        Assert.That(page!.Cache, Is.Null);
    }
}
