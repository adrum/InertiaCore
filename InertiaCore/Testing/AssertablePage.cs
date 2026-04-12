using System.Text.Json;
using InertiaCore.Extensions;
using InertiaCore.Models;

namespace InertiaCore.Testing;

/// <summary>
/// Fluent, NUnit-compatible assertion API for inspecting the Page model
/// returned by an Inertia response. Mirrors the behavior of the Laravel
/// adapter's <c>Inertia\Testing\AssertableInertia</c>.
/// </summary>
/// <remarks>
/// Internally the prop tree is normalized to <see cref="JsonElement"/> storage
/// so that both in-memory <see cref="Page"/> instances and pages deserialized
/// from a real HTTP response share a single assertion code path.
/// </remarks>
public sealed class AssertablePage
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _component;
    private readonly string _url;
    private readonly string? _version;
    private readonly bool _encryptHistory;
    private readonly bool _clearHistory;
    private readonly JsonElement _props;

    private AssertablePage(string component, string url, string? version,
        bool encryptHistory, bool clearHistory, JsonElement props)
    {
        _component = component;
        _url = url;
        _version = version;
        _encryptHistory = encryptHistory;
        _clearHistory = clearHistory;
        _props = props;
    }

    /// <summary>
    /// Construct an <see cref="AssertablePage"/> from a raw Inertia JSON payload
    /// (i.e. the body of an <c>application/json</c> Inertia response).
    /// </summary>
    public static AssertablePage FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InertiaAssertionException(
                "Cannot build AssertablePage from an empty or whitespace JSON string.");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new InertiaAssertionException(
                $"Response body is not valid JSON: {ex.Message}");
        }

        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InertiaAssertionException(
                $"Response body is not an Inertia JSON object (got {root.ValueKind}).");
        }

        var component = GetString(root, "component")
                        ?? throw new InertiaAssertionException(
                            "Response body is missing required 'component' field.");
        var url = GetString(root, "url")
                  ?? throw new InertiaAssertionException(
                      "Response body is missing required 'url' field.");
        var version = GetString(root, "version");
        var encryptHistory = GetBool(root, "encryptHistory");
        var clearHistory = GetBool(root, "clearHistory");

        JsonElement props;
        if (root.TryGetProperty("props", out var propsElement)
            && propsElement.ValueKind == JsonValueKind.Object)
        {
            props = propsElement.Clone();
        }
        else
        {
            using var empty = JsonDocument.Parse("{}");
            props = empty.RootElement.Clone();
        }

        return new AssertablePage(component, url, version, encryptHistory, clearHistory, props);
    }

    /// <summary>
    /// Construct an <see cref="AssertablePage"/> from an in-memory
    /// <see cref="Page"/>. Used internally by the
    /// <c>HttpResponseMessage.AssertInertia()</c> extension after deserialization.
    /// </summary>
    internal static AssertablePage FromPage(Page page)
    {
        var json = JsonSerializer.Serialize(page, SerializerOptions);
        return FromJson(json);
    }

    /// <summary>Assert that the page's <c>component</c> equals the expected value.</summary>
    public AssertablePage Component(string expected)
    {
        if (_component != expected)
        {
            throw new InertiaAssertionException(
                $"Expected Inertia component to be '{expected}' but got '{_component}'.");
        }

        return this;
    }

    /// <summary>Assert that the page's <c>url</c> equals the expected value.</summary>
    public AssertablePage Url(string expected)
    {
        if (_url != expected)
        {
            throw new InertiaAssertionException(
                $"Expected Inertia url to be '{expected}' but got '{_url}'.");
        }

        return this;
    }

    /// <summary>Assert that the page's <c>version</c> equals the expected value (null-tolerant).</summary>
    public AssertablePage Version(string? expected)
    {
        if (!string.Equals(_version, expected, StringComparison.Ordinal))
        {
            throw new InertiaAssertionException(
                $"Expected Inertia version to be '{expected ?? "<null>"}' but got '{_version ?? "<null>"}'.");
        }

        return this;
    }

    /// <summary>Assert that the page's <c>encryptHistory</c> flag equals the expected value.</summary>
    public AssertablePage EncryptHistory(bool expected)
    {
        if (_encryptHistory != expected)
        {
            throw new InertiaAssertionException(
                $"Expected Inertia encryptHistory to be {expected} but got {_encryptHistory}.");
        }

        return this;
    }

    /// <summary>Assert that the page's <c>clearHistory</c> flag equals the expected value.</summary>
    public AssertablePage ClearHistory(bool expected)
    {
        if (_clearHistory != expected)
        {
            throw new InertiaAssertionException(
                $"Expected Inertia clearHistory to be {expected} but got {_clearHistory}.");
        }

        return this;
    }

    /// <summary>
    /// Assert that a prop exists at the given key. Supports dot-notation paths
    /// such as <c>"users.0.name"</c>. Existence only — does not check the value.
    /// </summary>
    public AssertablePage Has(string key)
    {
        if (!TryWalk(key, out _))
        {
            throw new InertiaAssertionException(
                $"Expected Inertia props to contain key '{key}' but it was missing.");
        }

        return this;
    }

    /// <summary>
    /// Assert that no prop exists at the given key. Supports dot-notation paths.
    /// </summary>
    public AssertablePage Missing(string key)
    {
        if (TryWalk(key, out _))
        {
            throw new InertiaAssertionException(
                $"Expected Inertia props to NOT contain key '{key}' but it was present.");
        }

        return this;
    }

    /// <summary>
    /// Assert that the prop at the given key equals the expected value.
    /// Comparison is performed by round-tripping both values through JSON.
    /// Supports dot-notation paths.
    /// </summary>
    public AssertablePage Where(string key, object? expected)
    {
        if (!TryWalk(key, out var actual))
        {
            throw new InertiaAssertionException(
                $"Expected Inertia prop '{key}' to equal '{FormatExpected(expected)}' but the key was missing.");
        }

        var expectedJson = JsonSerializer.Serialize(expected, SerializerOptions);
        var actualJson = actual.GetRawText();

        if (!JsonEquals(expectedJson, actualJson))
        {
            throw new InertiaAssertionException(
                $"Expected Inertia prop '{key}' to equal '{expectedJson}' but got '{actualJson}'.");
        }

        return this;
    }

    /// <summary>
    /// Assert that the prop at the given key satisfies the supplied predicate.
    /// The predicate receives the raw <see cref="JsonElement"/> boxed as an
    /// <see cref="object"/> so callers may pattern-match against it.
    /// Supports dot-notation paths.
    /// </summary>
    public AssertablePage Where(string key, Func<object?, bool> predicate)
    {
        if (predicate == null)
        {
            throw new ArgumentNullException(nameof(predicate));
        }

        if (!TryWalk(key, out var actual))
        {
            throw new InertiaAssertionException(
                $"Expected Inertia prop '{key}' to satisfy the predicate but the key was missing.");
        }

        if (!predicate(actual))
        {
            throw new InertiaAssertionException(
                $"Expected Inertia prop '{key}' to satisfy the predicate but got '{actual.GetRawText()}'.");
        }

        return this;
    }

    /// <summary>
    /// Assert that the prop at the given key is a collection (array or object)
    /// with the given number of items.
    /// </summary>
    public AssertablePage Count(string key, int expected)
    {
        if (!TryWalk(key, out var actual))
        {
            throw new InertiaAssertionException(
                $"Expected Inertia prop '{key}' to have {expected} items but the key was missing.");
        }

        int actualCount;
        switch (actual.ValueKind)
        {
            case JsonValueKind.Array:
                actualCount = actual.GetArrayLength();
                break;
            case JsonValueKind.Object:
                actualCount = 0;
                foreach (var _ in actual.EnumerateObject()) actualCount++;
                break;
            default:
                throw new InertiaAssertionException(
                    $"Expected Inertia prop '{key}' to be a collection but got '{actual.ValueKind}'.");
        }

        if (actualCount != expected)
        {
            throw new InertiaAssertionException(
                $"Expected Inertia prop '{key}' to have {expected} items but got {actualCount}.");
        }

        return this;
    }

    // ---- internals --------------------------------------------------------

    private bool TryWalk(string key, out JsonElement value)
    {
        if (string.IsNullOrEmpty(key))
        {
            value = default;
            return false;
        }

        var segments = key.Split('.');
        var current = _props;

        foreach (var rawSegment in segments)
        {
            var segment = rawSegment;

            if (current.ValueKind == JsonValueKind.Array)
            {
                if (!int.TryParse(segment, out var index) || index < 0
                    || index >= current.GetArrayLength())
                {
                    value = default;
                    return false;
                }

                current = current[index];
                continue;
            }

            if (current.ValueKind != JsonValueKind.Object)
            {
                value = default;
                return false;
            }

            // Prop keys are normalized to camelCase during prop resolution,
            // so accept either the exact segment or its camelCase form.
            if (current.TryGetProperty(segment, out var next))
            {
                current = next;
                continue;
            }

            var camel = segment.ToCamelCase();
            if (!segment.Equals(camel, StringComparison.Ordinal)
                && current.TryGetProperty(camel, out next))
            {
                current = next;
                continue;
            }

            value = default;
            return false;
        }

        value = current;
        return true;
    }

    private static bool JsonEquals(string left, string right)
    {
        using var l = JsonDocument.Parse(left);
        using var r = JsonDocument.Parse(right);
        return ElementEquals(l.RootElement, r.RootElement);
    }

    private static bool ElementEquals(JsonElement a, JsonElement b)
    {
        if (a.ValueKind != b.ValueKind)
        {
            // Allow numeric equivalence across int/double representations.
            if ((a.ValueKind == JsonValueKind.Number || b.ValueKind == JsonValueKind.Number)
                && a.ValueKind == b.ValueKind)
            {
                return a.GetRawText() == b.GetRawText();
            }

            return false;
        }

        switch (a.ValueKind)
        {
            case JsonValueKind.Object:
                var aProps = a.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal).ToList();
                var bProps = b.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal).ToList();
                if (aProps.Count != bProps.Count) return false;
                for (var i = 0; i < aProps.Count; i++)
                {
                    if (aProps[i].Name != bProps[i].Name) return false;
                    if (!ElementEquals(aProps[i].Value, bProps[i].Value)) return false;
                }
                return true;

            case JsonValueKind.Array:
                if (a.GetArrayLength() != b.GetArrayLength()) return false;
                for (var i = 0; i < a.GetArrayLength(); i++)
                {
                    if (!ElementEquals(a[i], b[i])) return false;
                }
                return true;

            case JsonValueKind.String:
                return a.GetString() == b.GetString();

            case JsonValueKind.Number:
                if (a.TryGetDecimal(out var ad) && b.TryGetDecimal(out var bd))
                {
                    return ad == bd;
                }
                return a.GetRawText() == b.GetRawText();

            case JsonValueKind.True:
            case JsonValueKind.False:
            case JsonValueKind.Null:
                return true;

            default:
                return a.GetRawText() == b.GetRawText();
        }
    }

    private static string? GetString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var element)) return null;
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Null => null,
            _ => element.GetRawText()
        };
    }

    private static bool GetBool(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var element)) return false;
        return element.ValueKind == JsonValueKind.True;
    }

    private static string FormatExpected(object? expected)
    {
        return expected switch
        {
            null => "<null>",
            string s => s,
            _ => JsonSerializer.Serialize(expected, SerializerOptions)
        };
    }
}
