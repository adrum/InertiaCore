using InertiaCore.Ssr;

namespace InertiaCoreTests;

public partial class Tests
{
    [Test]
    [Description("SsrErrorType.FromString maps all 5 known Laravel keys to their enum values")]
    public void TestSsrErrorTypeFromStringMapsKnownKeys()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SsrErrorTypeExtensions.FromString("browser-api"), Is.EqualTo(SsrErrorType.BrowserApi));
            Assert.That(SsrErrorTypeExtensions.FromString("component-resolution"),
                Is.EqualTo(SsrErrorType.ComponentResolution));
            Assert.That(SsrErrorTypeExtensions.FromString("render"), Is.EqualTo(SsrErrorType.Render));
            Assert.That(SsrErrorTypeExtensions.FromString("connection"), Is.EqualTo(SsrErrorType.Connection));
            Assert.That(SsrErrorTypeExtensions.FromString("unknown"), Is.EqualTo(SsrErrorType.Unknown));
        });
    }

    [Test]
    [Description("SsrErrorType.FromString falls back to Unknown for null and garbage input")]
    public void TestSsrErrorTypeFromStringFallsBackToUnknown()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SsrErrorTypeExtensions.FromString(null), Is.EqualTo(SsrErrorType.Unknown));
            Assert.That(SsrErrorTypeExtensions.FromString(""), Is.EqualTo(SsrErrorType.Unknown));
            Assert.That(SsrErrorTypeExtensions.FromString("not-a-real-type"), Is.EqualTo(SsrErrorType.Unknown));
            Assert.That(SsrErrorTypeExtensions.FromString("BROWSER-API"), Is.EqualTo(SsrErrorType.Unknown));
        });
    }
}
