# cpm-migrate — Central Package Management Migration Tool

[![CI](https://github.com/your-org/cpm-migrate/actions/workflows/ci.yml/badge.svg)](https://github.com/your-org/cpm-migrate/actions/workflows/ci.yml)
[![Release](https://github.com/your-org/cpm-migrate/actions/workflows/release.yml/badge.svg)](https://github.com/your-org/cpm-migrate/releases)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

A production-grade CLI tool that migrates .NET solutions to [Central Package Management](https://learn.microsoft.com/nuget/consume-packages/central-package-management) (`Directory.Packages.props`).

Designed as the **single source of truth** for migration logic, reusable by VS Code extensions, JetBrains Rider plugins, and any other IDE adapter via a stable JSON contract.

---

## Features

- **Automatic solution root detection** — finds nearest `.sln`, falls back to git root, then current directory
- **Full PackageReference extraction** — `Version` attributes, nested `<Version>` elements, `Condition`, `PrivateAssets`, `IncludeAssets`, `Update=`
- **Version conflict detection** — same package with different versions across projects, with explicit resolution
- **Migration plan system** — JSON-based plan for IDE integration (analyze before applying)
- **Dry-run mode** — preview changes without modifying files
- **Idempotent** — running twice produces no additional changes
- **Legacy project detection** — warns about non-SDK-style projects
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

# 4. Verify the solution builds
cpm-migrate check --build
```

---

## Installation

### Download Binary (Recommended)

Download the self-contained binary for your platform from [GitHub Releases](https://github.com/your-org/cpm-migrate/releases):

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

Scans the solution and reports findings without making changes.

```bash
cpm-migrate analyze [--root <path>] [--json] [--verbose]
```

### `plan`

Generates a complete migration plan. The plan is a JSON document describing all creates, edits, conflicts, and warnings.

```bash
cpm-migrate plan [--root <path>] [--json] [--output plan.json] [--resolve Pkg=Version]
```

### `apply`

Applies the migration: creates `Directory.Packages.props` and removes `Version` attributes from `.csproj` files.

```bash
cpm-migrate apply [--root <path>] [--dry-run] [--json] [--resolve Pkg=Version] [--plan-file plan.json]
```

### `check`

Verifies the migrated solution by running `dotnet restore` (and optionally `dotnet build`).

```bash
cpm-migrate check [--root <path>] [--build] [--json]
```

### `resolve`

Provides conflict resolutions and generates an updated plan.

```bash
cpm-migrate resolve --resolve Newtonsoft.Json=13.0.3 [--resolve Serilog=3.1.1] [--output plan.json]
```

---

## Global Options

| Option      | Description                          |
|-------------|--------------------------------------|
| `--root`    | Solution root path (auto-detected)   |
| `--json`    | Output as machine-readable JSON      |
| `--verbose` | Verbose output                       |
| `--dry-run` | Preview changes without writing      |

---

## Exit Codes

| Code | Meaning              |
|------|----------------------|
| 0    | Success              |
| 1    | Analysis warnings    |
| 2    | Unresolved conflicts |
| 3    | Error                |
| 4    | Invalid arguments    |
| 5    | File system error    |

---

## JSON Plan Output (Example)

```json
{
  "schemaVersion": "1.0",
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
      "operations": [
        {
          "type": "removeVersion",
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

This CLI is designed as the backend engine for IDE extensions. The JSON plan output (`--json`) follows a versioned schema (`schemaVersion: "1.0"`) that IDE adapters consume.

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
git clone https://github.com/your-org/cpm-migrate.git
cd cpm-migrate

# Build
dotnet build

# Run tests
dotnet test

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
