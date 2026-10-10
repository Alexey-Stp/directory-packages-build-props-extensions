# Releasing cpm-migrate

Pushing a `vX.Y.Z` tag runs [`.github/workflows/release.yml`](../.github/workflows/release.yml). It publishes two channels from the same commit:

| Channel     | Artifact                                                                                         |
|-------------|--------------------------------------------------------------------------------------------------|
| dotnet tool | `CentralPackageMigrator` on nuget.org (`dotnet tool install -g CentralPackageMigrator`)           |
| Binaries    | Self-contained `cpm-migrate-{win-x64.exe,linux-x64,osx-x64,osx-arm64}` + `SHA256SUMS.txt` on GitHub Releases |

## How the pipeline gates a release

```
ci (full CI suite on the tagged commit)
 ├─ build  (4 RIDs: publish → sign/notarize → run `--version` == tag → upload)
 └─ pack   (dotnet pack → package version == tag → install tool → `--version` == tag)
      └─ github-release (SHA256SUMS.txt + self-check → attestations → gh release create)
           └─ nuget-publish (environment `release` → attestation → dotnet nuget push)
```

- **One version source.** MinVer derives the version from the tag. Assembly version, file version, NuGet version and `cpm-migrate --version` all equal `X.Y.Z`. Any mismatch fails the job before anything is published.
- **No unsigned binaries from a tag.** Without signing secrets, a `workflow_dispatch` dry run warns and continues. A `v*` tag fails instead.
- **Pre-release tags** (`v0.2.0-rc.1`) produce a GitHub pre-release and a pre-release NuGet package.
- **Dry run.** Use Actions → Release → *Run workflow* on any branch. It builds, verifies and uploads the artifacts to the run, but publishes nothing.

## One-time setup

### 1. nuget.org

1. Check that the ID is still free: `https://www.nuget.org/packages/CentralPackageMigrator` should return 404. If it is taken, change `PackageId` in `src/CentralPackageMigrator.Cli/CentralPackageMigrator.Cli.csproj` and the README before the first tag.
2. Create an API key: nuget.org → API Keys. Scope it to **Push new packages and package versions** with the glob pattern `CentralPackageMigrator`, and set an expiry.
3. Create a GitHub environment named `release` (Settings → Environments). Optionally require a reviewer. Add the key as the environment secret `NUGET_API_KEY`.

NuGet trusted publishing (OIDC) can replace the long-lived key later. The `nuget-publish` job already runs with `id-token: write`.

### 2. Windows signing (Azure Artifact Signing)

This is the recommended option. There is no hardware token or `.pfx` to manage, it builds SmartScreen reputation, and it costs about $10/month on the Basic tier.

1. In Azure, create an **Artifact Signing** account (formerly Trusted Signing). Complete **identity validation**, then create a **Public Trust** certificate profile.
   - Organizations are eligible in the US, Canada, the EU and the UK. Individual developers are eligible in the US and Canada.
2. Create an app registration (service principal). Give it the **Artifact Signing Certificate Profile Signer** role on the account. Some tenants still list it under its old name, *Trusted Signing Certificate Profile Signer*.
3. Add these repository **secrets**: `AZURE_TENANT_ID`, `AZURE_CLIENT_ID`, `AZURE_CLIENT_SECRET`.
4. Add these repository **variables**:
   - `AZURE_SIGNING_ENDPOINT`: the account's regional endpoint, e.g. `https://eus.codesigning.azure.net/`
   - `AZURE_SIGNING_ACCOUNT`
   - `AZURE_CERTIFICATE_PROFILE`

A new certificate starts with little SmartScreen reputation. Reputation builds as people download signed releases, so the first release may still show a "not commonly downloaded" notice to some users.

### 3. macOS signing and notarization

1. Join the Apple Developer Program ($99/year). Create a **Developer ID Application** certificate and export it with its private key as a `.p12`.
2. In App Store Connect → Users and Access → Integrations, create an **App Store Connect API key** with the Developer role and download the `.p8`.
3. Add these repository **secrets**:
   - `MACOS_CERT_P12_BASE64`: `base64 -i cert.p12`
   - `MACOS_CERT_PASSWORD`
   - `APPLE_API_KEY_P8_BASE64`: `base64 -i AuthKey_XXXX.p8`
   - `APPLE_API_KEY_ID`
   - `APPLE_API_ISSUER_ID`

Binaries are signed with the hardened runtime and [`build/macos/entitlements.plist`](../build/macos/entitlements.plist), which a self-contained .NET app needs (JIT, extracted native libraries). A bare executable cannot have its notarization ticket stapled, so Gatekeeper checks it online the first time the binary runs.

### 4. Repository settings

- Settings → Code security → enable **Private vulnerability reporting** (`SECURITY.md` points there).
- Settings → Branches → protect `main` and require these status checks: `build-and-test (ubuntu-latest)`, `build-and-test (windows-latest)`, `build-and-test (macos-latest)`, `format-check`, `identity-guard`, `contract-guard`.

## Cutting a release

1. Optional: do a dry run (Actions → Release → *Run workflow*) from `main`.
2. In `CHANGELOG.md`, rename `## [X.Y.Z] - TBD` to today's date (or move the `[Unreleased]` items under a new `## [X.Y.Z] - YYYY-MM-DD`) and update the link references. Merge that to `main`.
3. Tag the merge commit and push the tag:
   ```bash
   git switch main && git pull
   git tag vX.Y.Z
   git push origin vX.Y.Z
   ```
4. Watch the Release run. If `nuget-publish` fails after the GitHub Release was created (for example, an expired key), fix the cause and re-run the failed job. The push uses `--skip-duplicate`.

Never move or re-push a published tag. Cut a new patch version instead.

## Verifying a release (acceptance scenarios)

Run these against the published artifacts, not local builds, on clean machines.

1. **dotnet tool** (any OS with the .NET 8 SDK):
   ```bash
   dotnet tool install -g CentralPackageMigrator
   cpm-migrate --version      # prints X.Y.Z
   ```
   A new version can take up to about 15 minutes to appear after nuget.org indexes it.
2. **Checksums and provenance:**
   ```bash
   gh release download vX.Y.Z --repo Alexey-Stp/directory-packages-build-props-extensions
   sha256sum -c SHA256SUMS.txt                  # every line: OK
   gh attestation verify cpm-migrate-linux-x64 --repo Alexey-Stp/directory-packages-build-props-extensions
   ```
3. **OS trust:**
   - On a clean Windows machine, download `cpm-migrate-win-x64.exe` with a browser and run it. There should be no SmartScreen block, and the file's Properties → Digital Signatures should show a valid signature.
   - On a clean Mac, download `cpm-migrate-osx-arm64` (or `-osx-x64`) with a browser and run `chmod +x` and then `--version`. There should be no Gatekeeper block. `spctl -a -vv -t install <binary>` should report `source=Notarized Developer ID`.
4. **Published identity:** the CI and Release badges in the README are green and the links resolve. `git clone` with the README URL works, and the security link opens the advisory form.
