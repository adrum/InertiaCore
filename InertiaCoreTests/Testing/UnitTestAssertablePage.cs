using System.Text.Json;
using InertiaCore.Testing;

namespace InertiaCoreTests.Testing;

public class UnitTestAssertablePage
{
    private const string SamplePageJson = @"{
        ""component"": ""Users/Index"",
        ""url"": ""/users"",
        ""version"": ""v1"",
        ""encryptHistory"": false,
        ""clearHistory"": false,
        ""props"": {
            ""users"": [
                { ""name"": ""Alice"", ""age"": 30 },
                { ""name"": ""Bob"", ""age"": 25 }
            ],
            ""filters"": { ""active"": true, ""tag"": ""staff"" },
            ""count"": 2,
            ""message"": ""hello""
        }
    }";

    private static AssertablePage Page() => AssertablePage.FromJson(SamplePageJson);

    // ---- Component --------------------------------------------------------

    [Test]
    public void Component_passes_on_match()
    {
        Assert.DoesNotThrow(() => Page().Component("Users/Index"));
    }

    [Test]
    public void Component_throws_on_mismatch()
    {
        var ex = Assert.Throws<InertiaAssertionException>(
            () => Page().Component("Something/Else"));
        Assert.That(ex!.Message, Does.Contain("Something/Else"));
        Assert.That(ex.Message, Does.Contain("Users/Index"));
    }

    // ---- Url --------------------------------------------------------------

    [Test]
    public void Url_passes_on_match()
    {
        Assert.DoesNotThrow(() => Page().Url("/users"));
    }

    [Test]
    public void Url_throws_on_mismatch()
    {
        var ex = Assert.Throws<InertiaAssertionException>(
            () => Page().Url("/posts"));
        Assert.That(ex!.Message, Does.Contain("/posts"));
    }

    // ---- Version ----------------------------------------------------------

    [Test]
    public void Version_passes_on_match()
    {
        Assert.DoesNotThrow(() => Page().Version("v1"));
    }

    [Test]
    public void Version_throws_on_mismatch()
    {
        var ex = Assert.Throws<InertiaAssertionException>(
            () => Page().Version("v2"));
        Assert.That(ex!.Message, Does.Contain("v2"));
    }

    [Test]
    public void Version_handles_null()
    {
        var page = AssertablePage.FromJson(@"{""component"":""X"",""url"":""/"",""version"":null,""encryptHistory"":false,""clearHistory"":false,""props"":{}}");
        Assert.DoesNotThrow(() => page.Version(null));
        Assert.Throws<InertiaAssertionException>(() => page.Version("v1"));
    }

    // ---- EncryptHistory / ClearHistory -----------------------------------

    [Test]
    public void EncryptHistory_passes_on_match()
    {
        Assert.DoesNotThrow(() => Page().EncryptHistory(false));
    }

    [Test]
    public void EncryptHistory_throws_on_mismatch()
    {
        Assert.Throws<InertiaAssertionException>(() => Page().EncryptHistory(true));
    }

    [Test]
    public void ClearHistory_passes_on_match()
    {
        Assert.DoesNotThrow(() => Page().ClearHistory(false));
    }

    [Test]
    public void ClearHistory_throws_on_mismatch()
    {
        Assert.Throws<InertiaAssertionException>(() => Page().ClearHistory(true));
    }

    // ---- Has / Missing ---------------------------------------------------

    [Test]
    public void Has_passes_on_existing_top_level_key()
    {
        Assert.DoesNotThrow(() => Page().Has("users"));
    }

    [Test]
    public void Has_supports_dot_notation()
    {
        Assert.DoesNotThrow(() => Page().Has("users.0.name"));
        Assert.DoesNotThrow(() => Page().Has("filters.active"));
    }

    [Test]
    public void Has_throws_on_missing_top_level_key()
    {
        var ex = Assert.Throws<InertiaAssertionException>(() => Page().Has("password"));
        Assert.That(ex!.Message, Does.Contain("password"));
    }

    [Test]
    public void Has_throws_on_missing_nested_key()
    {
        Assert.Throws<InertiaAssertionException>(() => Page().Has("users.5.name"));
        Assert.Throws<InertiaAssertionException>(() => Page().Has("filters.bogus"));
    }

    [Test]
    public void Missing_passes_when_key_absent()
    {
        Assert.DoesNotThrow(() => Page().Missing("password"));
        Assert.DoesNotThrow(() => Page().Missing("users.5"));
    }

    [Test]
    public void Missing_throws_when_key_present()
    {
        Assert.Throws<InertiaAssertionException>(() => Page().Missing("users"));
        Assert.Throws<InertiaAssertionException>(() => Page().Missing("users.0.name"));
    }

    // ---- Where (value) ---------------------------------------------------

    [Test]
    public void Where_matches_primitive_string()
    {
        Assert.DoesNotThrow(() => Page().Where("message", "hello"));
    }

    [Test]
    public void Where_matches_primitive_int()
    {
        Assert.DoesNotThrow(() => Page().Where("count", 2));
    }

    [Test]
    public void Where_matches_nested_via_dot_notation()
    {
        Assert.DoesNotThrow(() => Page().Where("users.0.name", "Alice"));
        Assert.DoesNotThrow(() => Page().Where("filters.active", true));
    }

    [Test]
    public void Where_throws_on_value_mismatch()
    {
        var ex = Assert.Throws<InertiaAssertionException>(
            () => Page().Where("message", "goodbye"));
        Assert.That(ex!.Message, Does.Contain("message"));
    }

    [Test]
    public void Where_throws_on_missing_key()
    {
        Assert.Throws<InertiaAssertionException>(() => Page().Where("nope", "x"));
    }

    // ---- Where (predicate) -----------------------------------------------

    [Test]
    public void Where_predicate_passes()
    {
        Assert.DoesNotThrow(() => Page().Where("count", v =>
            v is JsonElement e && e.ValueKind == JsonValueKind.Number && e.GetInt32() == 2));
    }

    [Test]
    public void Where_predicate_throws_on_failure()
    {
        Assert.Throws<InertiaAssertionException>(() => Page().Where("count", v => false));
    }

    [Test]
    public void Where_predicate_throws_on_missing_key()
    {
        Assert.Throws<InertiaAssertionException>(() => Page().Where("nope", v => true));
    }

    [Test]
    public void Where_predicate_null_throws_argument_null()
    {
        Assert.Throws<ArgumentNullException>(
            () => Page().Where("count", (Func<object?, bool>)null!));
    }

    // ---- Count ------------------------------------------------------------

    [Test]
    public void Count_passes_on_array()
    {
        Assert.DoesNotThrow(() => Page().Count("users", 2));
    }

    [Test]
    public void Count_passes_on_object()
    {
        Assert.DoesNotThrow(() => Page().Count("filters", 2));
    }

    [Test]
    public void Count_throws_on_wrong_count()
    {
        var ex = Assert.Throws<InertiaAssertionException>(
            () => Page().Count("users", 10));
        Assert.That(ex!.Message, Does.Contain("10"));
    }

    [Test]
    public void Count_throws_on_non_collection()
    {
        var ex = Assert.Throws<InertiaAssertionException>(
            () => Page().Count("message", 5));
        Assert.That(ex!.Message, Does.Contain("collection"));
    }

    [Test]
    public void Count_throws_on_missing_key()
    {
        Assert.Throws<InertiaAssertionException>(() => Page().Count("nope", 1));
    }

    // ---- Chaining --------------------------------------------------------

    [Test]
    public void Assertions_chain_fluently()
    {
        Assert.DoesNotThrow(() => Page()
            .Component("Users/Index")
            .Url("/users")
            .Version("v1")
            .EncryptHistory(false)
            .ClearHistory(false)
            .Has("users")
            .Has("users.0.name")
            .Missing("password")
            .Where("users.0.name", "Alice")
            .Where("count", 2)
            .Count("users", 2)
            .Count("filters", 2));
    }

    // ---- FromJson input handling ------------------------------------------

    [Test]
    public void FromJson_throws_on_empty()
    {
        Assert.Throws<InertiaAssertionException>(() => AssertablePage.FromJson(""));
        Assert.Throws<InertiaAssertionException>(() => AssertablePage.FromJson("   "));
    }

    [Test]
    public void FromJson_throws_on_malformed_json()
    {
        var ex = Assert.Throws<InertiaAssertionException>(
            () => AssertablePage.FromJson("{not json"));
        Assert.That(ex!.Message, Does.Contain("not valid JSON"));
    }

    [Test]
    public void FromJson_throws_on_non_object_root()
    {
        Assert.Throws<InertiaAssertionException>(() => AssertablePage.FromJson("[]"));
        Assert.Throws<InertiaAssertionException>(() => AssertablePage.FromJson("\"hi\""));
    }

    [Test]
    public void FromJson_throws_on_missing_component()
    {
        Assert.Throws<InertiaAssertionException>(
            () => AssertablePage.FromJson(@"{""url"":""/""}"));
    }

    [Test]
    public void FromJson_throws_on_missing_url()
    {
        Assert.Throws<InertiaAssertionException>(
            () => AssertablePage.FromJson(@"{""component"":""X""}"));
    }

    [Test]
    public void FromJson_tolerates_missing_optional_fields()
    {
        var page = AssertablePage.FromJson(@"{""component"":""X"",""url"":""/""}");
        Assert.DoesNotThrow(() => page.Component("X"));
        Assert.DoesNotThrow(() => page.EncryptHistory(false));
        Assert.DoesNotThrow(() => page.Missing("anything"));
    }
}
