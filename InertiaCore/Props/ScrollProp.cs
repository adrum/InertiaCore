using InertiaCore.Utils;
using Microsoft.AspNetCore.Http;

namespace InertiaCore.Props;

/// <summary>
/// Paginated/infinite scroll prop with append/prepend strategies and pagination metadata.
/// Implements Mergeable for merge strategy and IIgnoresFirstLoad to defer on initial load.
/// </summary>
public class ScrollProp : InvokableProp, IIgnoresFirstLoad, Mergeable
{
    public bool merge { get; set; } = true;
    public bool deepMerge { get; set; } = false;
    public string[]? matchOn { get; set; }
    public bool Append { get; set; } = true;
    public List<string> AppendsAtPaths { get; } = new();
    public List<string> PrependsAtPaths { get; } = new();

    private readonly string _wrapper;
    private readonly IScrollMetadata? _metadata;
    private readonly Func<object?, IScrollMetadata>? _metadataFactory;
    private object? _resolved;
    private bool _isResolved;

    // Defer support
    private bool _deferred = false;
    private string? _deferGroup;

    public ScrollProp(object? value, string wrapper = "data", IScrollMetadata? metadata = null) : base(value)
    {
        _wrapper = wrapper;
        _metadata = metadata;
    }

    public ScrollProp(Func<object?> value, string wrapper = "data", IScrollMetadata? metadata = null) : base(value)
    {
        _wrapper = wrapper;
        _metadata = metadata;
    }

    public ScrollProp(Func<Task<object?>> value, string wrapper = "data", IScrollMetadata? metadata = null) : base(value)
    {
        _wrapper = wrapper;
        _metadata = metadata;
    }

    public ScrollProp(object? value, string wrapper, Func<object?, IScrollMetadata> metadataFactory) : base(value)
    {
        _wrapper = wrapper;
        _metadataFactory = metadataFactory;
    }

    public ScrollProp(Func<object?> value, string wrapper, Func<object?, IScrollMetadata> metadataFactory) : base(value)
    {
        _wrapper = wrapper;
        _metadataFactory = metadataFactory;
    }

    /// <summary>
    /// Configure the merge strategy based on the infinite scroll merge intent header.
    /// The frontend sends its merge intent directly.
    /// </summary>
    public ScrollProp ConfigureMergeIntent(HttpRequest request)
    {
        var intent = request.Headers[InertiaHeader.InfiniteScrollMergeIntent].ToString();
        if (intent == "prepend")
        {
            ((Mergeable)this).PrependAt(_wrapper);
        }
        else
        {
            ((Mergeable)this).AppendAt(_wrapper);
        }
        return this;
    }

    /// <summary>
    /// Mark this prop as deferred.
    /// </summary>
    public ScrollProp Defer(string? group = null)
    {
        _deferred = true;
        _deferGroup = group;
        return this;
    }

    public bool ShouldDefer() => _deferred;
    public string? Group() => _deferGroup ?? "default";

    /// <summary>
    /// Get the pagination metadata.
    /// </summary>
    public Dictionary<string, object?> GetMetadata()
    {
        var provider = ResolveMetadataProvider();
        return new Dictionary<string, object?>
        {
            { "pageName", provider.GetPageName() },
            { "previousPage", provider.GetPreviousPage() },
            { "nextPage", provider.GetNextPage() },
            { "currentPage", provider.GetCurrentPage() }
        };
    }

    private IScrollMetadata ResolveMetadataProvider()
    {
        if (_metadata != null)
            return _metadata;

        if (_metadataFactory != null)
        {
            // Resolve the value to pass to the factory
            var value = ResolveValue();
            return _metadataFactory(value);
        }

        // Default: return empty metadata
        return new ScrollMetadata("page");
    }

    private object? ResolveValue()
    {
        if (_isResolved) return _resolved;
        _resolved = Invoke().GetAwaiter().GetResult();
        _isResolved = true;
        return _resolved;
    }

    // Explicit Mergeable implementations
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

    public string[]? GetMatchOn() => matchOn;
}
