using InertiaCore.Utils;

namespace InertiaCore.Testing;

/// <summary>
/// Fluent builder for <see cref="HttpRequestMessage"/> instances pre-configured
/// with Inertia headers. Useful when exercising an ASP.NET Core
/// <c>TestServer</c> or a real <see cref="HttpClient"/> against an Inertia
/// backend in integration tests.
/// </summary>
public sealed class InertiaRequest
{
    private readonly string _url;
    private readonly HttpMethod _method;
    private readonly Dictionary<string, string> _headers = new(StringComparer.OrdinalIgnoreCase);

    private InertiaRequest(HttpMethod method, string url)
    {
        _method = method;
        _url = url;

        // Base Inertia headers — the protocol requires X-Inertia: true and a
        // version header on every XHR request.
        _headers[InertiaHeader.Inertia] = "true";
        _headers[InertiaHeader.Version] = string.Empty;
    }

    /// <summary>
    /// Create a plain Inertia GET request to the given URL with the base
    /// Inertia headers preset.
    /// </summary>
    public static InertiaRequest Get(string url)
    {
        return new InertiaRequest(HttpMethod.Get, url);
    }

    /// <summary>
    /// Create a partial-reload Inertia GET request for the given URL and
    /// component. Sets <c>X-Inertia-Partial-Component</c> in addition to the
    /// base Inertia headers.
    /// </summary>
    public static InertiaRequest Partial(string url, string component)
    {
        var request = new InertiaRequest(HttpMethod.Get, url);
        request._headers[InertiaHeader.PartialComponent] = component;
        return request;
    }

    /// <summary>Set the <c>X-Inertia-Partial-Data</c> header to the given comma-separated list of keys.</summary>
    public InertiaRequest Only(params string[] keys)
    {
        _headers[InertiaHeader.PartialOnly] = string.Join(",", keys);
        return this;
    }

    /// <summary>Set the <c>X-Inertia-Partial-Except</c> header to the given comma-separated list of keys.</summary>
    public InertiaRequest Except(params string[] keys)
    {
        _headers[InertiaHeader.PartialExcept] = string.Join(",", keys);
        return this;
    }

    /// <summary>Set the <c>X-Inertia-Error-Bag</c> header to the given bag name.</summary>
    public InertiaRequest ErrorBag(string bagName)
    {
        _headers[InertiaHeader.ErrorBag] = bagName;
        return this;
    }

    /// <summary>Override the <c>X-Inertia-Version</c> header (defaults to an empty string).</summary>
    public InertiaRequest Version(string version)
    {
        _headers[InertiaHeader.Version] = version;
        return this;
    }

    /// <summary>Set the <c>X-Inertia-Reset</c> header to the given comma-separated list of keys.</summary>
    public InertiaRequest Reset(params string[] keys)
    {
        _headers[InertiaHeader.Reset] = string.Join(",", keys);
        return this;
    }

    /// <summary>
    /// Add or override an arbitrary request header. Use as an escape hatch
    /// when the Inertia-specific builder methods don't cover your use case.
    /// </summary>
    public InertiaRequest Header(string name, string value)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("Header name must not be null or empty.", nameof(name));
        }

        _headers[name] = value;
        return this;
    }

    /// <summary>
    /// Materialize the builder into a concrete <see cref="HttpRequestMessage"/>
    /// with every configured header applied.
    /// </summary>
    public HttpRequestMessage Build()
    {
        var message = new HttpRequestMessage(_method, _url);

        foreach (var (name, value) in _headers)
        {
            message.Headers.TryAddWithoutValidation(name, value);
        }

        return message;
    }
}
