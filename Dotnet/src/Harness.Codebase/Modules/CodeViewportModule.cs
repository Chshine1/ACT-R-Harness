using Google.Protobuf.WellKnownTypes;
using Harness.Abstractions.Actr;
using Harness.Abstractions.Modules;
using Enum = System.Enum;

namespace Harness.Codebase.Modules;

[ModuleCommandRequest("""{ "file_path": { "type": "string" } }""")]
public record OpenFileRequest(string FilePath) : IStructRepresentable<OpenFileRequest>
{
    public Struct ToStruct() => new() { Fields = { ["file_path"] = Value.ForString(FilePath) } };
    public static OpenFileRequest FromStruct(Struct s) => new(s.Fields["file_path"].StringValue);
}

[ModuleCommandRequest("""{ "lines": { "type": "number" } }""")]
public record ScrollDownRequest(int? Lines) : IStructRepresentable<ScrollDownRequest>
{
    public Struct ToStruct() => new()
    {
        Fields = { ["lines"] = Lines.HasValue ? Value.ForNumber(Lines.Value) : Value.ForNull() }
    };

    public static ScrollDownRequest FromStruct(Struct s)
    {
        var fields = s.Fields;
        if (fields.TryGetValue("lines", out var v) && v.KindCase == Value.KindOneofCase.NumberValue)
            return new ScrollDownRequest((int)v.NumberValue);
        return new ScrollDownRequest(Lines: null);
    }
}

[ModuleCommandRequest("""{ "lines": { "type": "number" } }""")]
public record ScrollUpRequest(int? Lines) : IStructRepresentable<ScrollUpRequest>
{
    public Struct ToStruct() => new()
    {
        Fields = { ["lines"] = Lines.HasValue ? Value.ForNumber(Lines.Value) : Value.ForNull() }
    };

    public static ScrollUpRequest FromStruct(Struct s)
    {
        var fields = s.Fields;
        if (fields.TryGetValue("lines", out var v) && v.KindCase == Value.KindOneofCase.NumberValue)
            return new ScrollUpRequest((int)v.NumberValue);
        return new ScrollUpRequest(Lines: null);
    }
}

[ModuleCommandRequest("""{ "line": { "type": "number" } }""")]
public record GoToLineRequest(int Line) : IStructRepresentable<GoToLineRequest>
{
    public Struct ToStruct() => new() { Fields = { ["line"] = Value.ForNumber(Line) } };
    public static GoToLineRequest FromStruct(Struct s) => new((int)s.Fields["line"].NumberValue);
}

[ModuleCommandRequest("""{ "query": { "type": "string" } }""")]
public record FindRequest(string Query) : IStructRepresentable<FindRequest>
{
    public Struct ToStruct() => new() { Fields = { ["query"] = Value.ForString(Query) } };
    public static FindRequest FromStruct(Struct s) => new(s.Fields["query"].StringValue);
}

[ModuleCommandRequest("""{ "start_line": { "type": "number" }, "end_line": { "type": "number" } }""")]
public record SelectLinesRequest(int StartLine, int EndLine) : IStructRepresentable<SelectLinesRequest>
{
    public Struct ToStruct() => new()
    {
        Fields =
        {
            ["start_line"] = Value.ForNumber(StartLine),
            ["end_line"] = Value.ForNumber(EndLine)
        }
    };

    public static SelectLinesRequest FromStruct(Struct s) => new(
        (int)s.Fields["start_line"].NumberValue,
        (int)s.Fields["end_line"].NumberValue
    );
}

public enum ViewportStatus
{
    Ok,
    FileNotFound,
    ReadError,
    NoFileOpen
}

public enum SearchResult
{
    Found,
    NotFound,
    Inactive
}

public class CodeViewportModule : ModuleBase
{
    public override string ModuleId => "code_viewport";

    private const int DefaultViewportSize = 20;

