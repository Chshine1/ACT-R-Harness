using Google.Protobuf.WellKnownTypes;
using Harness.Abstractions;
using Harness.Abstractions.Actr;
using Harness.Abstractions.Modules;
using System.Collections.Concurrent;
using Harness.Shared.Utils;

namespace Harness.Codebase.Modules;

[ModuleCommandRequest("""{ "path": { "type": "string" } }""")]
public record FocusDirectoryRequest(string Path) : IStructRepresentable<FocusDirectoryRequest>
{
    public Struct ToStruct() => new() { Fields = { ["path"] = Value.ForString(Path) } };
    public static FocusDirectoryRequest FromStruct(Struct s) => new(s.Fields["path"].StringValue);
}

[ModuleCommandRequest("""{ "name": { "type": "string" } }""")]
public record ExpandEntryRequest(string Name) : IStructRepresentable<ExpandEntryRequest>
{
    public Struct ToStruct() => new() { Fields = { ["name"] = Value.ForString(Name) } };
    public static ExpandEntryRequest FromStruct(Struct s) => new(s.Fields["name"].StringValue);
}

[ModuleCommandRequest("""{ "tags": { "type": "array", "items": { "type": "string" } } }""")]
public record SetAttentionTagsRequest(string[] Tags) : IStructRepresentable<SetAttentionTagsRequest>
{
    public Struct ToStruct() => new() { Fields = { ["tags"] = Value.ForList(Tags.Select(Value.ForString).ToArray()) } };

    public static SetAttentionTagsRequest FromStruct(Struct s)
    {
        var list = s.Fields["tags"].ListValue.Values;
        return new SetAttentionTagsRequest(list.Select(v => v.StringValue).ToArray());
    }
}

public enum EntryType
{
    Directory,
    File
}

public record VisibleEntry(string Name, EntryType Type, string? Extension, long SizeBytes, float RelevanceScore)
{
    public Struct ToStruct() => new()
    {
        Fields =
        {
            ["name"] = Value.ForString(Name),
            ["type"] = Value.ForString(Type == EntryType.Directory ? "dir" : "file"),
            ["extension"] = Extension is not null ? Value.ForString(Extension) : Value.ForNull(),
            ["size_bytes"] = Value.ForNumber(SizeBytes),
            ["relevance_score"] = Value.ForNumber(RelevanceScore)
        }
    };
}

public class FileExplorerModule : ModuleBase
{
    public override string ModuleId => "file_explorer";

    private const int TopK = 10;
    private readonly IEmbeddingService _embedding;

    private string _currentDirectory = string.Empty;
    private readonly ConcurrentBag<string> _attentionTags = [];
    private readonly ConcurrentDictionary<string, List<VisibleEntry>> _directoryCache = new();
    private readonly ConcurrentDictionary<string, float[]> _embeddingCache = new();

    private IReadOnlyList<VisibleEntry> _visibleEntries = [];
    private readonly SemaphoreSlim _visibleEntriesLock = new(1, 1);

    public FileExplorerModule(IEmbeddingService embedding, IClock clock)
    {
        _embedding = embedding ?? throw new ArgumentNullException(nameof(embedding));
        clock.OnTickAsync += OnTickAsync;
    }

    [ModuleCommand("focus_directory")]
    protected void FocusDirectory(FocusDirectoryRequest request)
    {
        _currentDirectory = request.Path;
        EnsureDirectoryLoaded(_currentDirectory);
    }

