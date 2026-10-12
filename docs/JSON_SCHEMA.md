# JSON Schema — IDE Integration Contract

## Overview

The `cpm-migrate` CLI outputs a versioned JSON plan that IDE adapters can consume to present migration information, preview changes, and apply edits.

The schema is designed for **forward compatibility**: new fields may be added in future versions, but existing fields will not be removed or change meaning within the same major version.

## Schema Version

```json
{
  "schemaVersion": "1.1"
}
```

Version 1.1 added optional fields (`originalSha256`, `version`, `condition`, `suggestedVersion`, `severity`, `hasWarnings`), new operation types, the apply/check/error outputs below, and removed the never-emitted `replaceLine`/`insertLine`/`removeLine` operation types and `replacementText` field. Readers of 1.0 plans that ignore unknown fields keep working.

**Versioning policy:**
- **Patch** (1.0.x): Bug fixes, no schema changes
- **Minor** (1.x.0): New optional fields added (backward compatible)
- **Major** (x.0.0): Breaking changes to existing fields

IDE adapters should check `schemaVersion` and handle unknown fields gracefully (ignore them).

## Machine-Readable Schemas

The contract is published as JSON Schema (draft 2020-12) in [`docs/schemas/`](schemas/):

| Output                                             | Schema                                                         |
|----------------------------------------------------|----------------------------------------------------------------|
| `analyze`, `plan`, `resolve`, `plan --output` file | [`plan.schema.json`](schemas/plan.schema.json)                 |
| `apply` (with or without `--dry-run`)              | [`apply-result.schema.json`](schemas/apply-result.schema.json) |
| `check`                                            | [`check-result.schema.json`](schemas/check-result.schema.json) |
| Any failure                                        | [`error.schema.json`](schemas/error.schema.json)               |

The schemas set `additionalProperties: false` so that the contract tests catch every new field. Consumers should still ignore unknown fields rather than validate strictly with these schemas: a later minor version may add optional fields.

## Compatibility Rule

The contract is frozen by `tests/CentralPackageMigrator.Integration.Tests/Contract/JsonContractTests.cs`. It validates every `--json` output against these schemas and compares it byte for byte with the approved snapshots in `Contract/approved/`. It also checks that the full example below is a valid plan.

Any change to the JSON shape fails those tests. To make an intentional change, do all of the following in the same pull request:

1. Bump `MigrationPlan.CurrentSchemaVersion`: minor for new optional fields, major for anything else.
2. Update the `schemaVersion` const and the shape in `docs/schemas/*.schema.json`.
3. Regenerate the approved snapshots with `CPM_UPDATE_CONTRACT=1 dotnet test tests/CentralPackageMigrator.Integration.Tests --filter "FullyQualifiedName~JsonContractTests"` and review the diff.
4. Update this document.

CI rejects a pull request that changes the schemas or the approved snapshots without changing `CurrentSchemaVersion`.

## Full Example

```json
{
  "schemaVersion": "1.1",
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
      "originalSha256": "3e29d7d4f87adbce1a83f137c7755617032c7f1b047f35a6d85b5fd4bc520fef",
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
      "suggestedVersion": "13.0.3"
    }
  ],
  "warnings": [
    {
      "code": "CPM001",
      "message": "Legacy project format detected — will be skipped: src/Legacy/Legacy.csproj",
      "severity": "warning",
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
  },
  "hasUnresolvedConflicts": true,
  "hasWarnings": true,
  "isNoOp": false
}
```

Optional fields whose value is `null` are omitted, never written as `null`. For example, `resolvedVersion` is absent until a conflict is resolved. Paths in `relativePath`, `projects`, `projectPath` and `writtenFiles` use the platform's directory separator. The serializer escapes `<`, `>`, `"` and `'` inside strings as `<`, `>`, `"` and `'`; this is equivalent JSON.

## Field Reference

### Root Object

