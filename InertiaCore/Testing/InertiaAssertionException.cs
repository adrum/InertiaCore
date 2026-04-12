namespace InertiaCore.Testing;

/// <summary>
/// Thrown when an <see cref="AssertablePage"/> or
/// <see cref="InertiaResponseAssertions"/> assertion fails. The exception
/// carries a human-readable message describing the failure and is caught
/// by every major .NET test runner (NUnit, xUnit, MSTest) as a test failure.
/// </summary>
public sealed class InertiaAssertionException : Exception
{
    public InertiaAssertionException(string message) : base(message)
    {
    }

    public InertiaAssertionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