    private string? _filePath;
    private string[] _lines = [];
    private int _totalLines;
    private int _viewportStart = 1;
    private ViewportStatus _status = ViewportStatus.NoFileOpen;

    private string? _searchQuery;
    private int _searchMatchLine = -1;
    private SearchResult _searchResult = SearchResult.Inactive;

    private int _selectionStart = -1;
    private int _selectionEnd = -1;

    private readonly SemaphoreSlim _stateLock = new(1, 1);

    [ModuleCommand("open_file")]
    protected void OpenFile(OpenFileRequest request)
    {
        _stateLock.Wait();
        try
        {
            var path = request.FilePath;
            if (!File.Exists(path))
            {
                _filePath = null;
                _lines = [];
                _totalLines = 0;
                _viewportStart = 1;
                _status = ViewportStatus.FileNotFound;
                ClearInternalState();
                return;
            }

            try
            {
                _lines = File.ReadAllLines(path);
                _filePath = path;
                _totalLines = _lines.Length;
                _viewportStart = 1;
                _status = ViewportStatus.Ok;
                ClearInternalState();
            }
            catch
            {
                _filePath = null;
                _lines = [];
                _totalLines = 0;
                _viewportStart = 1;
                _status = ViewportStatus.ReadError;
                ClearInternalState();
            }
        }
        finally
        {
            _stateLock.Release();
        }
    }

    [ModuleCommand("close_file")]
    protected void CloseFile()
    {
        _stateLock.Wait();
        try
        {
            _filePath = null;
            _lines = [];
            _totalLines = 0;
            _viewportStart = 1;
            _status = ViewportStatus.NoFileOpen;
            ClearInternalState();
        }
        finally
        {
            _stateLock.Release();
        }
    }

    [ModuleCommand("scroll_down")]
    protected void ScrollDown(ScrollDownRequest request)
    {
        _stateLock.Wait();
        try
        {
            if (_status != ViewportStatus.Ok) return;
            var delta = request.Lines ?? DefaultViewportSize;
            _viewportStart = Math.Min(_totalLines - DefaultViewportSize + 1, _viewportStart + delta);
            if (_viewportStart < 1) _viewportStart = 1;
        }
        finally
        {
            _stateLock.Release();
        }
    }

    [ModuleCommand("scroll_up")]
    protected void ScrollUp(ScrollUpRequest request)
    {
        _stateLock.Wait();
        try
        {
            if (_status != ViewportStatus.Ok) return;
            var delta = request.Lines ?? DefaultViewportSize;
            _viewportStart = Math.Max(1, _viewportStart - delta);
        }
        finally
        {
            _stateLock.Release();
        }
    }

    [ModuleCommand("go_to_line")]
    protected void GoToLine(GoToLineRequest request)
    {
        _stateLock.Wait();
        try
        {
            if (_status != ViewportStatus.Ok) return;
            var line = request.Line;
            if (line < 1) line = 1;
            if (line > _totalLines - DefaultViewportSize + 1)
                line = Math.Max(1, _totalLines - DefaultViewportSize + 1);
            _viewportStart = line;
        }
        finally
        {
            _stateLock.Release();
        }
    }

    [ModuleCommand("find")]
    protected void Find(FindRequest request)
    {
        _stateLock.Wait();
        try
        {
            if (_status != ViewportStatus.Ok) return;

            _searchQuery = request.Query;
            var startIndex = _viewportStart + DefaultViewportSize - 1;
            if (startIndex >= _totalLines) startIndex = 0;

            var foundLine = -1;
            for (var i = 0; i < _totalLines; i++)
            {
                var idx = (startIndex + i) % _totalLines;
                if (!_lines[idx].Contains(_searchQuery, StringComparison.OrdinalIgnoreCase)) continue;
                foundLine = idx + 1;
                break;
            }

            if (foundLine > 0)
            {
                _searchMatchLine = foundLine;
                _searchResult = SearchResult.Found;
                var center = foundLine - DefaultViewportSize / 2;
                if (center < 1) center = 1;
                if (center > _totalLines - DefaultViewportSize + 1)
                    center = _totalLines - DefaultViewportSize + 1;
                _viewportStart = center;
            }
            else
            {
                _searchMatchLine = -1;
                _searchResult = SearchResult.NotFound;
            }
        }
        finally
        {
            _stateLock.Release();
        }
    }