    [ModuleCommand("expand_entry")]
    protected void ExpandEntry(ExpandEntryRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) return;
        _currentDirectory = Path.Combine(_currentDirectory, request.Name);
        EnsureDirectoryLoaded(_currentDirectory);
    }

    [ModuleCommand("add_attention_tag")]
    protected void AddAttentionTag(SetAttentionTagsRequest request)
    {
        _attentionTags.Clear();
        foreach (var tag in request.Tags.Where(t => !string.IsNullOrWhiteSpace(t)))
        {
            _attentionTags.Add(tag.Trim().ToLowerInvariant());
        }
    }

    [ModuleCommand("refresh")]
    protected void Refresh()
    {
        _directoryCache.TryRemove(_currentDirectory, out _);
        EnsureDirectoryLoaded(_currentDirectory);
    }

    public override BufferState GetBufferState()
    {
        IReadOnlyList<VisibleEntry> entries;
        _visibleEntriesLock.Wait();
        try
        {
            entries = _visibleEntries;
        }
        finally
        {
            _visibleEntriesLock.Release();
        }

        var data = new Struct
        {
            Fields =
            {
                ["current_directory"] = Value.ForString(_currentDirectory),
                ["visible_entries"] = entries.Count > 0
                    ? Value.ForList(entries.Select(e => Value.ForStruct(e.ToStruct())).ToArray())
                    : Value.ForNull(),
                ["is_at_root"] = Value.ForBool(IsAtProjectRoot(_currentDirectory)),
                ["parent_path"] = Directory.GetParent(_currentDirectory) is { } parent
                    ? Value.ForString(parent.FullName)
                    : Value.ForNull()
            }
        };
        return new BufferState { ModuleId = ModuleId, Data = data };
    }

    private async Task OnTickAsync(StepState state, CancellationToken cancellationToken)
    {
        if (_directoryCache.TryGetValue(_currentDirectory, out var fullList))
        {
            var missing = fullList
                .Select(e => e.Name)
                .Where(name => !_embeddingCache.ContainsKey(name))
                .Distinct()
                .ToList();

            if (missing.Count > 0)
            {
                await FetchAndCacheEmbeddingsAsync(missing, cancellationToken);
            }
        }

        float[]? contextVec = null;
        var tags = _attentionTags.ToArray();
        if (tags.Length > 0)
        {
            var missingTags = tags
                .Where(t => !_embeddingCache.ContainsKey(t))
                .Distinct()
                .ToList();

            if (missingTags.Count > 0)
            {
                await FetchAndCacheEmbeddingsAsync(missingTags, cancellationToken);
            }

            var tagVecs = new List<float[]>();
            foreach (var tag in tags)
            {
                if (_embeddingCache.TryGetValue(tag, out var vec)) tagVecs.Add(vec);
            }

            if (tagVecs.Count > 0) contextVec = NumericUtils.AverageVectors(tagVecs);
        }

        List<VisibleEntry> ranked;
        if (contextVec == null || fullList == null)
        {
            ranked = fullList?.Select(e => e with { RelevanceScore = 0f })
                .OrderBy(e => e.Name)
                .Take(TopK)
                .ToList() ?? [];
        }
        else
        {
            ranked = fullList.Select(entry =>
                {
                    var vec = _embeddingCache.GetValueOrDefault(entry.Name);
                    var score = vec != null ? NumericUtils.CosineSimilarity(vec, contextVec) : 0f;
                    return entry with { RelevanceScore = score };
                })
                .OrderByDescending(e => e.RelevanceScore)
                .Take(TopK)
                .ToList();
        }

        await _visibleEntriesLock.WaitAsync(cancellationToken);
        try
        {
            _visibleEntries = ranked;
        }
        finally
        {
            _visibleEntriesLock.Release();
        }
    }

    private async Task FetchAndCacheEmbeddingsAsync(List<string> texts, CancellationToken ct)
    {
        try
        {
            var embeddings = await _embedding.GetEmbeddingsAsync(texts, ct);

            for (var i = 0; i < texts.Count && i < embeddings.Length; i++)
            {
                _embeddingCache.TryAdd(texts[i], embeddings[i]);
            }
        }
        catch
        {
            // ignored
        }
    }

    private void EnsureDirectoryLoaded(string path)
    {
        if (_directoryCache.ContainsKey(path)) return;
        if (!Directory.Exists(path)) return;

        var entries = Directory.GetDirectories(path)
            .Select(dir => new VisibleEntry(Path.GetFileName(dir), EntryType.Directory, null, 0, 0f)).ToList();
        entries.AddRange(Directory.GetFiles(path).Select(file => new FileInfo(file)).Select(info =>
            new VisibleEntry(info.Name, EntryType.File, info.Extension, info.Length, 0f)));
        _directoryCache[path] = entries;

        _ = Task.Run(async () =>
        {
            var names = entries.Select(e => e.Name).Distinct().ToList();
            await FetchAndCacheEmbeddingsAsync(names, CancellationToken.None);
        });
    }

    private static bool IsAtProjectRoot(string path)
    {
        var parent = Directory.GetParent(path);
        if (parent == null) return true;
        string[] markers = [".git", "README.md", "*.sln", "*.csproj", "package.json"];
        return markers
            .Any(m =>
                Directory.GetFiles(parent.FullName, m).Length != 0 ||
                Directory.GetDirectories(parent.FullName, m).Length != 0
            );
    }
}