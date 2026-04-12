using InertiaCore.Models;
using Microsoft.AspNetCore.Http;

namespace InertiaCoreTests;

public partial class Tests
{
    [Test]
    [Description("Test that OnceProp is included on first load when no except header is present.")]
    public async Task TestOncePropIncludedOnFirstLoad()
    {
        var response = _factory.Render("Test/Page", new
        {
            Test = "Test",
            TestOnce = _factory.Once(() => "OnceValue")
        });

        var context = PrepareContext();

        response.SetContext(context);
        await response.ProcessResponse();

        var page = response.GetJson().Value as Page;

        Assert.That(page?.Props, Does.ContainKey("test"));
        Assert.That(page?.Props["test"], Is.EqualTo("Test"));
        Assert.That(page?.Props, Does.ContainKey("testOnce"));
        Assert.That(page?.Props["testOnce"], Is.EqualTo("OnceValue"));
    }

    [Test]
    [Description("Test that OnceProp is excluded when listed in X-Inertia-Except-Once-Props header.")]
    public async Task TestOncePropExcludedWhenInExceptHeader()
    {
        var response = _factory.Render("Test/Page", new
        {
            Test = "Test",
            TestOnce = _factory.Once(() => "OnceValue")
        });

        var headers = new HeaderDictionary
        {
            { "X-Inertia", "true" },
            { "X-Inertia-Except-Once-Props", "TestOnce" }
        };

        var context = PrepareContext(headers);

        response.SetContext(context);
        await response.ProcessResponse();

        var page = response.GetJson().Value as Page;

        Assert.That(page?.Props, Does.ContainKey("test"));
        Assert.That(page?.Props, Does.Not.ContainKey("testOnce"));
    }

    [Test]
    [Description("Test that OnceProp with a custom key uses that key for exclusion.")]
    public async Task TestOncePropCustomKeyExclusion()
    {
        var onceProp = _factory.Once(() => "OnceValue");
        onceProp.As("customKey");

        var response = _factory.Render("Test/Page", new
        {
            Test = "Test",
            TestOnce = onceProp
        });

        var headers = new HeaderDictionary
        {
            { "X-Inertia", "true" },
            { "X-Inertia-Except-Once-Props", "customKey" }
        };

        var context = PrepareContext(headers);

        response.SetContext(context);
        await response.ProcessResponse();

        var page = response.GetJson().Value as Page;

        Assert.That(page?.Props, Does.ContainKey("test"));
        Assert.That(page?.Props, Does.Not.ContainKey("testOnce"));
    }

    [Test]
    [Description("Test that OnceProp with Fresh() is NOT excluded even when in except header.")]
    public async Task TestOncePropFreshNotExcluded()
    {
        var onceProp = _factory.Once(() => "FreshOnceValue");
        onceProp.Fresh();

        var response = _factory.Render("Test/Page", new
        {
            Test = "Test",
            TestOnce = onceProp
        });

        var headers = new HeaderDictionary
        {
            { "X-Inertia", "true" },
            { "X-Inertia-Except-Once-Props", "TestOnce" }
        };

        var context = PrepareContext(headers);

        response.SetContext(context);
        await response.ProcessResponse();

        var page = response.GetJson().Value as Page;

        Assert.That(page?.Props, Does.ContainKey("test"));
        Assert.That(page?.Props, Does.ContainKey("testOnce"));
        Assert.That(page?.Props["testOnce"], Is.EqualTo("FreshOnceValue"));
    }

    [Test]
    [Description("Test that OnceProps metadata is generated in page object.")]
    public async Task TestOncePropsMetadataGenerated()
    {
        var response = _factory.Render("Test/Page", new
        {
            Test = "Test",
            TestOnce = _factory.Once(() => "OnceValue")
        });

        var context = PrepareContext();

        response.SetContext(context);
        await response.ProcessResponse();

        var page = response.GetJson().Value as Page;

        Assert.That(page?.OnceProps, Is.Not.Null);
        Assert.That(page?.OnceProps, Does.ContainKey("testOnce"));

        var metadata = page?.OnceProps!["testOnce"] as Dictionary<string, object?>;
        Assert.That(metadata, Is.Not.Null);
        Assert.That(metadata!["prop"], Is.EqualTo("testOnce"));
    }

    [Test]
    [Description("Test that OnceProp with a TTL emits expiresAt as a future Unix-ms timestamp.")]
    public async Task TestOncePropExpiresAtIsFutureMillisTimestamp()
    {
        var onceProp = _factory.Once(() => "OnceValue");
        onceProp.Until(TimeSpan.FromMinutes(5));

        var response = _factory.Render("Test/Page", new
        {
            Test = "Test",
            TestOnce = onceProp
        });

        var context = PrepareContext();

        response.SetContext(context);
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await response.ProcessResponse();

        var page = response.GetJson().Value as Page;

        Assert.That(page?.OnceProps, Is.Not.Null);
        Assert.That(page?.OnceProps, Does.ContainKey("testOnce"));

        var metadata = page?.OnceProps!["testOnce"] as Dictionary<string, object?>;
        Assert.That(metadata, Is.Not.Null);
        Assert.That(metadata!["prop"], Is.EqualTo("testOnce"));
        Assert.That(metadata, Does.ContainKey("expiresAt"));

        var expiresAtValue = metadata["expiresAt"];
        Assert.That(expiresAtValue, Is.Not.Null);
        Assert.That(expiresAtValue, Is.TypeOf<long>());

        var lowerBound = nowMs + (long)TimeSpan.FromMinutes(5).TotalMilliseconds - 2000;
        var upperBound = nowMs + (long)TimeSpan.FromMinutes(5).TotalMilliseconds + 2000;
        Assert.That((long)expiresAtValue!, Is.InRange(lowerBound, upperBound));
    }

    [Test]
    [Description("Test that OnceProp without a TTL emits expiresAt as null in OnceProps metadata.")]
    public async Task TestOncePropWithoutTtlHasNullExpiresAt()
    {
        var response = _factory.Render("Test/Page", new
        {
            Test = "Test",
            TestOnce = _factory.Once(() => "OnceValue")
        });

        var context = PrepareContext();

        response.SetContext(context);
        await response.ProcessResponse();

        var page = response.GetJson().Value as Page;

        Assert.That(page?.OnceProps, Is.Not.Null);
        Assert.That(page?.OnceProps, Does.ContainKey("testOnce"));

        var metadata = page?.OnceProps!["testOnce"] as Dictionary<string, object?>;
        Assert.That(metadata, Is.Not.Null);
        Assert.That(metadata!["prop"], Is.EqualTo("testOnce"));
        Assert.That(metadata, Does.ContainKey("expiresAt"));
        Assert.That(metadata["expiresAt"], Is.Null);
    }
}
