# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

`cpm-migrate` — a .NET 8 CLI (System.CommandLine 2.0 stable) that migrates .NET solutions to Central Package Management (`Directory.Packages.props`). It is meant to be the single migration engine behind IDE adapters (VS Code, Rider), which consume its versioned JSON plan output (`schemaVersion: "1.1"`, contract in [docs/JSON_SCHEMA.md](docs/JSON_SCHEMA.md), JSON Schemas in [docs/schemas/](docs/schemas/)). Treat that JSON shape as a public contract: only add optional fields within a major version; never remove or change the meaning of existing ones. `JsonContractTests` fail on any shape change; an intentional change must bump `MigrationPlan.CurrentSchemaVersion`, the schemas and the approved snapshots in the same PR (see the compatibility rule in JSON_SCHEMA.md).

## Commands

```bash
dotnet build
dotnet test                                                    # all tests
dotnet test tests/CentralPackageMigrator.Core.Tests            # unit
dotnet test tests/CentralPackageMigrator.Golden.Tests          # scenario/snapshot
dotnet test tests/CentralPackageMigrator.Integration.Tests     # real file system, full pipeline
dotnet test --filter "FullyQualifiedName~ConflictDetectorTests.SomeTestName"   # single test
dotnet test --filter "Category!=E2E"                           # skip the real `dotnet restore` test
dotnet format --verify-no-changes                              # CI enforces this

dotnet run --project src/CentralPackageMigrator.Cli -- analyze --root /path/to/solution
dotnet run --project src/CentralPackageMigrator.Cli -- plan --json --root /path/to/solution
dotnet run --project src/CentralPackageMigrator.Cli -- apply --dry-run --root /path/to/solution
```

Build settings in [Directory.Build.props](Directory.Build.props) apply to every project: `TreatWarningsAsErrors`, `EnforceCodeStyleInBuild`, nullable enabled, `AnalysisLevel 8` — style/analyzer violations fail the build. This repo itself uses CPM: add package versions to [Directory.Packages.props](Directory.Packages.props) and leave `Version` off `PackageReference` in `.csproj` files. CI runs build+test on Ubuntu, Windows, and macOS, so path handling must be cross-platform. Versions come only from `vX.Y.Z` git tags via MinVer (never set `Version` by hand); `--version` prints the informational version, which equals the NuGet package version. Releasing is described in [docs/RELEASING.md](docs/RELEASING.md).

## Architecture

Three layers ([docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) has the full picture):

- **Core** (`src/CentralPackageMigrator.Core`) — domain records in `Models/`, pipeline services in `Services/` (each with an `I*` interface) orchestrated by `MigrationService`, span-based XML editing in `Editing/`, `PathSafety` in `Security/`, and `IFileSystem`/`IProcessRunner` abstractions. Depends only on `Microsoft.Extensions.Logging.Abstractions` and `NuGet.Versioning`; never touch `System.IO` or processes directly here — go through `IFileSystem`. Expected failures throw `MigrationException` with an `ErrorCodes` value.
- **Infrastructure** — `PhysicalFileSystem`, `DotNetProcessRunner` (runs `dotnet restore`/`build`), `PlanSerializer` (System.Text.Json), and `ServiceCollectionExtensions.AddCentralPackageMigrator()` for DI registration.
- **Cli** — System.CommandLine 2.0. `Program.RunAsync(args, stdout, stderr, ...)` parses first, then builds the DI container (logs always to stderr). Each `Commands/*Command.cs` exposes `Create(CliContext)` and runs its body through `CommandRunner`, which turns exceptions into a `MigrationError` (JSON on stdout with `--json`) and an exit code. Exit codes are in `ExitCodes.cs` (0 success, 1 warnings of severity `warning`, 2 unresolved conflicts, 3 error, 4 invalid args, 5 file system error, 6 check failed).

Pipeline: `SolutionDetector` (explicit `--root` as-is; otherwise nearest `.sln`/`.slnx` → git root → cwd) → `ProjectScanner` (every `.csproj/.fsproj/.vbproj` and `Directory.Build.props/.targets` under the root) → `PackageCollector` (`Version` attribute or element, conditions incl. `ItemGroup`, `$(Property)` resolution, `VersionOverride`, `Update=`) → `ConflictDetector` / `PackageVersionAnalyzer` (NuGet.Versioning comparison, conditional versions) → `MigrationPlanner.CreatePlan()` → `FileEditEngine.Apply()`.

Invariants to preserve:
- `MigrationPlanner.CreatePlan()` is pure — no file writes. All writes happen only in `FileEditEngine.Apply()`, which validates paths, checks each edit's `originalSha256`, computes and verifies every file, then writes (rolling back on failure).
- An existing `Directory.Packages.props` is edited (add/update/enable ops), never recreated.
- `apply --plan-file` never trusts the plan: it regenerates the plan from disk with the saved resolutions and requires an exact match.
- Version conflicts are never auto-resolved; they surface in the plan and are resolved explicitly via `--resolve Pkg=Version`.
- Every written path is validated to stay inside the solution root (no `..` traversal). The engine only creates `Directory.Packages.props` and edits existing `.csproj` files — never deletes.
- Edits are exact text-span replacements: `XmlSource` maps XDocument line info to offsets in the original text, so everything outside the edited spans (indentation, comments, line endings) is untouched. Don't use regex over XML.
- Output is deterministic (packages sorted alphabetically in the props file) and migration is idempotent (a second run produces no changes).

## Tests

xUnit + FluentAssertions (+ NSubstitute in Core.Tests). Shared helpers (`InMemoryFileSystem`, `FixedTimeProvider`, `Pipeline`) live in `tests/CentralPackageMigrator.TestUtilities`. Assert exact file content, not substrings. Golden tests load `tests/CentralPackageMigrator.Golden.Tests/TestData/<scenario>/` via `LoadScenario`; `formatting-snapshot` compares `input/` after migration with `expected/` byte for byte. CLI tests call `Program.RunAsync` with string writers (`CliContractTests`). `RealRestoreTests` (`Category=E2E`) runs a real `dotnet restore`.

On Windows, `core.autocrlf=true` checks files out with CRLF while `.editorconfig` requires LF, so `dotnet format --verify-no-changes` reports ENDOFLINE locally for untouched files; CI (Linux) is unaffected.

## Conventions

From [CONTRIBUTING.md](CONTRIBUTING.md) / [.editorconfig](.editorconfig): file-scoped namespaces, immutable `record` types for models, argument validation on public domain-service methods, `Microsoft.Extensions.Logging` for logging.
