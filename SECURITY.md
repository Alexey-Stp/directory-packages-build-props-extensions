# Security Policy

## Threat Model

`cpm-migrate` operates on the local file system. Its threat model considers:

1. **Malicious project files** — A `.csproj` file could contain XML designed to exploit parsing. Mitigation: `System.Xml.Linq` is used (not regex-based parsing); malformed XML is caught and reported as a warning.

2. **Path traversal attacks** — A crafted plan could attempt to write outside the solution root. Mitigation: All paths in the plan are validated to resolve within the solution root. Any path containing `..` segments is rejected.

3. **Arbitrary file deletion** — The tool never deletes files. It only creates `Directory.Packages.props` and edits existing `.csproj` files by removing `Version` attributes.

4. **Supply chain** — Downloaded binaries could be tampered with. Mitigation: Every release includes `SHA256SUMS.txt` for checksum verification.

## File Write Policy

The engine maintains an **explicit file write list**:
- It only creates files listed in `plan.creates[]`
- It only edits files listed in `plan.edits[]`
- It never writes to paths outside the solution root
- Dry-run mode validates the plan without any writes

## Path Validation

All file paths go through validation:
1. Resolved to absolute path via `Path.GetFullPath()`
2. Verified to start with the solution root path
3. Rejected if containing `..` segments
4. Edit targets verified to exist on disk

## Reporting Vulnerabilities

If you discover a security vulnerability, please report it privately:

1. **Do not** open a public issue
2. Report it through [GitHub private vulnerability reporting](https://github.com/Alexey-Stp/directory-packages-build-props-extensions/security/advisories/new)
3. Include: description, reproduction steps, impact assessment
4. We will respond within 48 hours

## Supported Versions

| Version | Supported |
|---------|-----------|
| 0.1.x   | Yes      |
