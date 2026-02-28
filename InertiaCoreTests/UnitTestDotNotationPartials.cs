using InertiaCore.Models;
using InertiaCore.Utils;
using Microsoft.AspNetCore.Http;

namespace InertiaCoreTests;

public partial class Tests
{
    [Test]
    [Description("DotNotationHelper.Get returns a nested value using dot notation.")]
    public void TestDotNotationGet()
    {
        var dict = new Dictionary<string, object?>
        {
            ["User"] = new Dictionary<string, object?>
            {
                ["Name"] = "John",
                ["Address"] = new Dictionary<string, object?>
                {
                    ["City"] = "New York"
                }
            }
        };

        Assert.That(DotNotationHelper.Get(dict, "User.Name"), Is.EqualTo("John"));
        Assert.That(DotNotationHelper.Get(dict, "User.Address.City"), Is.EqualTo("New York"));
        Assert.That(DotNotationHelper.Get(dict, "User.Missing"), Is.Null);
        Assert.That(DotNotationHelper.Get(dict, "Missing.Key"), Is.Null);
    }

    [Test]
    [Description("DotNotationHelper.Set creates a nested dictionary structure.")]
    public void TestDotNotationSet()
    {
        var dict = new Dictionary<string, object?>();

        DotNotationHelper.Set(dict, "User.Name", "John");
        DotNotationHelper.Set(dict, "User.Address.City", "New York");

        Assert.That(DotNotationHelper.Get(dict, "User.Name"), Is.EqualTo("John"));
        Assert.That(DotNotationHelper.Get(dict, "User.Address.City"), Is.EqualTo("New York"));

        var user = dict["User"] as Dictionary<string, object?>;
        Assert.That(user, Is.Not.Null);
        Assert.That(user!["Name"], Is.EqualTo("John"));

        var address = user["Address"] as Dictionary<string, object?>;
        Assert.That(address, Is.Not.Null);
        Assert.That(address!["City"], Is.EqualTo("New York"));
    }

    [Test]
    [Description("DotNotationHelper.Forget removes a nested key.")]
    public void TestDotNotationForget()
    {
        var dict = new Dictionary<string, object?>
        {
            ["User"] = new Dictionary<string, object?>
            {
                ["Name"] = "John",
                ["Email"] = "john@example.com"
            }
        };

        DotNotationHelper.Forget(dict, "User.Email");

        Assert.That(DotNotationHelper.Get(dict, "User.Name"), Is.EqualTo("John"));
        Assert.That(DotNotationHelper.Get(dict, "User.Email"), Is.Null);

        var user = dict["User"] as Dictionary<string, object?>;
        Assert.That(user, Is.Not.Null);
        Assert.That(user!.ContainsKey("Email"), Is.False);
        Assert.That(user.ContainsKey("Name"), Is.True);
    }

    [Test]
    [Description("Partial reload with dot-notation in X-Inertia-Partial-Data returns nested structure.")]
    public async Task TestPartialOnlyDotNotation()
    {
        var response = _factory.Render("Test/Page", new Dictionary<string, object?>
        {
            ["User"] = new Dictionary<string, object?>
            {
                ["Name"] = "John",
                ["Email"] = "john@example.com"
            },
            ["Other"] = "Data"
        });

        var headers = new HeaderDictionary
        {
            { "X-Inertia-Partial-Data", "User.Name" },
            { "X-Inertia-Partial-Component", "Test/Page" }
        };

        var context = PrepareContext(headers);

        response.SetContext(context);
        await response.ProcessResponse();

        var page = response.GetJson().Value as Page;

        Assert.That(page?.Props, Is.EqualTo(new Dictionary<string, object?>
        {
            {
                "user", new Dictionary<string, object?>
                {
                    { "name", "John" }
                }
            },
            { "errors", new Dictionary<string, string>(0) }
        }));
    }

    [Test]
    [Description("Partial reload with dot-notation in X-Inertia-Partial-Except removes nested key.")]
    public async Task TestPartialExceptDotNotation()
    {
        var response = _factory.Render("Test/Page", new Dictionary<string, object?>
        {
            ["User"] = new Dictionary<string, object?>
            {
                ["Name"] = "John",
                ["Email"] = "john@example.com"
            },
            ["Other"] = "Data"
        });

        var headers = new HeaderDictionary
        {
            { "X-Inertia-Partial-Except", "User.Email" },
            { "X-Inertia-Partial-Component", "Test/Page" }
        };

        var context = PrepareContext(headers);

        response.SetContext(context);
        await response.ProcessResponse();

        var page = response.GetJson().Value as Page;

        Assert.That(page?.Props, Is.EqualTo(new Dictionary<string, object?>
        {
            {
                "user", new Dictionary<string, object?>
                {
                    { "name", "John" }
                }
            },
            { "other", "Data" },
            { "errors", new Dictionary<string, string>(0) }
        }));
    }
}
