namespace InertiaCore.Ssr;

/// <summary>
/// Allows an <see cref="IGateway"/> implementation to exclude certain request
/// paths from server-side rendering. Mirrors inertia-laravel's
/// <c>Inertia\Ssr\ExcludesSsrPaths</c> contract.
/// </summary>
public interface IExcludesSsrPaths
{
    /// <summary>
    /// Exclude one or more path patterns from SSR. Patterns support <c>*</c>
    /// wildcards matching any sequence of characters, mirroring Laravel's
    /// <c>Str::is()</c> behavior.
    /// </summary>
    void Except(params string[] paths);

    /// <summary>
    /// Exclude a collection of path patterns from SSR. See
    /// <see cref="Except(string[])"/> for matching semantics.
    /// </summary>
    void Except(IEnumerable<string> paths);
}
