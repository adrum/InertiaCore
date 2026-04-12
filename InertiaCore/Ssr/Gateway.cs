using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using InertiaCore.Models;
using InertiaCore.Utils;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Hosting;

namespace InertiaCore.Ssr;

internal interface IGateway : IHasHealthCheck
{
    public Task<SsrResponse?> Dispatch(object model, string url);
    public bool ShouldDispatch();

    /// <summary>
    /// Raised whenever an SSR dispatch attempt fails. Fires regardless of
    /// <c>SsrThrowOnError</c> so the silent-fallback default still gets
    /// observability. Mirrors inertia-laravel's <c>SsrRenderFailed</c> event.
    /// </summary>
    event EventHandler<SsrRenderFailed>? RenderFailed;
}

internal class Gateway : IGateway, IDisablesSsr, IExcludesSsrPaths
{
    internal const string DisabledKey = "inertia.ssr_disabled";
    internal const string ExcludedPathsKey = "inertia.ssr_excluded_paths";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IInertiaSerializer _serializer;
    private readonly IOptions<InertiaOptions> _options;
    private readonly IWebHostEnvironment _environment;
    private readonly IHttpContextAccessor _contextAccessor;

    public event EventHandler<SsrRenderFailed>? RenderFailed;

    public Gateway(IHttpClientFactory httpClientFactory, IInertiaSerializer serializer,
        IOptions<InertiaOptions> options, IWebHostEnvironment environment,
        IHttpContextAccessor contextAccessor)
        => (_httpClientFactory, _serializer, _options, _environment, _contextAccessor)
            = (httpClientFactory, serializer, options, environment, contextAccessor);

    public void Disable(bool condition = true)
    {
        var context = _contextAccessor.HttpContext;
        if (context == null) return;
        context.Items[DisabledKey] = condition;
    }

    public void Disable(Func<bool> condition)
    {
        var context = _contextAccessor.HttpContext;
        if (context == null) return;
        context.Items[DisabledKey] = condition;
    }

    public void Except(params string[] paths) => Except((IEnumerable<string>)paths);

    public void Except(IEnumerable<string> paths)
    {
        var context = _contextAccessor.HttpContext;
        if (context == null) return;

        List<string> list;
        if (context.Items.TryGetValue(ExcludedPathsKey, out var existing) && existing is List<string> existingList)
        {
            list = existingList;
        }
        else
        {
            list = new List<string>();
            context.Items[ExcludedPathsKey] = list;
        }

        foreach (var p in paths)
        {
            if (!string.IsNullOrEmpty(p)) list.Add(p);
        }
    }

    public async Task<SsrResponse?> Dispatch(dynamic model, string url)
    {
        string? responseBody = null;
        try
        {
            var json = _serializer.Serialize(model);
            var content = new StringContent(json.ToString(), Encoding.UTF8, "application/json");

            var client = _httpClientFactory.CreateClient();
            var response = await client.PostAsync(url, content);
            if (!response.IsSuccessStatusCode)
            {
                responseBody = await SafeReadBody(response);
                HandleSsrFailure(model, ParseErrorBody(responseBody),
                    $"SSR server returned {(int)response.StatusCode} {response.ReasonPhrase}".Trim());
                response.EnsureSuccessStatusCode();
            }
            return await response.Content.ReadFromJsonAsync<SsrResponse>();
        }
        catch (SsrException)
        {
            // Already raised and classified; propagate without re-raising the event.
            throw;
        }
        catch (HttpRequestException ex)
        {
            // Either a non-2xx that the inner block converted, OR a transport failure.
            // If it's a transport failure, responseBody is still null and we haven't raised yet.
            if (responseBody == null)
            {
                HandleSsrFailure(model, new SsrErrorPayload(ex.Message, "connection"), ex.Message);
            }

            if (!_options.Value.SsrThrowOnError) return null;
            throw new SsrException($"Inertia SSR dispatch to {url} failed: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            // Deserialization / other failures after a successful status.
            HandleSsrFailure(model, new SsrErrorPayload(ex.Message, null), ex.Message);

            if (!_options.Value.SsrThrowOnError) return null;
            throw new SsrException($"Inertia SSR dispatch to {url} failed: {ex.Message}", ex);
        }
    }

    private static async Task<string?> SafeReadBody(HttpResponseMessage response)
    {
        try { return await response.Content.ReadAsStringAsync(); }
        catch { return null; }
    }

    private readonly record struct SsrErrorPayload(
        string? Error,
        string? Type,
        string? Hint = null,
        string? BrowserApi = null,
        string? Stack = null,
        string? SourceLocation = null);

    private static SsrErrorPayload ParseErrorBody(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return new SsrErrorPayload(null, null);

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return new SsrErrorPayload(null, null);

            string? Get(string name) =>
                doc.RootElement.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
                    ? el.GetString()
                    : null;

            return new SsrErrorPayload(
                Error: Get("error"),
                Type: Get("type"),
                Hint: Get("hint"),
                BrowserApi: Get("browserApi"),
                Stack: Get("stack"),
                SourceLocation: Get("sourceLocation"));
        }
        catch (JsonException)
        {
            return new SsrErrorPayload(null, null);
        }
    }

