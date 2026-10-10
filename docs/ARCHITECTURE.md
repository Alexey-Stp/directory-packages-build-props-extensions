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
│  │  MigrationService (orchestrator)                 │    │
│  │         │                                        │    │
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

Depends only on `Microsoft.Extensions.Logging.Abstractions` and `NuGet.Versioning`. Contains:

- **Models/** — Immutable record types (`PackageReference`, `ProjectInfo`, `SolutionInfo`, `ConflictGroup`, `MigrationPlan`, `FileCreate`, `FileEdit`, `EditOperation`, `MigrationWarning`, `MigrationStats`), `ConflictResolution` parsing/validation, and `MigrationException` carrying a stable `ErrorCodes` value
- **Services/** — One service per pipeline step, plus `MigrationService`, the orchestrator every caller goes through
- **Editing/** — `XmlSource` (maps XDocument line info to exact character offsets) and `XmlFileEditor` (applies and verifies edit operations)
- **Security/** — `PathSafety` (root containment, relative path validation)
- **Abstractions/** — `IFileSystem` and `IProcessRunner` for infrastructure decoupling

### Infrastructure (`CentralPackageMigrator.Infrastructure`)

- `PhysicalFileSystem` — delegates to `System.IO`; preserves an existing file's UTF-8 BOM on overwrite
- `DotNetProcessRunner` — runs `dotnet restore`/`build` with an argument list and a caller-supplied timeout
- `PlanSerializer` — JSON serialization via `System.Text.Json` for plans and every other JSON output
- `ServiceCollectionExtensions` — DI registration (`IFileSystem`, `IProcessRunner` and `TimeProvider` use `TryAdd`, so hosts and tests can substitute them)

### CLI (`CentralPackageMigrator.Cli`)

- `Program.RunAsync(args, stdout, stderr, ...)` — parses first, then builds the DI container (log level depends on `--verbose`; all logs go to stderr)
- `CommandRunner` — uniform error handling: every failure becomes a `MigrationError` (JSON on stdout in `--json` mode, text on stderr otherwise) and a defined exit code
- `Commands/` — thin handlers that call `IMigrationService`
- `Output/` — `ConsoleRenderer` (human-readable), `JsonRenderer` and `JsonContracts` (apply, check and error outputs)
- `ExitCodes` — process exit codes, including the shared "conflicts over warnings" rule

## Data Flow

```
analyze:  Detect → Scan → Plan (no resolutions) → Report
plan:     Detect → Scan → Plan → Output
apply:    Detect → Scan → Plan → [conflicts? stop] → Engine: validate, compute, verify all → write all
apply --plan-file:  Load → check schema + root → regenerate plan with saved resolutions → must match exactly → Engine
check:    Detect → dotnet restore <sln> → [dotnet build <sln> --no-restore]
```

Scanning covers every project and `Directory.Build.props/.targets` under the root, because `Directory.Packages.props` applies by directory, not by solution membership. The solution file is only used to report projects outside the root.

### Key Invariants

- **Planning has no side effects.** `MigrationPlanner.CreatePlan()` reads files but never writes. All writes happen in `FileEditEngine.Apply()`.
- **Nothing is written until everything is verified.** The engine validates every path, checks every `originalSha256`, computes every new file and re-parses it to verify each operation took effect. Only then does it write; a failed write restores the files already written.
- **Saved plans are never trusted.** Their content is only used for comparison with a freshly computed plan.
- **Edits touch only the bytes that change.** `XmlSource` maps XDocument line/column info to offsets in the original text, so indentation, comments, attribute order and line endings outside the edited spans are untouched.

## Design Decisions

1. **Immutable domain models** — All models are C# `record` types. No mutation after construction.
2. **IFileSystem abstraction** — Enables unit testing with `InMemoryFileSystem` without touching disk.
3. **Explicit conflict resolution** — Conflicts are never auto-resolved. The plan offers a `suggestedVersion` (the highest), but the user or IDE decides.
4. **Semantic versions** — Versions are compared with NuGet.Versioning, so `1.0` and `1.0.0` agree. Conditional references whose conditions each map to one version produce conditional `PackageVersion` items instead of conflicts.
5. **Span-based XML editing** — XDocument is used to locate elements; edits are applied as exact text replacements and verified by re-parsing. No regular expressions over XML.
6. **Merge, never overwrite** — An existing `Directory.Packages.props` is edited (add, update, enable CPM); its existing entries and formatting are kept, and mismatched versions become conflicts.
7. **Deterministic output** — Packages are sorted, operations ordered, and the timestamp comes from an injectable `TimeProvider`.

## Security Model

- **Restricted targets** — the engine creates only `Directory.Packages.props` (and only if it does not exist), and edits only project files, `Directory.Build.props/.targets` and `Directory.Packages.props`, each with the operation types allowed for that file. It never deletes, except to roll back a file it created in the same run.
- **Path containment** — plan paths must be plain relative paths (no rooted, drive-qualified or `..` paths) and resolve strictly inside the root, compared on a directory boundary with the platform's case sensitivity.
- **Untrusted plan files** — `apply --plan-file` requires the plan's root to match the current root and the plan to equal one regenerated from disk; file content in the plan is never written as-is.
- **Stale-plan detection** — every edit carries the SHA-256 of the content it was computed from.
- **Escaped output** — package IDs, versions and conditions are XML-escaped; `--resolve` values must be valid NuGet IDs and versions.
- **No network access** — except `dotnet restore`/`build`, which the user explicitly invokes via `check`.

## Testing Strategy

| Project      | Test Type   | What's Tested                                                                    |
|--------------|-------------|----------------------------------------------------------------------------------|
| Core.Tests   | Unit        | Services, exact before/after file content, engine safety (paths, hashes, rollback), parsing |
| Golden.Tests | Snapshot    | `TestData/<scenario>` inputs; `formatting-snapshot` compares every output file byte for byte with `expected/` |
| Integration  | End-to-end  | Real file system, BOM/CRLF, CLI JSON contract and exit codes via `Program.RunAsync` |
| Integration  | E2E (`Category=E2E`) | Migrates a real project and runs `dotnet restore` through `check`       |

`TestUtilities` holds the shared `InMemoryFileSystem`, `FixedTimeProvider` and `Pipeline` (all Core services wired over a file system).
