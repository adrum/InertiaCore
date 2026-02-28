namespace InertiaCore.Utils;

/// <summary>
/// Default implementation of scroll metadata with manual constructor.
/// </summary>
public class ScrollMetadata : IScrollMetadata
{
    private readonly string _pageName;
    private readonly object? _previousPage;
    private readonly object? _nextPage;
    private readonly object? _currentPage;

    public ScrollMetadata(string pageName, object? previousPage = null, object? nextPage = null, object? currentPage = null)
    {
        _pageName = pageName;
        _previousPage = previousPage;
        _nextPage = nextPage;
        _currentPage = currentPage;
    }

    public string GetPageName() => _pageName;
    public object? GetPreviousPage() => _previousPage;
    public object? GetNextPage() => _nextPage;
    public object? GetCurrentPage() => _currentPage;
}
