namespace InertiaCore.Ssr;

public enum SsrErrorType
{
    BrowserApi,
    ComponentResolution,
    Render,
    Connection,
    Unknown,
}

public static class SsrErrorTypeExtensions
{
    /// <summary>
    /// Maps the string keys from the SSR node process error body
    /// ("browser-api", "component-resolution", "render", "connection", "unknown")
    /// to the <see cref="SsrErrorType"/> enum, defaulting to <see cref="SsrErrorType.Unknown"/>.
    /// </summary>
    public static SsrErrorType FromString(string? value)
    {
        return value switch
        {
            "browser-api" => SsrErrorType.BrowserApi,
            "component-resolution" => SsrErrorType.ComponentResolution,
            "render" => SsrErrorType.Render,
            "connection" => SsrErrorType.Connection,
            "unknown" => SsrErrorType.Unknown,
            _ => SsrErrorType.Unknown,
        };
    }
}
