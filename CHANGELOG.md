# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Fixed

- `apply` no longer collapses indentation in every edited project file.
- Versions are removed when other attributes come before `Version`, when `Version` is the first attribute, and from nested `<Version>` elements. Previously these were left in place while also being centralized, which broke restore with NU1008.
- An existing `Directory.Packages.props` is merged instead of overwritten; its entries were previously lost.
- `--json` output is no longer preceded by log lines on stdout. All logs go to stderr.
- A plan file can no longer write outside the solution root (sibling directories sharing the root's prefix were accepted), and its file content is no longer trusted.
- Package IDs, versions and conditions are XML-escaped in generated files.
- Duplicate `--resolve` entries no longer crash.
- `check --json` reports the build step; the 60-second timeout is now `--timeout` (default 600).
- `check` targets the solution file, avoiding MSB1011 when the root holds several project or solution files.
- Malformed projects are reported as `CPM005` instead of as legacy projects.
- UTF-8 BOMs and CRLF line endings are preserved.

### Added

- Saved plans are verified before applying: the plan is regenerated with its recorded resolutions and must match (`ERR_STALE_PLAN`). Edits carry `originalSha256`.
- Every edited file is verified before anything is written; failed writes roll back (`ERR_VERIFICATION_FAILED`, `ERR_FILE_WRITE_FAILED`).
- Structured JSON errors for every command, and JSON outputs for `apply` and `check`.
- `.fsproj`, `.vbproj`, `.slnx` and `PackageReference` items in `Directory.Build.props/.targets`.
- Semantic version comparison (NuGet.Versioning), `suggestedVersion` on conflicts, conditional `PackageVersion` generation, `$(Property)` version resolution.
- Warning severities (`info` / `warning`) and codes CPM009–CPM013; CPM005 and CPM008 are now emitted.
- `IMigrationService` orchestrator for in-process IDE hosts; `Program.RunAsync` with injectable writers.
- Exit code 6 for failed `check`.

### Changed

- **Breaking:** plan schema is now `1.1`. `replaceLine`/`insertLine`/`removeLine` and `replacementText` (never emitted) were removed; new operation types `addPackageVersion`, `updatePackageVersion`, `enableCentralPackageManagement`.
- **Breaking:** `apply --json` now writes `{ dryRun, writtenFiles, plan }` instead of the bare plan.
- **Breaking:** `analyze` exits with 2 (not 1) for unresolved conflicts, matching `plan`. Informational warnings (e.g. CPM002) no longer cause exit code 1.
- `--root` is used as the root as-is, without walking up to a parent solution or repository.
- Global options (`--root`, `--json`, `--verbose`) are accepted before or after the command.
- `resolve --dry-run` is deprecated and hidden (it never had an effect).
- Upgraded System.CommandLine to 2.0 stable. The .NET tool package ID is now explicitly `CentralPackageMigrator`.

## [0.1.0] - 2024-12-01

### Added

- Initial release of the `cpm-migrate` CLI tool.
- `analyze` command: scan a .NET solution and report projects, packages, and version conflicts.
- `plan` command: generate a deterministic migration plan as JSON or human-readable output.
- `apply` command: execute the migration plan, creating `Directory.Packages.props` and removing inline versions from `.csproj` files.
- `check` command: verify the migration by running `dotnet restore` and optionally `dotnet build`.
- `resolve` command: provide version conflict resolutions and regenerate the migration plan.
- JSON output mode (`--json`) on all commands for IDE and tooling integration.
- Dry-run mode (`--dry-run`) for previewing changes without modifying files.
- Support for loading and applying saved plan files (`--plan-file`).
- Cross-platform support (Windows, Linux, macOS including ARM64).
- Solution root auto-detection via `.sln` files, `.git` directories, or explicit `--root` path.
- Version conflict detection with interactive resolution workflow.
- Warning system for legacy projects, conditional version references, and malformed XML.
- Stable JSON schema (v1.0) for IDE integration contract.
- IFileSystem abstraction for fully testable file operations.
- Three-tier architecture: Core (domain logic), Infrastructure (I/O), CLI (presentation).