| Field                    | Type                | Description                                        |
|--------------------------|---------------------|----------------------------------------------------|
| `schemaVersion`          | `string`            | Schema version (e.g., `"1.1"`)                     |
| `timestamp`              | `string` (ISO 8601) | UTC time when plan was generated                   |
| `solutionRoot`           | `string`            | Absolute path to the solution root                 |
| `creates`                | `FileCreate[]`      | New files to create                                |
| `edits`                  | `FileEdit[]`        | Existing files to edit                             |
| `conflicts`              | `ConflictGroup[]`   | Version conflicts                                  |
| `warnings`               | `Warning[]`         | Non-fatal warnings                                 |
| `stats`                  | `Stats`             | Aggregate statistics                               |
| `hasUnresolvedConflicts` | `bool`              | True if any conflict has no `resolvedVersion`      |
| `hasWarnings`            | `bool`              | True if any warning has severity `warning`         |
| `isNoOp`                 | `bool`              | True if there is nothing to create or edit         |

### Stats

All fields are non-negative integers.

| Field                    | Description                                     |
|--------------------------|-------------------------------------------------|
| `totalProjects`          | Project files found under the root              |
| `sdkStyleProjects`       | SDK-style projects (migrated)                   |
| `legacyProjects`         | Legacy (non-SDK-style) projects (skipped)       |
| `totalPackageReferences` | `PackageReference` items with a version         |
| `uniquePackages`         | Distinct package IDs that will be centralized (excludes unresolved conflicts) |
| `conflictCount`          | Packages referenced with more than one version  |
| `filesCreated`           | Entries in `creates`                            |
| `filesModified`          | Entries in `edits`                              |
| `versionsRemoved`        | `removeVersion` operations across all edits     |

### FileCreate

A file that must not exist yet. An existing `Directory.Packages.props` is never recreated; it is edited instead.

| Field          | Type     | Description                              |
|----------------|----------|------------------------------------------|
| `relativePath` | `string` | Path relative to solution root           |
| `content`      | `string` | Full file content to write               |
| `description`  | `string` | Human-readable description               |

### FileEdit

| Field            | Type              | Description                                                        |
|------------------|-------------------|--------------------------------------------------------------------|
| `relativePath`   | `string`          | Path relative to solution root                                     |
| `originalSha256` | `string?`         | SHA-256 of the file content the plan was computed from (lowercase hex). Apply fails with `ERR_STALE_PLAN` if it no longer matches |
| `operations`     | `EditOperation[]` | Changes to apply                                                   |

### EditOperation

| Field          | Type      | Description                                                              |
|----------------|-----------|--------------------------------------------------------------------------|
| `type`         | `string`  | See below                                                                |
| `line`         | `int`     | 1-based line of the targeted element; `0` when not applicable            |
| `packageId`    | `string?` | Related package ID                                                       |
| `originalText` | `string?` | Version being removed or replaced                                        |
| `version`      | `string?` | Version written by `addPackageVersion` / `updatePackageVersion`          |
| `condition`    | `string?` | MSBuild condition of an added `PackageVersion`                           |
| `description`  | `string`  | Human-readable description                                               |

| `type`                           | Allowed in                     | Effect                                                     |
|----------------------------------|--------------------------------|------------------------------------------------------------|
| `removeVersion`                  | project files, `Directory.Build.props/.targets` | Remove the `Version` attribute or `<Version>` element |
| `addPackageVersion`              | `Directory.Packages.props`     | Add a `PackageVersion` item                                |
| `updatePackageVersion`           | `Directory.Packages.props`     | Change an existing unconditional `PackageVersion`          |
| `enableCentralPackageManagement` | `Directory.Packages.props`     | Set `ManagePackageVersionsCentrally` to `true`             |

### ConflictGroup

| Field              | Type              | Description                                              |
|--------------------|-------------------|----------------------------------------------------------|
| `package`          | `PackageIdentity` | The conflicting package                                  |
| `entries`          | `ConflictEntry[]` | Version groups                                           |
| `resolvedVersion`  | `string?`         | Resolution (absent if unresolved)                        |
| `suggestedVersion` | `string?`         | Highest version among the entries, when all parse        |

### ConflictEntry

| Field      | Type       | Description                                                                   |
|------------|------------|-------------------------------------------------------------------------------|
| `version`  | `string`   | Version string                                                                |
| `projects` | `string[]` | Files using it; an existing central version is listed as `Directory.Packages.props` |

### Warning

