namespace InertiaCore.Utils;

internal static class DotNotationHelper
{
    internal static object? Get(Dictionary<string, object?> dict, string key)
    {
        var segments = key.Split('.');
        object? current = dict;
        foreach (var segment in segments)
        {
            if (current is Dictionary<string, object?> currentDict)
            {
                if (!currentDict.TryGetValue(segment, out current))
                    return null;
            }
            else if (current is Dictionary<string, object> currentDictNonNull)
            {
                if (!currentDictNonNull.TryGetValue(segment, out var next))
                    return null;
                current = next;
            }
            else
                return null;
        }
        return current;
    }

    internal static void Set(Dictionary<string, object?> dict, string key, object? value)
    {
        var segments = key.Split('.');
        var current = dict;
        for (int i = 0; i < segments.Length - 1; i++)
        {
            if (!current.TryGetValue(segments[i], out var next) || next is not Dictionary<string, object?> nextDict)
            {
                nextDict = new Dictionary<string, object?>();
                current[segments[i]] = nextDict;
            }
            current = nextDict;
        }
        current[segments[^1]] = value;
    }

    internal static void Forget(Dictionary<string, object?> dict, string key)
    {
        var segments = key.Split('.');
        var current = dict;
        for (int i = 0; i < segments.Length - 1; i++)
        {
            if (!current.TryGetValue(segments[i], out var next) || next is not Dictionary<string, object?> nextDict)
                return;
            current = nextDict;
        }
        current.Remove(segments[^1]);
    }
}
