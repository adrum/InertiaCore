using Microsoft.AspNetCore.Http;

namespace InertiaCore.Ssr;

/// <summary>
/// Per-request caching container for an SSR dispatch. Mirrors inertia-laravel's
/// <c>Inertia\Ssr\SsrState</c>, giving both the body and head renderers a single
/// place to coordinate so the SSR gateway is hit at most once per request.
///
/// Retrieved via <see cref="ForRequest"/> which stores the instance on
/// <see cref="HttpContext.Items"/> under the <see cref="ItemsKey"/> key.
///
/// Note: this class is available as infrastructure for the upcoming disableSsr
/// work (V3) but is not yet consumed by <c>ResponseFactory.Head</c> /
/// <c>ResponseFactory.Html</c>; those still use the existing
/// <c>HttpContext.Features</c>-based caching.
/// </summary>
internal sealed class SsrState
{
    public const string ItemsKey = "inertia.ssr_state";

    public Dictionary<string, object?> Page { get; set; } = new();

    public SsrResponse? Response { get; private set; }

    private bool _dispatched;

    /// <summary>
    /// Runs <paramref name="dispatcher"/> at most once and caches the result. Subsequent
    /// calls return the cached <see cref="SsrResponse"/> (including <c>null</c>).
    /// </summary>
    public async Task<SsrResponse?> DispatchOnce(Func<Task<SsrResponse?>> dispatcher)
    {
        if (_dispatched) return Response;
        _dispatched = true;
        Response = await dispatcher();
        return Response;
    }

    /// <summary>
    /// Gets or creates the <see cref="SsrState"/> for the given <see cref="HttpContext"/>.
    /// </summary>
    public static SsrState ForRequest(HttpContext context)
    {
        if (context.Items.TryGetValue(ItemsKey, out var existing) && existing is SsrState state)
            return state;

        var fresh = new SsrState();
        context.Items[ItemsKey] = fresh;
        return fresh;
    }
}
