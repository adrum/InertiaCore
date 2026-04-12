using System.Net;
using System.Net.Http.Headers;
using System.Text;
using InertiaCore.Testing;

namespace InertiaCoreTests.Testing;

public class UnitTestInertiaResponseAssertions
{
    private const string ValidBody = @"{
        ""component"": ""Users/Index"",
        ""url"": ""/users"",
        ""version"": ""v1"",
        ""encryptHistory"": false,
        ""clearHistory"": false,
        ""props"": {
            ""users"": [{ ""name"": ""Alice"" }],
            ""count"": 1
        }
    }";

    private static HttpResponseMessage BuildResponse(
        string body,
        string contentType = "application/json",
        HttpStatusCode status = HttpStatusCode.OK)
    {
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8)
        };
        if (response.Content.Headers.ContentType != null)
        {
            response.Content.Headers.ContentType.MediaType = contentType;
        }
        else
        {
            response.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        }
        return response;
    }

    [Test]
    public void AssertInertia_invokes_callback_on_valid_response()
    {
        var response = BuildResponse(ValidBody);
        var invoked = false;

        var result = response.AssertInertia(page =>
        {
            invoked = true;
            page.Component("Users/Index")
                .Url("/users")
                .Has("users")
                .Where("users.0.name", "Alice")
                .Count("users", 1);
        });

        Assert.That(invoked, Is.True);
        Assert.That(result, Is.SameAs(response));
    }

    [Test]
    public void AssertInertia_throws_on_non_json_content_type()
    {
        var response = BuildResponse("<html></html>", "text/html");

        var ex = Assert.Throws<InertiaAssertionException>(
            () => response.AssertInertia(_ => { }));
        Assert.That(ex!.Message, Does.Contain("not a valid Inertia JSON response"));
        Assert.That(ex.Message, Does.Contain("text/html"));
    }

    [Test]
    public void AssertInertia_throws_on_malformed_json_body()
    {
        var response = BuildResponse("{not valid");

        var ex = Assert.Throws<InertiaAssertionException>(
            () => response.AssertInertia(_ => { }));
        Assert.That(ex!.Message, Does.Contain("not a valid Inertia JSON response"));
    }

    [Test]
    public void AssertInertia_throws_on_missing_required_fields()
    {
        var response = BuildResponse(@"{""foo"": ""bar""}");

        Assert.Throws<InertiaAssertionException>(
            () => response.AssertInertia(_ => { }));
    }

    [Test]
    public void AssertInertia_propagates_inner_assertion_failures()
    {
        var response = BuildResponse(ValidBody);

        var ex = Assert.Throws<InertiaAssertionException>(() =>
            response.AssertInertia(page => page.Component("Wrong")));
        Assert.That(ex!.Message, Does.Contain("Wrong"));
    }

    [Test]
    public void AssertInertia_chains_back_the_response()
    {
        var response = BuildResponse(ValidBody);

        var result = response
            .AssertInertia(page => page.Component("Users/Index"))
            .AssertInertia(page => page.Url("/users"));

        Assert.That(result, Is.SameAs(response));
    }

    [Test]
    public void AssertInertia_throws_on_null_response()
    {
        HttpResponseMessage response = null!;
        Assert.Throws<ArgumentNullException>(
            () => InertiaResponseAssertions.AssertInertia(response, _ => { }));
    }

    [Test]
    public void AssertInertia_throws_on_null_callback()
    {
        var response = BuildResponse(ValidBody);
        Assert.Throws<ArgumentNullException>(
            () => response.AssertInertia(null!));
    }

    [Test]
    public void AssertInertia_truncates_long_body_in_error_message()
    {
        var longBody = new string('x', 2000);
        var response = BuildResponse(longBody, "text/plain");

        var ex = Assert.Throws<InertiaAssertionException>(
            () => response.AssertInertia(_ => { }));
        Assert.That(ex!.Message, Does.Contain("..."));
        Assert.That(ex.Message.Length, Is.LessThan(2000));
    }
}
