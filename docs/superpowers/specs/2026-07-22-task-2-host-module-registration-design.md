# Task 2: Host Module Registration Design

## Background

The active rules in `shared/ruleset/lab.yml` already rely on four host-side buffers:

- `declarative_memory`
- `intention`
- `file_explorer`
- `code_viewport`

Before this task, the C# composition root only registered the first two modules. `FileExplorerModule` and `CodeViewportModule` already existed in `Harness.Codebase`, but `Harness.Core` did not reference that project and `AddHarnessCore()` never registered either module.

At the same time, `FileExplorerModule` depends on `IEmbeddingService`, but the interface had no implementation. The host also still carried an outdated `FrostpunkWorld` gRPC client registration and config entry that no longer matched the current demo path.

## Goal

Task 2 should make the C# host resolve every module required by the currently active rules, without introducing an external embedding runtime dependency.

After this task:

- `Harness.Core` can compose the code-navigation modules from `Harness.Codebase`
- `AddHarnessCore()` registers `FileExplorerModule` and `CodeViewportModule`
- `IEmbeddingService` resolves to a minimal deterministic implementation
- the host solution builds cleanly with the current module graph

## Scope

In scope:

- add the `Harness.Codebase` project reference where module composition actually happens
- register `FileExplorerModule` and `CodeViewportModule` in `AddHarnessCore()`
- add a minimal `IEmbeddingService` implementation that is stable and local
- add focused automated tests for DI composition
- remove stale `FrostpunkWorld` host wiring that blocks the current build

Out of scope:

- a real ONNX-backed embedding pipeline
- semantic ranking quality tuning
- step-loop termination changes from Task 3
- logging/report generation from Task 4

## Design

### 1. Compose codebase modules in `Harness.Core`

The module registry is owned by `Harness.Core`, so the `Harness.Codebase` reference should be added there rather than to the host entrypoint.

This keeps the composition boundary consistent:

- `Harness.Host` configures process-level concerns such as options and gRPC clients
- `Harness.Core` owns module registration and runtime composition

### 2. Use a deterministic local embedding service

Task 2 only needs `FileExplorerModule` to resolve and run. It does not need a production embedding backend yet.

The minimal implementation should therefore:

- implement `IEmbeddingService`
- return deterministic embeddings for equal inputs
- avoid external model files, ONNX runtime packages, or network calls

A small normalized character-bucket vector is sufficient for this stage. It keeps the module usable for ranking without adding new infrastructure.

### 3. Avoid the registry construction cycle

`DeclarativeMemoryModule` already depends on `IModuleRegistry` so it can snapshot all module buffers on tick.

If `IModuleRegistry` is built by resolving `DeclarativeMemoryModule` inside the same factory, the container creates a circular construction path:

- build `IModuleRegistry`
- resolve `DeclarativeMemoryModule`
- resolve `IModuleRegistry` again

The composition should therefore register a concrete `ModuleRegistry` singleton first, then construct `DeclarativeMemoryModule` against that singleton, and finally expose the populated registry through `IModuleRegistry`.

### 4. Clean up stale host wiring

The current host build should only register the gRPC clients that still exist:

- declarative memory
- procedural memory
- neuro core

The outdated `FrostpunkWorld` registration and `FrostpunkWorldAddress` config field should be removed so the host matches the actual current architecture.

### 5. Tests

Task 2 should add one focused .NET test project to validate the composition root.

Coverage should verify:

- `AddHarnessCore()` registers all four required modules
- `FileExplorerModule` and `CodeViewportModule` are directly resolvable
- `IEmbeddingService` resolves and returns deterministic fixed-size vectors

These tests are sufficient for Task 2 because the work is primarily dependency-registration and construction behavior.

## File Changes

Expected files to modify or create:

- Modify: `Dotnet/src/Harness.Core/Harness.Core.csproj`
- Modify: `Dotnet/src/Harness.Core/Extensions/DependencyInjectionExtensions.cs`
- Create: `Dotnet/src/Harness.Core/Embeddings/DeterministicEmbeddingService.cs`
- Modify: `Dotnet/src/Harness.Host/Program.cs`
- Modify: `Dotnet/src/Harness.Host/Options/GrpcClientsOptions.cs`
- Modify: `Dotnet/src/Harness.Host/appsettings.example.json`
- Create: `Dotnet/tests/Harness.Core.Tests/Harness.Core.Tests.csproj`
- Create: `Dotnet/tests/Harness.Core.Tests/DependencyInjectionExtensionsTests.cs`
- Modify: `Dotnet/ACT-R-Harness.slnx`

## Acceptance Criteria

Task 2 is complete when all of the following are true:

- `Harness.Core` references `Harness.Codebase`
- `AddHarnessCore()` registers `declarative_memory`, `intention`, `file_explorer`, and `code_viewport`
- `IEmbeddingService` resolves without extra environment setup
- the DI graph no longer hangs during registry construction
- focused .NET tests verify module registration and embedding service behavior
- the .NET solution builds without the stale `FrostpunkWorld` dependency

## Non-Goals

This task does not attempt to solve any of the following:

- higher-quality semantic embeddings
- initial goal seeding
- multi-step stop conditions
- end-to-end host/Python runtime smoke tests
- trace/report persistence
