# cpm-migrate — Central Package Management Migration Tool

[![CI](https://github.com/Alexey-Stp/directory-packages-build-props-extensions/actions/workflows/ci.yml/badge.svg)](https://github.com/Alexey-Stp/directory-packages-build-props-extensions/actions/workflows/ci.yml)
[![Release](https://github.com/Alexey-Stp/directory-packages-build-props-extensions/actions/workflows/release.yml/badge.svg)](https://github.com/Alexey-Stp/directory-packages-build-props-extensions/releases)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

A production-grade CLI tool that migrates .NET solutions to [Central Package Management](https://learn.microsoft.com/nuget/consume-packages/central-package-management) (`Directory.Packages.props`).

Designed as the **single source of truth** for migration logic, reusable by VS Code extensions, JetBrains Rider plugins, and any other IDE adapter via a stable JSON contract.

---

## Features

- **Automatic solution root detection** — finds the nearest `.sln`/`.slnx`, falls back to the git root, then the current directory. An explicit `--root` is used as-is.
- **Every project under the root** — `.csproj`, `.fsproj` and `.vbproj`, plus `PackageReference` items in `Directory.Build.props`/`.targets` (`bin`, `obj`, `node_modules`, `packages`, `TestResults` and hidden directories are skipped)
- **Full PackageReference extraction** — `Version` attributes in any position, nested `<Version>` elements, `Condition` (including on the enclosing `ItemGroup`), `PrivateAssets`, `IncludeAssets`, `VersionOverride`, `Update=`
- **Formatting-preserving edits** — only the version text is removed; indentation, comments, attribute order, line endings and the UTF-8 BOM are kept
- **Merges into an existing `Directory.Packages.props`** — existing entries, comments and properties are kept; mismatches become conflicts
- **Semantic version comparison** — `1.0` and `1.0.0` are the same version; conflicts suggest the highest version
- **Conditional versions** — per-condition versions that agree with each other become conditional `PackageVersion` items instead of conflicts
- **Property-based versions** — `$(SerilogVersion)` is resolved from the project or the root `Directory.Build.props`
- **Explicit conflict resolution** — conflicts are never auto-resolved
- **Verified writes** — every edited file is re-parsed and checked before anything is written; a failed write rolls back the files already written
- **Safe plan files** — a saved plan is only applied if a plan regenerated from the current files matches it exactly
- **Dry-run mode** — runs every check without writing files
- **Idempotent** — running twice produces no additional changes
- **Cross-platform** — Windows x64, Linux x64, macOS x64, macOS ARM64

---

## Quick Start

```bash
# 1. Analyze your solution
cpm-migrate analyze

# 2. Generate a migration plan
cpm-migrate plan --json > plan.json

# 3. Apply the migration (or dry-run first)
cpm-migrate apply --dry-run
cpm-migrate apply

# 4. Verify the solution restores and builds
cpm-migrate check --build
```

---

## Installation

### Download Binary (Recommended)

Download the self-contained binary for your platform from [GitHub Releases](https://github.com/Alexey-Stp/directory-packages-build-props-extensions/releases):

| Platform     | Binary                      |
|--------------|-----------------------------|
| Windows x64  | `cpm-migrate-win-x64.exe`   |
| Linux x64    | `cpm-migrate-linux-x64`     |
| macOS x64    | `cpm-migrate-osx-x64`       |
| macOS ARM64  | `cpm-migrate-osx-arm64`     |

Verify the checksum:
```bash
sha256sum -c SHA256SUMS.txt
```

### .NET Tool

```bash
dotnet tool install -g CentralPackageMigrator
```

---

## Commands

### `analyze`

Scans the solution and reports projects, packages, conflicts and warnings without making changes.

```bash
cpm-migrate analyze [--root <path>] [--json] [--verbose]
```

### `plan`

Generates a complete migration plan. The plan is a JSON document describing all creates, edits, conflicts, and warnings.

```bash
cpm-migrate plan [--root <path>] [--json] [--output plan.json] [--resolve Pkg=Version]...
```

### `apply`

Applies the migration: creates or updates `Directory.Packages.props` and removes versions from project and import files.

```bash
cpm-migrate apply [--root <path>] [--dry-run] [--json] [--resolve Pkg=Version]...
cpm-migrate apply [--root <path>] [--dry-run] [--json] --plan-file plan.json
```

With `--plan-file`, the plan is regenerated from the current files using the resolutions recorded in the file, and applied only if it matches the saved plan exactly (`ERR_STALE_PLAN` otherwise). `--plan-file` cannot be combined with `--resolve`.

### `check`

Verifies the migrated solution by running `dotnet restore` (and optionally `dotnet build`) against the solution file, or the root directory when there is none.

```bash
cpm-migrate check [--root <path>] [--build] [--timeout <seconds>] [--json]
```

`--timeout` applies to each `dotnet` command (default: 600).

### `resolve`

Provides conflict resolutions and outputs the updated plan. Equivalent to `plan` with at least one required `--resolve`.

```bash
cpm-migrate resolve --resolve Newtonsoft.Json=13.0.3 [--resolve Serilog=3.1.1] [--output plan.json]
```

Resolutions must be valid NuGet package IDs and versions (or ranges). Repeating a package with a different version is an error; a resolution for a package without a conflict produces warning `CPM009`.

---

## Global Options

These are accepted before or after the command name.

| Option      | Description                                                        |
|-------------|--------------------------------------------------------------------|
| `--root`    | Solution root path, used as-is (auto-detected when omitted)        |
| `--json`    | Machine-readable JSON on stdout; logs go to stderr                 |
| `--verbose` | Verbose output and debug logs                                      |

---

## Exit Codes

`analyze`, `plan`, `resolve` and `apply` use the same rule: unresolved conflicts (2) take precedence over warnings (1). Warnings with severity `info` never change the exit code.

| Code | Meaning                                               |
|------|-------------------------------------------------------|
| 0    | Success                                               |
| 1    | Completed with warnings (severity `warning`)          |
| 2    | Unresolved conflicts                                  |
| 3    | Error (stale or invalid plan, failed verification)    |
| 4    | Invalid arguments                                     |
| 5    | File system error                                     |
| 6    | `check`: `dotnet restore` or `dotnet build` failed    |

In `--json` mode, failures write `{ "schemaVersion", "success": false, "error": { "code", "message", ... } }` to stdout. See [docs/JSON_SCHEMA.md](docs/JSON_SCHEMA.md) for error codes.

---

## JSON Plan Output (Example)

```json
{
  "schemaVersion": "1.1",
  "timestamp": "2024-01-15T10:30:00+00:00",
  "solutionRoot": "/path/to/solution",
  "creates": [
    {
      "relativePath": "Directory.Packages.props",
      "content": "<Project>\n  <PropertyGroup>...",
      "description": "Central Package Management file with all package versions"
    }
  ],
  "edits": [
    {
      "relativePath": "src/MyProject/MyProject.csproj",
      "originalSha256": "9f2c...e1",
      "operations": [
        {
          "type": "removeVersion",
          "line": 6,
          "packageId": "Newtonsoft.Json",
          "originalText": "13.0.3",
          "description": "Remove Version=\"13.0.3\" from Newtonsoft.Json"
        }
      ]
    }
  ],
  "conflicts": [],
  "warnings": [],
  "stats": {
    "totalProjects": 5,
    "sdkStyleProjects": 5,
    "legacyProjects": 0,
    "totalPackageReferences": 25,
    "uniquePackages": 18,
    "conflictCount": 0,
    "filesCreated": 1,
    "filesModified": 5,
    "versionsRemoved": 25
  }
}
```

---

## IDE Integration

This CLI is designed as the backend engine for IDE extensions. The JSON plan output (`--json`) follows a versioned schema (`schemaVersion: "1.1"`) that IDE adapters consume. .NET hosts can also reference `CentralPackageMigrator.Core` and call `IMigrationService` in-process.

See [docs/JSON_SCHEMA.md](docs/JSON_SCHEMA.md) for the full contract specification.

### Integration Pattern

```
IDE Extension                    CLI Engine
     |                               |
     |--- cpm-migrate plan --json -->|
     |                               |--- scan projects
     |                               |--- detect conflicts
     |                               |--- generate plan
     |<--- JSON plan ----------------|
     |                               |
     |--- cpm-migrate apply -------->|
     |                               |--- write files
     |<--- result --------------------|
```

---

## Building from Source

```bash
git clone https://github.com/Alexey-Stp/directory-packages-build-props-extensions.git
cd directory-packages-build-props-extensions

# Build
dotnet build

# Run tests
dotnet test

# Skip the end-to-end test that runs a real dotnet restore (needs NuGet access)
dotnet test --filter "Category!=E2E"

# Run the CLI locally
dotnet run --project src/CentralPackageMigrator.Cli -- analyze

# Build release binary
dotnet publish src/CentralPackageMigrator.Cli -c Release --self-contained -r osx-arm64
```

---

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md).

## Security

See [SECURITY.md](SECURITY.md).

## License

[MIT](LICENSE)