| Field         | Type      | Description                                      |
|---------------|-----------|--------------------------------------------------|
| `code`        | `string`  | Warning code (see below)                         |
| `message`     | `string`  | Human-readable message                           |
| `severity`    | `string`  | `warning` (affects exit code 1) or `info`        |
| `projectPath` | `string?` | Related file                                     |
| `packageId`   | `string?` | Related package                                  |

## Other Outputs

### `analyze --json`, `plan --json`, `resolve --json`

All three write the plan object described above. `plan --output <file>` writes the same document to a file, which `apply --plan-file` accepts.

### `apply --json`

```json
{
  "schemaVersion": "1.1",
  "dryRun": false,
  "writtenFiles": ["Directory.Packages.props", "src/App/App.csproj"],
  "plan": { "...": "the applied plan" }
}
```

### `check --json`

```json
{
  "schemaVersion": "1.1",
  "success": false,
  "steps": [
    { "name": "restore", "success": false, "exitCode": 1, "timedOut": false, "durationMs": 1830,
      "errorCode": "ERR_RESTORE_FAILED", "standardOutput": "...", "standardError": "" },
    { "name": "build", "success": false, "skipped": true, "timedOut": false, "durationMs": 0 }
  ]
}
```

Process output is included for failed steps, or for all steps with `--verbose`.

### Errors (any command, `--json`)

```json
{
  "schemaVersion": "1.1",
  "success": false,
  "error": { "code": "ERR_STALE_PLAN", "message": "...", "detail": "...", "filePath": "..." }
}
```

Logs are always written to stderr, so stdout holds exactly one JSON document.

## Warning Codes

| Code   | Severity | Meaning                                                              |
|--------|----------|----------------------------------------------------------------------|
| CPM001 | warning  | Legacy (non-SDK-style) project skipped                               |
| CPM002 | info     | Project has no PackageReference elements                             |
| CPM003 | info     | Project already uses Central Package Management                      |
| CPM004 | info     | Conditional PackageReference with version                            |
| CPM005 | warning  | Malformed XML; file skipped                                          |
| CPM006 | warning  | `Update=` PackageReference sets a version (not centralized)          |
| CPM007 | info     | Existing `Directory.Packages.props` found; versions are merged       |
| CPM008 | info     | `VersionOverride` detected; left unchanged                           |
| CPM009 | warning  | `--resolve` given for a package without a conflict                   |
| CPM010 | warning  | Solution lists a project outside the root                            |
| CPM011 | info / warning | Property-based version resolved (info) or unresolved (warning) |
| CPM012 | warning  | Existing central file has only conditional entries for a package     |
| CPM013 | info     | `ManagePackageVersionsCentrally` will be enabled                     |

## Error Codes

| Code                      | Exit | Meaning                                                    |
|---------------------------|------|------------------------------------------------------------|
| ERR_SOLUTION_NOT_FOUND    | 5    | Root directory does not exist                              |
| ERR_NO_PROJECTS_FOUND     | 3    | No project files under the root                            |
| ERR_UNRESOLVED_CONFLICTS  | 2    | Apply blocked by conflicts                                 |
| ERR_MALFORMED_PROJECT     | 3    | A file to edit, or the existing central file, is invalid XML |
| ERR_FILE_WRITE_FAILED     | 5    | A write failed; already-written files were restored        |
| ERR_IO_FAILED             | 5    | Other I/O failure                                          |
| ERR_RESTORE_FAILED        | —    | `check` step error code: `dotnet restore` failed           |
| ERR_BUILD_FAILED          | —    | `check` step error code: `dotnet build` failed             |
| ERR_INVALID_PATH          | 3    | Path is not a plain relative path inside the root, or not a file the tool may touch |
| ERR_PLAN_FILE_NOT_FOUND   | 5    | Specified plan file not found                              |
| ERR_INVALID_PLAN_FILE     | 3    | Plan file malformed, wrong schema major, or for another root |
| ERR_STALE_PLAN            | 3    | Files changed since planning, or the plan was modified     |
| ERR_INVALID_ARGUMENTS     | 4    | Invalid command line or `--resolve` value                  |
| ERR_VERIFICATION_FAILED   | 3    | Edited file failed post-edit verification; nothing written |
| ERR_CANCELLED             | 3    | Cancelled (Ctrl+C)                                         |
| ERR_UNEXPECTED            | 3    | Unexpected failure                                         |
