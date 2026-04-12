using System.Net.Http.Json;
using System.Text;
using InertiaCore.Models;
using InertiaCore.Utils;
using Microsoft.Extensions.Options;

namespace InertiaCore.Ssr;

internal interface IGateway
{
    public Task<SsrResponse?> Dispatch(object model, string url);
}

internal class Gateway : IGateway
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IInertiaSerializer _serializer;
    private readonly IOptions<InertiaOptions> _options;

    public Gateway(IHttpClientFactory httpClientFactory, IInertiaSerializer serializer,
        IOptions<InertiaOptions> options)
        => (_httpClientFactory, _serializer, _options) = (httpClientFactory, serializer, options);

    public async Task<SsrResponse?> Dispatch(dynamic model, string url)
    {
        try
        {
            var json = _serializer.Serialize(model);
            var content = new StringContent(json.ToString(), Encoding.UTF8, "application/json");

            var client = _httpClientFactory.CreateClient();
            var response = await client.PostAsync(url, content);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<SsrResponse>();
        }
        catch (Exception) when (!_options.Value.SsrThrowOnError)
        {
            return null;
        }
        catch (Exception ex)
        {
            throw new SsrException($"Inertia SSR dispatch to {url} failed: {ex.Message}", ex);
        }
    }
}
