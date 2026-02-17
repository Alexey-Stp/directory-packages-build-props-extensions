# Architecture

## System Overview

```
┌─────────────────────────────────────────────────────────┐
│                     IDE Adapters                        │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐  │
│  │  VS Code Ext │  │ Rider Plugin │  │  Other IDE   │  │
│  └──────┬───────┘  └──────┬───────┘  └──────┬───────┘  │
│         │                 │                 │           │
│         └─────────────────┼─────────────────┘           │
│                           │ JSON Contract               │
│                           ▼                             │
│  ┌─────────────────────────────────────────────────┐    │
│  │              CLI Layer (cpm-migrate)             │    │
│  │  Commands | Output Renderers | Exit Codes       │    │
│  └──────────────────────┬──────────────────────────┘    │
│                         │                               │
│  ┌──────────────────────┴──────────────────────────┐    │
│  │           Infrastructure Layer                   │    │
│  │  PhysicalFileSystem | ProcessRunner | Serializer │    │
│  └──────────────────────┬──────────────────────────┘    │
│                         │                               │
│  ┌──────────────────────┴──────────────────────────┐    │
│  │              Core Domain Layer                   │    │
│  │  Models | Services | Abstractions               │    │
│  │                                                  │    │
│  │  SolutionDetector ──► ProjectScanner             │    │
│  │         │                   │                    │    │
│  │         ▼                   ▼                    │    │
│  │  PackageCollector ──► ConflictDetector            │    │
│  │         │                   │                    │    │
│  │         ▼                   ▼                    │    │
│  │  MigrationPlanner ──► FileEditEngine             │    │
│  └──────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────┘
```

## Layers

### Core Domain (`CentralPackageMigrator.Core`)

Zero external dependencies except `Microsoft.Extensions.Logging.Abstractions`. Contains:

- **Models/** — Immutable record types: `PackageIdentity`, `PackageReference`, `ProjectInfo`, `SolutionInfo`, `ConflictGroup`, `MigrationPlan`, `FileCreate`, `FileEdit`, `EditOperation`, `MigrationWarning`, `MigrationStats`
- **Services/** — Interfaces and implementations for each step of the pipeline
- **Abstractions/** — `IFileSystem` and `IProcessRunner` for infrastructure decoupling

### Infrastructure (`CentralPackageMigrator.Infrastructure`)

Concrete implementations of abstractions:

- `PhysicalFileSystem` — delegates to `System.IO`
- `DotNetProcessRunner` — runs `dotnet restore`/`dotnet build` via `System.Diagnostics.Process`
- `PlanSerializer` — JSON serialization via `System.Text.Json`
- `ServiceCollectionExtensions` — DI registration

### CLI (`CentralPackageMigrator.Cli`)

User-facing layer:

- `Program.cs` — entry point, DI setup, command registration
- `Commands/` — `AnalyzeCommand`, `PlanCommand`, `ApplyCommand`, `CheckCommand`, `ResolveCommand`
- `Output/` — `ConsoleRenderer` (human-readable), `JsonRenderer` (machine-readable)
- `ExitCodes` — well-defined process exit codes

## Data Flow

```
analyze:  Detect → Scan → Collect → Detect Conflicts → Report
plan:     Detect → Scan → Collect → Detect Conflicts → Generate Plan → Output JSON
apply:    Plan → Validate Paths → Create Files → Edit Files → Report
check:    Run dotnet restore → [Run dotnet build] → Report
```

### Key Invariant: Plan Phase Has No Side Effects

The `MigrationPlanner.CreatePlan()` method is **pure** — it reads data and computes a plan but never writes to the file system. All writes happen exclusively in `FileEditEngine.Apply()`.

## Design Decisions

1. **Immutable domain models** — All models are C# `record` types. No mutation after construction.
2. **IFileSystem abstraction** — Enables unit testing with `InMemoryFileSystem` without touching disk.
3. **Explicit conflict resolution** — Conflicts are never auto-resolved. The CLI returns them in the plan for the user/IDE to decide.
4. **Path security** — All paths are validated against the solution root. Directory traversal (`..`) is blocked.
5. **XML editing via regex for formatting preservation** — While `XDocument` is used for parsing, version removal uses targeted regex replacement to preserve the original file formatting as closely as possible.
6. **Deterministic output** — Packages in `Directory.Packages.props` are sorted alphabetically. Operations are ordered by line number (descending for safe removal).

## Security Model

- **No arbitrary file deletion** — the engine only creates `Directory.Packages.props` and edits existing `.csproj` files
- **Path traversal protection** — all paths must resolve within the solution root
- **Explicit file write list** — the plan enumerates every file that will be created or modified
- **No network access** — the CLI never makes HTTP requests (except `dotnet restore`/`build` which the user explicitly invokes via `check`)
- **Input validation** — malformed XML is handled gracefully; invalid arguments produce clear errors

## Testing Strategy

| Layer          | Test Type      | What's Tested                                          |
|----------------|----------------|--------------------------------------------------------|
| Core           | Unit           | Package extraction, conflict detection, plan generation |
| Core           | Unit           | XML edge cases, idempotency, path security             |
| Golden         | Snapshot        | Known input solutions → expected plan output           |
| Integration    | End-to-end     | Real file system, full pipeline, idempotency           |