    [ModuleCommand("clear_find")]
    protected void ClearFind()
    {
        _stateLock.Wait();
        try
        {
            _searchQuery = null;
            _searchMatchLine = -1;
            _searchResult = SearchResult.Inactive;
        }
        finally
        {
            _stateLock.Release();
        }
    }

    [ModuleCommand("select_lines")]
    protected void SelectLines(SelectLinesRequest request)
    {
        _stateLock.Wait();
        try
        {
            if (_status != ViewportStatus.Ok) return;
            var start = request.StartLine;
            var end = request.EndLine;
            if (start < 1) start = 1;
            if (end > _totalLines) end = _totalLines;
            if (start > end) return;

            _selectionStart = start;
            _selectionEnd = end;
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public override BufferState GetBufferState()
    {
        _stateLock.Wait();
        try
        {
            var viewportEnd = Math.Min(_viewportStart + DefaultViewportSize - 1, _totalLines);
            var visibleLines = new List<Struct>();
            if (_status == ViewportStatus.Ok)
            {
                for (var i = _viewportStart; i <= viewportEnd; i++)
                {
                    var lineIndex = i - 1;
                    var isSelected = i >= _selectionStart && i <= _selectionEnd;
                    visibleLines.Add(new Struct
                    {
                        Fields =
                        {
                            ["line_number"] = Value.ForNumber(i),
                            ["text"] = Value.ForString(_lines[lineIndex]),
                            ["is_selected"] = Value.ForBool(isSelected)
                        }
                    });
                }
            }

            var data = new Struct
            {
                Fields =
                {
                    ["file_path"] = _filePath is not null ? Value.ForString(_filePath) : Value.ForNull(),
                    ["file_name"] = _filePath is not null ? Value.ForString(Path.GetFileName(_filePath)) : Value.ForNull(),
                    ["total_lines"] = Value.ForNumber(_totalLines),
                    ["viewport_start_line"] = _status == ViewportStatus.Ok ? Value.ForNumber(_viewportStart) : Value.ForNull(),
                    ["viewport_end_line"] = _status == ViewportStatus.Ok ? Value.ForNumber(viewportEnd) : Value.ForNull(),
                    ["viewport_size"] = Value.ForNumber(DefaultViewportSize),
                    ["visible_lines"] = visibleLines.Count > 0
                        ? Value.ForList(visibleLines.Select(Value.ForStruct).ToArray())
                        : Value.ForNull(),
                    ["search_query"] = _searchQuery is not null ? Value.ForString(_searchQuery) : Value.ForNull(),
                    ["search_match_line"] = _searchMatchLine >= 0 ? Value.ForNumber(_searchMatchLine) : Value.ForNull(),
                    ["search_result"] = Value.ForString(_searchResult.ToString().ToLowerInvariant()),
                    ["status"] = Value.ForString((Enum.GetName(_status) ?? throw new ArgumentException(nameof(_status))).ToLowerInvariant()),
                    ["selection_start"] = _selectionStart > 0 ? Value.ForNumber(_selectionStart) : Value.ForNull(),
                    ["selection_end"] = _selectionEnd > 0 ? Value.ForNumber(_selectionEnd) : Value.ForNull()
                }
            };

            return new BufferState { ModuleId = ModuleId, Data = data };
        }
        finally
        {
            _stateLock.Release();
        }
    }

    private void ClearInternalState()
    {
        _searchQuery = null;
        _searchMatchLine = -1;
        _searchResult = SearchResult.Inactive;
        _selectionStart = -1;
        _selectionEnd = -1;
    }
}