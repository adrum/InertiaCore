namespace InertiaCore.Utils;

public interface IOnceable
{
    IOnceable Once(bool value = true);
    bool ShouldResolveOnce();
    bool ShouldBeRefreshed();
    string? GetOnceKey();
    IOnceable As(string key);
    IOnceable Fresh(bool value = true);
    IOnceable Until(int seconds);
    IOnceable Until(TimeSpan duration);
    IOnceable Until(DateTimeOffset expiresAt);
    long? ExpiresAt();
}
