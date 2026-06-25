using System.Collections.Concurrent;
using Google.Protobuf.WellKnownTypes;
using Harness.Abstractions;
using Harness.Abstractions.Actr;
using Harness.Abstractions.Modules;

namespace Harness.Codebase.Modules;

[ModuleCommandRequest(
    """
    {
        "path": { "type": "string" }
    }
    """
)]
public record FocusDirectoryRequest(string Path) : IStructRepresentable<FocusDirectoryRequest>
{
    public Struct ToStruct() => new()
    {
        Fields =
        {
            ["path"] = Value.ForString(Path)
        }
    };

    public static FocusDirectoryRequest FromStruct(Struct s)
    {
        return new FocusDirectoryRequest(s.Fields["path"].StringValue);
    }
}

[ModuleCommandRequest(
    """
    {
        "bias": {
            "type": "array",
            "items": { "type": "number" }
        }
    }
    """
)]
public record SetAttentionBiasRequest(float[] Bias) : IStructRepresentable<SetAttentionBiasRequest>
{
    public Struct ToStruct() => new()
    {
        Fields =
        {
            ["bias"] = Value.ForList(Bias.Select(f => Value.ForNumber(f)).ToArray())
        }
    };

    public static SetAttentionBiasRequest FromStruct(Struct s)
    {
        var list = s.Fields["bias"].ListValue.Values;
        return new SetAttentionBiasRequest(list.Select(v => (float)v.NumberValue).ToArray());
    }
}

[ModuleCommandRequest(
    """
    {
        "name": { "type": "string" }
    }
    """
)]
public record ExpandEntryRequest(string Name) : IStructRepresentable<ExpandEntryRequest>
{
    public Struct ToStruct() => new()
    {
        Fields =
        {
            ["name"] = Value.ForString(Name)
        }
    };

    public static ExpandEntryRequest FromStruct(Struct s)
    {
        return new ExpandEntryRequest(s.Fields["name"].StringValue);
    }
}

public record VisibleEntry(
    string Name,
    string Type,
    string? Extension,
    long SizeBytes,
    float RelevanceScore
)
{
    public Struct ToStruct() => new()
    {
        Fields =
        {
            ["name"] = Value.ForString(Name),
            ["type"] = Value.ForString(Type),
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
    private readonly IClock? _clock;
    private string _currentDirectory = string.Empty;
    private float[] _attentionBias = [];

    private readonly ConcurrentDictionary<string, List<VisibleEntry>> _directoryCache = new();
    private readonly ConcurrentDictionary<string, float[]> _embeddingCache = new();

    public FileExplorerModule(IEmbeddingService embedding, IClock? clock = null)
    {
        _embedding = embedding ?? throw new ArgumentNullException(nameof(embedding));
        _clock = clock;

        if (_clock is not null)
        {
            _clock.OnTickAsync += OnTickAsync;
        }
    }

    private async Task OnTickAsync(StepState state, CancellationToken ct)
    {
        if (!_directoryCache.TryGetValue(_currentDirectory, out var fullList))
            return;

        foreach (var entry in fullList.Where(entry => !_embeddingCache.ContainsKey(entry.Name)))
        {
            var vec = await _embedding.GetEmbeddingAsync(entry.Name, ct);
            _embeddingCache.TryAdd(entry.Name, vec);
        }
    }

    [ModuleCommand("focus_directory")]
    protected void FocusDirectory(FocusDirectoryRequest request)
    {
        _currentDirectory = request.Path;
        EnsureDirectoryInCache(_currentDirectory);
    }

    [ModuleCommand("set_attention_bias")]
    protected void SetAttentionBias(SetAttentionBiasRequest request)
    {
        _attentionBias = request.Bias;
    }

    [ModuleCommand("expand_entry")]
    protected void ExpandEntry(ExpandEntryRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return;

        _currentDirectory = Path.Combine(_currentDirectory, request.Name);
        EnsureDirectoryInCache(_currentDirectory);
    }

    [ModuleCommand("refresh")]
    protected void Refresh()
    {
        _directoryCache.TryRemove(_currentDirectory, out _);
        EnsureDirectoryInCache(_currentDirectory);
    }

    public override BufferState GetBufferState()
    {
        var state = new Struct
        {
            Fields =
            {
                ["current_directory"] = Value.ForString(_currentDirectory)
            }
        };

        if (_directoryCache.TryGetValue(_currentDirectory, out var fullList))
        {
            var scoredEntries = ComputeTopK(fullList);
            state.Fields["visible_entries"] = Value.ForList(
                scoredEntries.Select(e => Value.ForStruct(e.ToStruct())).ToArray()
            );
        }
        else
        {
            state.Fields["visible_entries"] = Value.ForNull();
        }

        state.Fields["is_at_root"] = Value.ForBool(IsAtProjectRoot(_currentDirectory));
        state.Fields["parent_path"] = Directory.GetParent(_currentDirectory) is { } parent
            ? Value.ForString(parent.FullName)
            : Value.ForNull();

        return new BufferState { ModuleId = ModuleId, Data = state };
    }

    private List<VisibleEntry> ComputeTopK(List<VisibleEntry> fullList)
    {
        if (fullList.Count <= TopK)
            return fullList.OrderByDescending(e => ComputeRelevanceScore(e.Name)).ToList();

        return fullList
            .Select(entry => entry with { RelevanceScore = ComputeRelevanceScore(entry.Name) })
            .OrderByDescending(e => e.RelevanceScore)
            .Take(TopK)
            .ToList();
    }

    private float ComputeRelevanceScore(string entryName)
    {
        if (!_embeddingCache.TryGetValue(entryName, out var entryVec))
            return 0f;

        var dotProduct = 0f;
        if (_attentionBias.Length > 0 && _attentionBias.Length == entryVec.Length)
        {
            dotProduct += entryVec.Select((t, i) => _attentionBias[i] * t).Sum();
        }

        return dotProduct;
    }

    private void EnsureDirectoryInCache(string path)
    {
        if (_directoryCache.ContainsKey(path))
            return;

        if (!Directory.Exists(path))
            return;

        var entries = Directory.GetDirectories(path).Select(dir => Path.GetFileName(dir))
            .Select(name => new VisibleEntry(name, "dir", null, 0, 0f)).ToList();
        entries.AddRange(from file in Directory.GetFiles(path)
            let name = Path.GetFileName(file)
            let ext = Path.GetExtension(file)
            let size = new FileInfo(file).Length
            select new VisibleEntry(name, "file", ext, size, 0f));

        _directoryCache[path] = entries;

        _ = Task.Run(async () =>
        {
            foreach (var entry in entries.Where(entry => !_embeddingCache.ContainsKey(entry.Name)))
            {
                var vec = await _embedding.GetEmbeddingAsync(entry.Name);
                _embeddingCache.TryAdd(entry.Name, vec);
            }
        });
    }

    private static bool IsAtProjectRoot(string path)
    {
        var parent = Directory.GetParent(path);
        if (parent == null) return true;
        var markerFiles = new[] { ".git", "README.md", "package.json", "*.sln", "*.csproj" };
        return markerFiles.Any(m => Directory.GetFiles(parent.FullName, m).Length != 0 ||
                                    Directory.GetDirectories(parent.FullName, m).Length != 0);
    }
}

public interface IEmbeddingService
{
    Task<float[]> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default);
}