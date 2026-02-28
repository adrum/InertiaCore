namespace InertiaCore.Utils;

/// <summary>
/// Interface for providing scroll/pagination metadata.
/// </summary>
public interface IScrollMetadata
{
    string GetPageName();
    object? GetPreviousPage();
    object? GetNextPage();
    object? GetCurrentPage();
}
