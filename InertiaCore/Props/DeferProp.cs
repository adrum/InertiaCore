using InertiaCore.Utils;

namespace InertiaCore.Props;

public class DeferProp : InvokableProp, IIgnoresFirstLoad, Mergeable
{
    public bool merge { get; set; }
    public bool deepMerge { get; set; } = false;
    protected readonly string _group = "default";
    public string[]? matchOn { get; set; }
    public bool Append { get; set; } = true;
    public List<string> AppendsAtPaths { get; } = new();
    public List<string> PrependsAtPaths { get; } = new();

    public DeferProp(object? value, string group) : base(value)
    {
        _group = group;
    }

    internal DeferProp(Func<object?> value, string group) : base(value)
    {
        _group = group;
    }

    internal DeferProp(Func<Task<object?>> value, string group) : base(value)
    {
        _group = group;
    }

    public Mergeable Merge()
    {
        merge = true;
        return this;
    }

    public Mergeable DeepMerge()
    {
        deepMerge = true;
        merge = true;
        return this;
    }

    public string? Group()
    {
        return _group;
    }

    public string[]? GetMatchOn() => matchOn;
}
