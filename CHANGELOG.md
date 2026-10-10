# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.1.0] - TBD

First published release: the `CentralPackageMigrator` dotnet tool on nuget.org, plus signed binaries on GitHub Releases. An untagged 0.1.0 draft dated 2024-12-01 was never published. This entry includes it and the fixes that followed.

### Release and distribution

- Version is taken from the `vX.Y.Z` git tag (MinVer). The assembly version, NuGet package version and `cpm-migrate --version` output are identical. `--version` reports the tool's own version even when the CLI is hosted in-process.
- Release workflow:
  - runs the full CI suite on the tagged commit;
  - publishes the dotnet tool to nuget.org;
  - attaches self-contained binaries with `SHA256SUMS.txt` and build-provenance attestations to the GitHub Release.
  - Any mismatch between the tag and an artifact's version fails the release before anything is published.
- Windows binaries are Authenticode-signed. macOS binaries are signed with a Developer ID and notarized. A release tag never publishes an unsigned Windows or macOS binary.
- JSON Schemas for every `--json` output are in `docs/schemas/`. Contract tests fail on any change to the JSON shape, and CI requires a `schemaVersion` bump for intentional changes.
- CI enforces a coverage threshold and `dotnet format`, publishes test results, and rejects placeholder repository URLs.
- Package metadata added: MIT license, repository URL, README. Security reports go through GitHub private vulnerability reporting.

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
- Upgraded System.CommandLine from the 2022 pre-release `2.0.0-beta4.22272.1` to the stable `2.0.12`, pinned exactly in `Directory.Packages.props`. The .NET tool package ID is now explicitly `CentralPackageMigrator`.

### Initial feature set

- The `cpm-migrate` CLI tool.
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
- Versioned JSON schema for the IDE integration contract. It was drafted as 1.0 and is released as 1.1; see Changed.
- IFileSystem abstraction for fully testable file operations.
- Three-tier architecture: Core (domain logic), Infrastructure (I/O), CLI (presentation).

[Unreleased]: https://github.com/Alexey-Stp/directory-packages-build-props-extensions/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/Alexey-Stp/directory-packages-build-props-extensions/releases/tag/v0.1.0
