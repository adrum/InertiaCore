namespace InertiaCore.Testing;

/// <summary>
/// Extension methods that let consumers fluently assert against Inertia
/// responses returned from an <see cref="HttpClient"/> or ASP.NET Core
/// <c>TestServer</c>.
/// </summary>
public static class InertiaResponseAssertions
{
    private const int MaxBodyExcerptLength = 512;

    /// <summary>
    /// Assert that the given <see cref="HttpResponseMessage"/> is a valid
    /// Inertia JSON response and invoke the supplied callback with an
    /// <see cref="AssertablePage"/> built from its body.
    /// </summary>
    /// <param name="response">The response returned from an Inertia endpoint.</param>
    /// <param name="assertion">The callback that performs assertions against the page.</param>
    /// <returns>The original <paramref name="response"/> to support further chaining.</returns>
    public static HttpResponseMessage AssertInertia(
        this HttpResponseMessage response,
        Action<AssertablePage> assertion)
    {
        if (response == null)
        {
            throw new ArgumentNullException(nameof(response));
        }

        if (assertion == null)
        {
            throw new ArgumentNullException(nameof(assertion));
        }

        var contentType = response.Content?.Headers.ContentType?.MediaType;
        if (!string.Equals(contentType, "application/json", StringComparison.OrdinalIgnoreCase))
        {
            var body = ReadBody(response);
            throw new InertiaAssertionException(
                "Response is not a valid Inertia JSON response. " +
                $"Status: {(int)response.StatusCode}, Content-Type: {contentType ?? "<none>"}, Body: {Truncate(body)}");
        }

        var rawBody = ReadBody(response);

        AssertablePage page;
        try
        {
            page = AssertablePage.FromJson(rawBody);
        }
        catch (InertiaAssertionException ex)
        {
            throw new InertiaAssertionException(
                "Response is not a valid Inertia JSON response. " +
                $"Status: {(int)response.StatusCode}, Content-Type: {contentType}, " +
                $"Body: {Truncate(rawBody)}. Underlying error: {ex.Message}");
        }

        assertion(page);

        return response;
    }

    private static string ReadBody(HttpResponseMessage response)
    {
        if (response.Content == null) return string.Empty;

        // Synchronously block to keep the extension method signature simple.
        // In test contexts the response content is already in memory.
        return response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
    }

    private static string Truncate(string body)
    {
        if (string.IsNullOrEmpty(body)) return "<empty>";
        return body.Length <= MaxBodyExcerptLength
            ? body
            : body.Substring(0, MaxBodyExcerptLength) + "...";
    }
}
