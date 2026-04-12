namespace InertiaCore.Ssr;

/// <summary>
/// Allows an <see cref="IGateway"/> implementation to opt the current request
/// out of server-side rendering. Mirrors inertia-laravel's
/// <c>Inertia\Ssr\DisablesSsr</c> contract.
/// </summary>
public interface IDisablesSsr
{
    /// <summary>
    /// Disable SSR for the current request unconditionally (or re-enable it
    /// by passing <c>false</c>).
    /// </summary>
    void Disable(bool condition = true);

    /// <summary>
    /// Disable SSR for the current request based on a closure that is
    /// evaluated lazily at dispatch time.
    /// </summary>
    void Disable(Func<bool> condition);
}
