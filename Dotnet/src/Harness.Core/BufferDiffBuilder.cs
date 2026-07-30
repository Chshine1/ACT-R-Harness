using System.Collections;

namespace Harness.Core;

public static class BufferDiffBuilder
{
    public static IReadOnlyList<BufferStateChange> Build(
        IReadOnlyList<ModuleSnapshot> before,
        IReadOnlyList<ModuleSnapshot> after)
    {
        var beforeByModule = before.ToDictionary(snapshot => snapshot.ModuleId, StringComparer.OrdinalIgnoreCase);
        var afterByModule = after.ToDictionary(snapshot => snapshot.ModuleId, StringComparer.OrdinalIgnoreCase);
        var moduleIds = beforeByModule.Keys
            .Union(afterByModule.Keys, StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase);

        var changes = new List<BufferStateChange>();
        foreach (var moduleId in moduleIds)
        {
            beforeByModule.TryGetValue(moduleId, out var beforeSnapshot);
            afterByModule.TryGetValue(moduleId, out var afterSnapshot);

            var fieldChanges = new List<BufferFieldChange>();
            AppendChanges(
                path: string.Empty,
                beforeSnapshot?.Data,
                afterSnapshot?.Data,
                fieldChanges);

            if (fieldChanges.Count > 0)
            {
                changes.Add(new BufferStateChange(moduleId, fieldChanges));
            }
        }

        return changes;
    }

    public static IReadOnlyList<BufferFieldChange> Build(
        IReadOnlyDictionary<string, object?>? before,
        IReadOnlyDictionary<string, object?>? after)
    {
        var changes = new List<BufferFieldChange>();
        AppendChanges(string.Empty, before, after, changes);
        return changes;
    }

    private static void AppendChanges(
        string path,
        object? before,
        object? after,
        List<BufferFieldChange> changes)
    {
        if (AreEquivalent(before, after))
        {
            return;
        }

        if (before is IReadOnlyDictionary<string, object?> beforeDict &&
            after is IReadOnlyDictionary<string, object?> afterDict)
        {
            foreach (var key in beforeDict.Keys.Union(afterDict.Keys, StringComparer.OrdinalIgnoreCase))
            {
                beforeDict.TryGetValue(key, out var beforeValue);
                afterDict.TryGetValue(key, out var afterValue);
                AppendChanges(Join(path, key), beforeValue, afterValue, changes);
            }

            return;
        }

        if (before is IDictionary beforeDictionary &&
            after is IDictionary afterDictionary)
        {
            var keys = beforeDictionary.Keys.Cast<object?>()
                .Select(key => key?.ToString() ?? string.Empty)
                .Union(afterDictionary.Keys.Cast<object?>().Select(key => key?.ToString() ?? string.Empty), StringComparer.OrdinalIgnoreCase);

            foreach (var key in keys)
            {
                AppendChanges(
                    Join(path, key),
                    beforeDictionary[key],
                    afterDictionary[key],
                    changes);
            }

            return;
        }

        if (before is IList beforeList && after is IList afterList)
        {
            var length = Math.Max(beforeList.Count, afterList.Count);
            for (var i = 0; i < length; i++)
            {
                var beforeValue = i < beforeList.Count ? beforeList[i] : null;
                var afterValue = i < afterList.Count ? afterList[i] : null;
                AppendChanges($"{path}[{i}]", beforeValue, afterValue, changes);
            }

            return;
        }

        changes.Add(new BufferFieldChange(path, before, after));
    }

    private static bool AreEquivalent(object? before, object? after)
    {
        if (ReferenceEquals(before, after))
        {
            return true;
        }

        if (before is null || after is null)
        {
            return false;
        }

        if (before is IReadOnlyDictionary<string, object?> beforeDict &&
            after is IReadOnlyDictionary<string, object?> afterDict)
        {
            if (beforeDict.Count != afterDict.Count)
            {
                return false;
            }

            foreach (var key in beforeDict.Keys)
            {
                if (!afterDict.TryGetValue(key, out var afterValue) ||
                    !AreEquivalent(beforeDict[key], afterValue))
                {
                    return false;
                }
            }

            return true;
        }

        if (before is IList beforeList && after is IList afterList)
        {
            if (beforeList.Count != afterList.Count)
            {
                return false;
            }

            for (var i = 0; i < beforeList.Count; i++)
            {
                if (!AreEquivalent(beforeList[i], afterList[i]))
                {
                    return false;
                }
            }

            return true;
        }

        return Equals(before, after);
    }

    private static string Join(string path, string key)
    {
        return string.IsNullOrWhiteSpace(path) ? key : $"{path}.{key}";
    }
}
