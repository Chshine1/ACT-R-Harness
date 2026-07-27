using Google.Protobuf.WellKnownTypes;
using Harness.Abstractions.Actr;
using Harness.Codebase.Modules;
using Harness.Core.Modules;
using Harness.Host.Options;
using Microsoft.Extensions.Options;

namespace Harness.Host;

public class DemoScenarioSeeder(
    DeclarativeMemoryModule declarativeMemoryModule,
    IntentionModule intentionModule,
    FileExplorerModule fileExplorerModule,
    IOptions<HarnessOptions> options)
{
    private readonly HarnessOptions _options = options.Value;

    public Task SeedAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var workspaceRoot = Path.GetFullPath(_options.WorkspaceRoot);
        if (!Directory.Exists(workspaceRoot))
        {
            throw new DirectoryNotFoundException($"Workspace root '{workspaceRoot}' does not exist.");
        }

        fileExplorerModule.OperateBuffer(new BufferOperation
        {
            TargetModuleId = fileExplorerModule.ModuleId,
            Command = "goto_directory",
            Params = new Struct
            {
                Fields =
                {
                    ["path"] = Value.ForString(workspaceRoot)
                }
            }
        });

        intentionModule.OperateBuffer(new BufferOperation
        {
            TargetModuleId = intentionModule.ModuleId,
            Command = "clear_goals",
            Params = new Struct()
        });

        if (_options.Scenario.SeedMemoryChunk is { } chunk && !string.IsNullOrWhiteSpace(chunk.Id))
        {
            var slotFields = new Struct();
            AddSlot(slotFields, "module", chunk.Module);
            AddSlot(slotFields, "keywords", chunk.Keywords);

            if (!string.IsNullOrWhiteSpace(chunk.RelativeFilePath))
            {
                var absolutePath = Path.GetFullPath(Path.Combine(workspaceRoot, chunk.RelativeFilePath));
                if (!File.Exists(absolutePath))
                {
                    throw new FileNotFoundException(
                        $"Seed file '{absolutePath}' does not exist.",
                        absolutePath);
                }

                AddSlot(slotFields, "file_path", absolutePath);
            }

            declarativeMemoryModule.OperateBuffer(new BufferOperation
            {
                TargetModuleId = declarativeMemoryModule.ModuleId,
                Command = "add_chunk",
                Params = new Struct
                {
                    Fields =
                    {
                        ["id"] = Value.ForString(chunk.Id),
                        ["slots"] = Value.ForStruct(slotFields)
                    }
                }
            });
        }

        var goalSlots = new Struct
        {
            Fields =
            {
                ["id"] = Value.ForString(_options.Scenario.GoalId),
                ["query"] = Value.ForString(_options.Scenario.Query),
                ["status"] = Value.ForString(_options.Scenario.GoalStatus)
            }
        };

        intentionModule.OperateBuffer(new BufferOperation
        {
            TargetModuleId = intentionModule.ModuleId,
            Command = "set_goal",
            Params = new Struct
            {
                Fields =
                {
                    ["id"] = Value.ForString(_options.Scenario.GoalId),
                    ["slots"] = Value.ForStruct(goalSlots)
                }
            }
        });

        return Task.CompletedTask;
    }

    private static void AddSlot(Struct slots, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            slots.Fields[key] = Value.ForString(value);
        }
    }
}
