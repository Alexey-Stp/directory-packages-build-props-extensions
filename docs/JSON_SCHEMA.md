# JSON Schema — IDE Integration Contract

## Overview

The `cpm-migrate` CLI outputs a versioned JSON plan that IDE adapters can consume to present migration information, preview changes, and apply edits.

The schema is designed for **forward compatibility**: new fields may be added in future versions, but existing fields will not be removed or change meaning within the same major version.

## Schema Version

```json
{
  "schemaVersion": "1.0"
}
```

**Versioning policy:**
- **Patch** (1.0.x): Bug fixes, no schema changes
- **Minor** (1.x.0): New optional fields added (backward compatible)
- **Major** (x.0.0): Breaking changes to existing fields

IDE adapters should check `schemaVersion` and handle unknown fields gracefully (ignore them).

## Full Example

```json
{
  "schemaVersion": "1.0",
  "timestamp": "2024-01-15T10:30:00+00:00",
  "solutionRoot": "/home/user/myproject",
  "creates": [
    {
      "relativePath": "Directory.Packages.props",
      "content": "<Project>\n  <PropertyGroup>\n    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>\n  </PropertyGroup>\n\n  <ItemGroup>\n    <PackageVersion Include=\"FluentValidation\" Version=\"11.9.0\" />\n    <PackageVersion Include=\"Newtonsoft.Json\" Version=\"13.0.3\" />\n    <PackageVersion Include=\"Serilog\" Version=\"3.1.1\" />\n  </ItemGroup>\n</Project>\n",
      "description": "Central Package Management file with all package versions"
    }
  ],
  "edits": [
    {
      "relativePath": "src/AppA/AppA.csproj",
      "operations": [
        {
          "type": "removeVersion",
          "line": 6,
          "packageId": "Newtonsoft.Json",
          "originalText": "13.0.3",
          "description": "Remove Version=\"13.0.3\" from Newtonsoft.Json"
        },
        {
          "type": "removeVersion",
          "line": 7,
          "packageId": "Serilog",
          "originalText": "3.1.1",
          "description": "Remove Version=\"3.1.1\" from Serilog"
        }
      ]
    }
  ],
  "conflicts": [
    {
      "package": {
        "id": "Newtonsoft.Json"
      },
      "entries": [
        {
          "version": "13.0.3",
          "projects": ["src/AppA/AppA.csproj", "src/AppC/AppC.csproj"]
        },
        {
          "version": "12.0.3",
          "projects": ["src/AppB/AppB.csproj"]
        }
      ],
      "resolvedVersion": null
    }
  ],
  "warnings": [
    {
      "code": "CPM001",
      "message": "Legacy project format detected — will be skipped: src/Legacy/Legacy.csproj",
      "projectPath": "src/Legacy/Legacy.csproj"
    }
  ],
  "stats": {
    "totalProjects": 4,
    "sdkStyleProjects": 3,
    "legacyProjects": 1,
    "totalPackageReferences": 10,
    "uniquePackages": 6,
    "conflictCount": 1,
    "filesCreated": 1,
    "filesModified": 3,
    "versionsRemoved": 8
  }
}
```

## Field Reference

### Root Object

| Field             | Type             | Description                                    |
|-------------------|------------------|------------------------------------------------|
| `schemaVersion`   | `string`         | Schema version (e.g., `"1.0"`)                |
| `timestamp`       | `string` (ISO 8601) | UTC time when plan was generated           |
| `solutionRoot`    | `string`         | Absolute path to the solution root             |
| `creates`         | `FileCreate[]`   | Files to be created                            |
| `edits`           | `FileEdit[]`     | Files to be edited                             |
| `conflicts`       | `ConflictGroup[]`| Version conflicts                              |
| `warnings`        | `Warning[]`      | Non-fatal warnings                             |
| `stats`           | `Stats`          | Aggregate statistics                           |

### FileCreate

| Field          | Type     | Description                              |
|----------------|----------|------------------------------------------|
| `relativePath` | `string` | Path relative to solution root           |
| `content`      | `string` | Full file content to write               |
| `description`  | `string` | Human-readable description               |

### FileEdit

| Field          | Type              | Description                        |
|----------------|-------------------|------------------------------------|
| `relativePath` | `string`          | Path relative to solution root     |
| `operations`   | `EditOperation[]` | Ordered list of changes            |

### EditOperation

| Field             | Type     | Description                              |
|-------------------|----------|------------------------------------------|
| `type`            | `string` | One of: `removeVersion`, `replaceLine`, `insertLine`, `removeLine` |
| `line`            | `int`    | 1-based line number                      |
| `packageId`       | `string?`| Related package ID                       |
| `originalText`    | `string?`| Text being replaced/removed              |
| `replacementText` | `string?`| Replacement text (for replace ops)       |
| `description`     | `string` | Human-readable description               |

### ConflictGroup

| Field             | Type             | Description                          |
|-------------------|------------------|--------------------------------------|
| `package`         | `PackageIdentity`| The conflicting package              |
| `entries`         | `ConflictEntry[]`| Version groups                       |
| `resolvedVersion` | `string?`        | Resolution (null if unresolved)      |

### ConflictEntry

| Field      | Type       | Description                    |
|------------|------------|--------------------------------|
| `version`  | `string`   | Version string                 |
| `projects` | `string[]` | Project relative paths         |

## Warning Codes

| Code   | Meaning                                           |
|--------|---------------------------------------------------|
| CPM001 | Legacy (non-SDK-style) project detected            |
| CPM002 | Project has no PackageReference elements           |
| CPM003 | Project already uses Central Package Management    |
| CPM004 | Conditional PackageReference with version          |
| CPM005 | Malformed XML in project file                      |
| CPM006 | `Update=` PackageReference detected                |
| CPM007 | Existing `Directory.Packages.props` found          |
| CPM008 | `VersionOverride` attribute detected               |

## Error Codes

| Code                      | Meaning                              |
|---------------------------|--------------------------------------|
| ERR_SOLUTION_NOT_FOUND    | No solution root could be determined |
| ERR_NO_PROJECTS_FOUND     | No .csproj files found               |
| ERR_UNRESOLVED_CONFLICTS  | Apply blocked by conflicts           |
| ERR_MALFORMED_PROJECT     | Invalid XML in project file          |
| ERR_FILE_WRITE_FAILED     | File write operation failed          |
| ERR_RESTORE_FAILED        | `dotnet restore` failed              |
| ERR_BUILD_FAILED          | `dotnet build` failed                |
| ERR_INVALID_PATH          | Invalid or traversal path            |
| ERR_ALREADY_MIGRATED      | Solution already uses CPM            |
| ERR_PLAN_FILE_NOT_FOUND   | Specified plan file not found        |
| ERR_INVALID_PLAN_FILE     | Plan file is malformed               |
