using InertiaCore.Utils;

namespace InertiaCore.Props;

public class OnceProp : InvokableProp, IOnceable
{
    private bool _once = true;
    private bool _refresh;
    private int? _ttl;
    private string? _key;

    public OnceProp(Func<object?> callback) : base(callback) { }
    public OnceProp(Func<Task<object?>> callback) : base(callback) { }

    public IOnceable Once(bool value = true) { _once = value; return this; }
    public bool ShouldResolveOnce() => _once;
    public bool ShouldBeRefreshed() => _refresh;
    public string? GetOnceKey() => _key;
    public IOnceable As(string key) { _key = key; return this; }
    public IOnceable Fresh(bool value = true) { _refresh = value; return this; }
    public IOnceable Until(int seconds) { _ttl = seconds; return this; }
    public IOnceable Until(TimeSpan duration) { _ttl = (int)duration.TotalSeconds; return this; }
    public IOnceable Until(DateTimeOffset expiresAt) { _ttl = (int)(expiresAt - DateTimeOffset.UtcNow).TotalSeconds; return this; }
    public long? ExpiresAt()
    {
        if (_ttl == null) return null;
        return DateTimeOffset.UtcNow.AddSeconds(_ttl.Value).ToUnixTimeMilliseconds();
    }
}