    private void HandleSsrFailure(object model, SsrErrorPayload payload, string fallbackMessage)
    {
        var handler = RenderFailed;
        if (handler == null) return;

        IReadOnlyDictionary<string, object?> page;
        try
        {
            var json = _serializer.Serialize(model).ToString();
            page = JsonSerializer.Deserialize<Dictionary<string, object?>>(json!)
                   ?? new Dictionary<string, object?>();
        }
        catch
        {
            page = new Dictionary<string, object?>();
        }

        var evt = new SsrRenderFailed(
            Page: page,
            Error: payload.Error ?? fallbackMessage,
            Type: SsrErrorTypeExtensions.FromString(payload.Type),
            Hint: payload.Hint,
            BrowserApi: payload.BrowserApi,
            Stack: payload.Stack,
            SourceLocation: payload.SourceLocation);

        handler.Invoke(this, evt);
    }

    public bool ShouldDispatch()
    {
        if (IsDisabledForRequest()) return false;
        return !_options.Value.SsrEnsureBundleExists || BundleExists();
    }

    private bool IsDisabledForRequest()
    {
        var context = _contextAccessor.HttpContext;
        if (context == null) return false;

        if (context.Items.TryGetValue(DisabledKey, out var disabledValue))
        {
            switch (disabledValue)
            {
                case bool b when b:
                    return true;
                case Func<bool> closure when closure():
                    return true;
            }
        }

        if (context.Items.TryGetValue(ExcludedPathsKey, out var pathsValue)
            && pathsValue is IReadOnlyList<string> paths
            && paths.Count > 0)
        {
            var requestPath = (context.Request.Path.Value ?? string.Empty).TrimStart('/');
            foreach (var pattern in paths)
            {
                if (PathMatches(pattern, requestPath)) return true;
            }
        }

        return false;
    }

    private static bool PathMatches(string pattern, string path)
    {
        // Mirror Laravel's Str::is(): glob-style * wildcards that match any
        // sequence (including slashes). Both ends are anchored. Leading
        // slashes have already been stripped from both pattern and path.
        var normalizedPattern = pattern.TrimStart('/');
        if (normalizedPattern == path) return true;

        var regex = "^" + Regex.Escape(normalizedPattern).Replace("\\*", ".*") + "$";
        return Regex.IsMatch(path, regex);
    }

    private bool BundleExists()
    {
        var overridePath = _options.Value.SsrBundlePath;
        if (!string.IsNullOrEmpty(overridePath))
        {
            var resolvedOverride = ResolvePath(overridePath);
            if (!string.IsNullOrEmpty(resolvedOverride) && File.Exists(resolvedOverride))
            {
                return true;
            }
        }

        var commonBundlePaths = new[]
        {
            "~/public/js/ssr.js",
            "~/public/build/ssr.js",
            "~/wwwroot/js/ssr.js",
            "~/wwwroot/build/ssr.js",
            "~/dist/ssr.js",
            "~/build/ssr.js"
        };

        foreach (var path in commonBundlePaths)
        {
            var resolvedPath = ResolvePath(path);
            if (!string.IsNullOrEmpty(resolvedPath) && File.Exists(resolvedPath))
            {
                return true;
            }
        }

        return false;
    }

    private string? ResolvePath(string path)
    {
        if (path.StartsWith("~/"))
        {
            return Path.Combine(_environment.ContentRootPath, path[2..]);
        }
        return Path.IsPathRooted(path) ? path : Path.Combine(_environment.ContentRootPath, path);
    }

    public async Task<bool> IsHealthy()
    {
        try
        {
            var healthUrl = GetUrl("/health");
            var client = _httpClientFactory.CreateClient();

            using var response = await client.GetAsync(healthUrl);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private string GetUrl(string endpoint)
    {
        var baseUrl = _options.Value.SsrUrl.TrimEnd('/');
        return $"{baseUrl}{endpoint}";
    }
}
