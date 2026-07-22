# Host Module Registration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Register the code-navigation modules required by the active rules, provide a minimal local `IEmbeddingService`, and make the current host solution build cleanly.

**Architecture:** Keep host composition in `Harness.Core` by adding the `Harness.Codebase` reference there, register the codebase modules in `AddHarnessCore()`, and satisfy `FileExplorerModule` with a deterministic in-process embedding service. Break the existing registry construction cycle by registering the concrete `ModuleRegistry` singleton before composing `DeclarativeMemoryModule`.

**Tech Stack:** .NET 10, Microsoft.Extensions.DependencyInjection, xUnit, gRPC client stubs

---

## Status

Implemented on `2026-07-22`.

## File Structure

- `Dotnet/src/Harness.Core/Harness.Core.csproj`: adds the `Harness.Codebase` project reference
- `Dotnet/src/Harness.Core/Extensions/DependencyInjectionExtensions.cs`: registers the embedding service, codebase modules, and fixes registry composition
- `Dotnet/src/Harness.Core/Embeddings/DeterministicEmbeddingService.cs`: minimal deterministic `IEmbeddingService`
- `Dotnet/src/Harness.Host/Program.cs`: removes stale `FrostpunkWorld` client registration
- `Dotnet/src/Harness.Host/Options/GrpcClientsOptions.cs`: removes `FrostpunkWorldAddress`
- `Dotnet/src/Harness.Host/appsettings.example.json`: removes the stale host config entry
- `Dotnet/tests/Harness.Core.Tests/Harness.Core.Tests.csproj`: focused .NET test project for composition behavior
- `Dotnet/tests/Harness.Core.Tests/DependencyInjectionExtensionsTests.cs`: DI coverage for module registration and embeddings
- `Dotnet/ACT-R-Harness.slnx`: includes the new test project

### Task 1: Register `Harness.Codebase` at the Core Composition Layer

**Files:**

- Modify: `Dotnet/src/Harness.Core/Harness.Core.csproj`
- Modify: `Dotnet/src/Harness.Core/Extensions/DependencyInjectionExtensions.cs`

- [x] Add the `Harness.Codebase` project reference to `Harness.Core`
- [x] Register `FileExplorerModule` and `CodeViewportModule` inside `AddHarnessCore()`
- [x] Register those modules into the shared module registry alongside `DeclarativeMemoryModule` and `IntentionModule`

### Task 2: Add a Minimal Local Embedding Service

**Files:**

- Create: `Dotnet/src/Harness.Core/Embeddings/DeterministicEmbeddingService.cs`
- Modify: `Dotnet/src/Harness.Core/Extensions/DependencyInjectionExtensions.cs`

- [x] Add `DeterministicEmbeddingService : IEmbeddingService`
- [x] Return deterministic 16-dimensional normalized vectors for equal text inputs
- [x] Register the implementation as the default `IEmbeddingService`

### Task 3: Fix the Existing DI Construction Cycle

**Files:**

- Modify: `Dotnet/src/Harness.Core/Extensions/DependencyInjectionExtensions.cs`

- [x] Register a concrete `ModuleRegistry` singleton first
- [x] Construct `DeclarativeMemoryModule` against that singleton instead of re-entering `IModuleRegistry`
- [x] Expose the populated registry through `IModuleRegistry`

### Task 4: Remove Stale Host Wiring

**Files:**

- Modify: `Dotnet/src/Harness.Host/Program.cs`
- Modify: `Dotnet/src/Harness.Host/Options/GrpcClientsOptions.cs`
- Modify: `Dotnet/src/Harness.Host/appsettings.example.json`

- [x] Remove the nonexistent `FrostpunkWorld` client registration
- [x] Remove `FrostpunkWorldAddress` from the options type
- [x] Remove the stale example configuration entry

### Task 5: Add Focused Composition Tests

**Files:**

- Create: `Dotnet/tests/Harness.Core.Tests/Harness.Core.Tests.csproj`
- Create: `Dotnet/tests/Harness.Core.Tests/DependencyInjectionExtensionsTests.cs`
- Modify: `Dotnet/ACT-R-Harness.slnx`

- [x] Add a dedicated xUnit project for `Harness.Core`
- [x] Verify `AddHarnessCore()` registers all four required modules
- [x] Verify `FileExplorerModule` and `CodeViewportModule` are resolvable
- [x] Verify `IEmbeddingService` resolves and returns deterministic vectors

## Verification

- [x] Run `.\.dotnet\dotnet.exe test Dotnet/tests/Harness.Core.Tests/Harness.Core.Tests.csproj`
- [x] Run `.\.dotnet\dotnet.exe build Dotnet/ACT-R-Harness.slnx`

## Notes

- Local verification used a repository-local `.NET 10.0.302` SDK in `.\.dotnet\` because the machine-wide SDK was `9.0.304` and cannot build `net10.0` targets.
- The new tests use a local placeholder `DeclarativeMemoryClient` instance only to satisfy construction of `DeclarativeMemoryModule`; they do not rely on a running gRPC server.
