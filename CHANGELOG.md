# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
