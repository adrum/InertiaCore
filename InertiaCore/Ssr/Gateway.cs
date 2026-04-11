using System.Net.Http.Json;
using System.Text;
using InertiaCore.Models;
using InertiaCore.Utils;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Hosting;

namespace InertiaCore.Ssr;

internal interface IGateway : IHasHealthCheck
{
    public Task<SsrResponse?> Dispatch(object model, string url);
    public bool ShouldDispatch();
}

internal class Gateway : IGateway
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IInertiaSerializer _serializer;
    private readonly IOptions<InertiaOptions> _options;
    private readonly IWebHostEnvironment _environment;

    public Gateway(IHttpClientFactory httpClientFactory, IInertiaSerializer serializer,
        IOptions<InertiaOptions> options, IWebHostEnvironment environment)
        => (_httpClientFactory, _serializer, _options, _environment) = (httpClientFactory, serializer, options, environment);

    public async Task<SsrResponse?> Dispatch(dynamic model, string url)
    {
        var json = _serializer.Serialize(model);
        var content = new StringContent(json.ToString(), Encoding.UTF8, "application/json");

        var client = _httpClientFactory.CreateClient();
        var response = await client.PostAsync(url, content);
        return await response.Content.ReadFromJsonAsync<SsrResponse>();
    }

    public bool ShouldDispatch()
    {
        return !_options.Value.SsrEnsureBundleExists || BundleExists();
    }

    private bool BundleExists()
    {
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
