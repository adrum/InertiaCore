using InertiaCore.Extensions;

namespace InertiaCore.Utils;

internal class InertiaSharedProps
{
    private IDictionary<string, object?>? Data { get; set; }

    public Dictionary<string, object?> GetMerged(IDictionary<string, object?> with)
    {
        var result = new Dictionary<string, object?>();

        if (Data != null)
            foreach (var (key, value) in Data)
                result[key.ToCamelCase()] = value;

        foreach (var (key, value) in with) result[key] = value;

        return result;
    }

    public void Merge(IDictionary<string, object?> with) => Data = GetMerged(with);

    public void Set(string key, object? value)
    {
        Data ??= new Dictionary<string, object?>();
        Data[key.ToCamelCase()] = value;
    }

    public void Clear()
    {
        Data = null;
    }

    public IReadOnlyDictionary<string, object?> GetAll()
    {
        return Data != null
            ? new Dictionary<string, object?>(Data)
            : new Dictionary<string, object?>();
    }

    public bool TryGet(string key, out object? value)
    {
        if (Data != null && Data.TryGetValue(key.ToCamelCase(), out value))
        {
            return true;
        }

        value = null;
        return false;
    }
}
