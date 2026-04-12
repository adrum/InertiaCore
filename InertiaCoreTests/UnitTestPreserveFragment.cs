using InertiaCore;
using InertiaCore.Models;
using Microsoft.AspNetCore.Mvc;

namespace InertiaCoreTests;

public partial class Tests
{
    [Test]
    [Description("Without calling PreserveFragment(), the page's PreserveFragment is null and omitted from JSON.")]
    public async Task TestPreserveFragmentDefault()
    {
        var response = _factory.Render("Test/Page");

        var context = PrepareContext();
        response.SetContext(context);
        await response.ProcessResponse();

        var result = response.GetResult();
        var page = (result as ViewResult)?.Model as Page;

        Assert.That(page, Is.Not.Null);
        Assert.That(page!.PreserveFragment, Is.Null);
    }

    [Test]
    [Description("Calling factory.PreserveFragment() surfaces preserveFragment=true on the page response.")]
    public async Task TestPreserveFragmentViaFactory()
    {
        _factory.PreserveFragment();

        var response = _factory.Render("Test/Page");

        var context = PrepareContext();
        response.SetContext(context);
        await response.ProcessResponse();

        var result = response.GetResult();
        var page = (result as ViewResult)?.Model as Page;

        Assert.That(page, Is.Not.Null);
        Assert.That(page!.PreserveFragment, Is.True);
    }

    [Test]
    [Description("Calling Inertia.PreserveFragment() via the static facade surfaces preserveFragment=true.")]
    public async Task TestPreserveFragmentViaFacade()
    {
        Inertia.UseFactory(_factory);
        try
        {
            Inertia.PreserveFragment();

            var response = Inertia.Render("Test/Page");

            var context = PrepareContext();
            response.SetContext(context);
            await response.ProcessResponse();

            var result = response.GetResult();
            var page = (result as ViewResult)?.Model as Page;

            Assert.That(page, Is.Not.Null);
            Assert.That(page!.PreserveFragment, Is.True);
        }
        finally
        {
            Inertia.ResetFactory();
        }
    }
}
