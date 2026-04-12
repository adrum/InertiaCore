using System.Collections.Generic;

namespace InertiaCore.Ssr;

/// <summary>
/// Event payload raised by <see cref="IGateway.RenderFailed"/> whenever an SSR
/// dispatch attempt fails. Mirrors inertia-laravel's <c>Inertia\Ssr\SsrRenderFailed</c>.
/// </summary>
public sealed record SsrRenderFailed(
    IReadOnlyDictionary<string, object?> Page,
    string Error,
    SsrErrorType Type = SsrErrorType.Unknown,
    string? Hint = null,
    string? BrowserApi = null,
    string? Stack = null,
    string? SourceLocation = null)
{
    public string Component =>
        Page.TryGetValue("component", out var c) ? c?.ToString() ?? "Unknown" : "Unknown";

    public string Url =>
        Page.TryGetValue("url", out var u) ? u?.ToString() ?? "/" : "/";
}
