namespace InertiaCore.Ssr;

/// <summary>
/// Thrown when SSR dispatch fails and <see cref="Models.InertiaOptions.SsrThrowOnError"/>
/// is enabled. By default SSR failures are silently swallowed and the request falls back
/// to client-side rendering.
/// </summary>
public class SsrException : Exception
{
    public SsrException(string message) : base(message) { }
    public SsrException(string message, Exception innerException) : base(message, innerException) { }
}
