namespace InertiaCore.Utils;

/// <summary>
/// Structured output of <see cref="PropsResolver"/> containing the page-level
/// metadata collected while walking the prop tree. Any null/empty collection
/// is dropped before being written onto the <see cref="Models.Page"/>.
/// </summary>
internal sealed class PropsMetadata
{
    public List<string>? SharedProps { get; set; }
    public List<string>? MergeProps { get; set; }
    public List<string>? PrependProps { get; set; }
    public List<string>? DeepMergeProps { get; set; }
    public List<string>? MatchPropsOn { get; set; }
    public Dictionary<string, List<string>>? DeferredProps { get; set; }
    public Dictionary<string, object>? ScrollProps { get; set; }
    public Dictionary<string, object>? OnceProps { get; set; }
}
