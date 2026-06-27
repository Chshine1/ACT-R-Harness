using Google.Protobuf.WellKnownTypes;
using Harness.Abstractions;
using Harness.Abstractions.Actr;
using Harness.Abstractions.Modules;
using Harness.Shared.Utils;
using System.Collections.Concurrent;

namespace Harness.Codebase.Modules;

[ModuleCommandRequest("""{ "path": { "type": "string" } }""")]
public record GotoDirectoryRequest(string Path) : IStructRepresentable<GotoDirectoryRequest>
{
    public Struct ToStruct() => new() { Fields = { ["path"] = Value.ForString(Path) } };
    public static GotoDirectoryRequest FromStruct(Struct s) => new(s.Fields["path"].StringValue);
}

[ModuleCommandRequest("""{ "name": { "type": "string" } }""")]
public record EnterSubdirectoryRequest(string Name) : IStructRepresentable<EnterSubdirectoryRequest>
{
    public Struct ToStruct() => new() { Fields = { ["name"] = Value.ForString(Name) } };
    public static EnterSubdirectoryRequest FromStruct(Struct s) => new(s.Fields["name"].StringValue);
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
    private string[] _attentionTags = [];
    private readonly Stack<string> _backStack = new();
    private readonly Stack<string> _forwardStack = new();

    private readonly ConcurrentDictionary<string, List<VisibleEntry>> _directoryCache = new();
    private readonly ConcurrentDictionary<string, float[]> _embeddingCache = new();

    private IReadOnlyList<VisibleEntry> _visibleEntries = [];
    private readonly SemaphoreSlim _visibleEntriesLock = new(1, 1);

    public FileExplorerModule(IEmbeddingService embedding, IClock clock)
    {
        _embedding = embedding ?? throw new ArgumentNullException(nameof(embedding));
        clock.OnTickAsync += OnTickAsync;
    }

    [ModuleCommand("goto_directory")]
    protected void GotoDirectory(GotoDirectoryRequest request)
    {
        var path = request.Path;
        if (string.IsNullOrWhiteSpace(path) || path == _currentDirectory) return;
        if (!Directory.Exists(path)) return;

        PushToHistory(_currentDirectory);
        _currentDirectory = path;
        _forwardStack.Clear();
        EnsureDirectoryLoaded(_currentDirectory);
    }

    [ModuleCommand("enter_subdirectory")]
    protected void EnterSubdirectory(EnterSubdirectoryRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) return;
        var target = Path.Combine(_currentDirectory, request.Name);
        if (!Directory.Exists(target)) return;

        PushToHistory(_currentDirectory);
        _currentDirectory = target;
        _forwardStack.Clear();
        EnsureDirectoryLoaded(_currentDirectory);
    }

    [ModuleCommand("go_to_parent")]
    protected void GoToParent()
    {
        var parent = Directory.GetParent(_currentDirectory);
        if (parent == null) return;

        PushToHistory(_currentDirectory);
        _currentDirectory = parent.FullName;
        _forwardStack.Clear();
        EnsureDirectoryLoaded(_currentDirectory);
    }

    [ModuleCommand("navigate_back")]
    protected void NavigateBack()
    {
        if (_backStack.Count == 0) return;
        _forwardStack.Push(_currentDirectory);
        _currentDirectory = _backStack.Pop();
        EnsureDirectoryLoaded(_currentDirectory);
    }

    [ModuleCommand("navigate_forward")]
    protected void NavigateForward()
    {
        if (_forwardStack.Count == 0) return;
        _backStack.Push(_currentDirectory);
        _currentDirectory = _forwardStack.Pop();
        EnsureDirectoryLoaded(_currentDirectory);
    }

    [ModuleCommand("set_attention_tags")]
    protected void SetAttentionTags(SetAttentionTagsRequest request)
    {
        _attentionTags = request.Tags
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim().ToLowerInvariant())
            .ToArray();
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

        var parent = Directory.GetParent(_currentDirectory);
        var data = new Struct
        {
            Fields =
            {
                ["current_path"] = Value.ForString(_currentDirectory),
                ["entries"] = entries.Count > 0
                    ? Value.ForList(entries.Select(e => Value.ForStruct(e.ToStruct())).ToArray())
                    : Value.ForNull(),
                ["attention_tags"] = _attentionTags.Length > 0
                    ? Value.ForList(_attentionTags.Select(Value.ForString).ToArray())
                    : Value.ForNull(),
                ["can_go_back"] = Value.ForBool(_backStack.Count > 0),
                ["can_go_forward"] = Value.ForBool(_forwardStack.Count > 0),
                ["parent_path"] = parent is not null ? Value.ForString(parent.FullName) : Value.ForNull()
            }
        };
        return new BufferState { ModuleId = ModuleId, Data = data };
    }

    private void PushToHistory(string path)
    {
        if (!string.IsNullOrEmpty(path))
        {
            _backStack.Push(path);
        }
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
        if (_attentionTags.Length > 0)
        {
            var missingTags = _attentionTags
                .Where(t => !_embeddingCache.ContainsKey(t))
                .Distinct()
                .ToList();

            if (missingTags.Count > 0)
            {
                await FetchAndCacheEmbeddingsAsync(missingTags, cancellationToken);
            }

            var tagVecs = new List<float[]>();
            foreach (var tag in _attentionTags)
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
            .Select(dir => new VisibleEntry(Path.GetFileName(dir), EntryType.Directory, null, 0, 0f))
            .ToList();

        entries.AddRange(Directory.GetFiles(path).Select(file => new FileInfo(file)).Select(info =>
            new VisibleEntry(info.Name, EntryType.File, info.Extension, info.Length, 0f)));

        _directoryCache[path] = entries;

        _ = Task.Run(async () =>
        {
            var names = entries.Select(e => e.Name).Distinct().ToList();
            await FetchAndCacheEmbeddingsAsync(names, CancellationToken.None);
        });
    }
}