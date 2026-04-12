using InertiaCore;
using InertiaCore.Models;

namespace InertiaCoreTests;

public partial class Tests
{
    private enum Pages
    {
        Home,
        UserList,
        UserEdit,
    }

    [Test]
    [Description("Test rendering with an enum component resolves via ToString().")]
    public async Task TestRenderEnumComponent()
    {
        var response = _factory.Render(Pages.Home, new { test = "value" });

        var context = PrepareContext();
        response.SetContext(context);

        await response.ProcessResponse();

        var page = response.GetJson().Value as Page;

        Assert.That(page?.Component, Is.EqualTo("Home"));
        Assert.That(page?.Props, Contains.Key("test"));
    }

    [Test]
    [Description("Test rendering with an enum component via the Inertia facade.")]
    public async Task TestRenderEnumComponentViaFacade()
    {
        Inertia.UseFactory(_factory);
        try
        {
            var response = Inertia.Render(Pages.UserList);

            var context = PrepareContext();
            response.SetContext(context);

            await response.ProcessResponse();

            var page = response.GetJson().Value as Page;

            Assert.That(page?.Component, Is.EqualTo("UserList"));
        }
        finally
        {
            Inertia.ResetFactory();
        }
    }

    [Test]
    [Description("Test rendering with an enum component and null props matches string overload behavior.")]
    public async Task TestRenderEnumComponentWithNullProps()
    {
        var enumResponse = _factory.Render(Pages.UserEdit);
        var stringResponse = _factory.Render("UserEdit");

        var enumContext = PrepareContext();
        enumResponse.SetContext(enumContext);
        await enumResponse.ProcessResponse();

        var stringContext = PrepareContext();
        stringResponse.SetContext(stringContext);
        await stringResponse.ProcessResponse();

        var enumPage = enumResponse.GetJson().Value as Page;
        var stringPage = stringResponse.GetJson().Value as Page;

        Assert.That(enumPage?.Component, Is.EqualTo(stringPage?.Component));
        Assert.That(enumPage?.Component, Is.EqualTo("UserEdit"));
    }
}
