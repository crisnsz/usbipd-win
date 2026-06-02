# Release Process for usbipd-win

This document describes how to create and publish releases for usbipd-win to both GitHub Releases and Windows Package Manager (winget).

## Overview

The release process is **fully automated via GitHub Actions**:
1. Create a git tag (`v5.4.0`)
2. GitHub Actions builds the project automatically
3. GitHub Actions creates MSI installers for x64 and ARM64
4. GitHub Actions publishes to GitHub Releases automatically
5. GitHub Actions triggers winget update automatically

## Prerequisites

- Git with signed commits configured ✅ (already done)
- GitHub CLI (`gh`) - optional, for manual tag creation
- WINGET_TOKEN secret configured in GitHub ✅ (already done)

## Release Steps (Fully Automated)

### 1. Merge PR to Master

Create a pull request with your changes and merge to `master`.

GitHub Actions will automatically:
- Compile the code
- Run tests
- Build MSI installers (x64 and ARM64)

### 2. Create Git Tag

Create and push a version tag. This triggers the entire release pipeline:

```bash
# Create tag (locally)
git tag -a v5.4.0 -m "Release version 5.4.0"

# Push tag to GitHub
git push origin v5.4.0
```

Or using GitHub CLI:
```bash
gh release create v5.4.0 --generate-notes
```

**Tag Format**: Use semantic versioning: `v5.4.0`, `v5.4.1-beta.1`, etc.

### 3. GitHub Actions Release Pipeline

When you push a tag (`v5.4.0`), the `release.yml` workflow automatically:

1. **Builds** the project for x64 and ARM64
2. **Tests** the build
3. **Creates installers** (MSI files)
4. **Calculates checksums** (SHA256)
5. **Creates GitHub Release** with:
   - MSI files attached
   - Checksums documented
   - Automatic release notes
6. **Triggers winget workflow** automatically

You can monitor progress at: https://github.com/crisnsz/usbipd-win/actions

### 4. Winget Automatic Update

Once the GitHub release is published, the workflow `winget.yml` automatically:
- Detects the new release
- Creates a pull request in [microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs)
- Updates the winget package manifest

**Note**: The `winget.yml` workflow requires:
- `WINGET_TOKEN` secret configured in GitHub ✅ (already done)
- A personal access token from winget team ✅ (already done)

## Manual Winget Manifest Creation

If you need to create winget manifests manually (normally done automatically by winget.yml):

```powershell
# Generate manifest files
.\scripts\Create-WingetManifest.ps1 `
    -Version "5.4.0" `
    -GitHubReleaseUrl "https://github.com/crisnsz/usbipd-win/releases/download/v5.4.0"

# Output structure:
# crisnsz.usbipd-win/
#   5.4.0/
#     crisnsz.usbipd-win.yaml
#     crisnsz.usbipd-win.installer.yaml
#     crisnsz.usbipd-win.locale.en-US.yaml
```

To submit manually to winget:
1. Fork https://github.com/microsoft/winget-pkgs
2. Create branch: `submit/crisnsz.usbipd-win/{version}`
3. Copy manifest folder to: `manifests/c/crisnsz/usbipd-win/{version}/`
4. Create pull request

**Note**: This is normally done automatically by the `winget.yml` workflow.

## Release Workflow Timeline

```
Merge PR to master
    ↓
(Optional) Create git tag: git tag v5.4.0
    ↓
Push tag: git push origin v5.4.0
    ↓
GitHub Actions: release.yml triggers
    ├─ Compile (x64 + ARM64)
    ├─ Run tests
    ├─ Build MSI installers
    ├─ Calculate checksums
    └─ Create GitHub Release
    ↓
GitHub Actions: winget.yml triggers automatically
    ↓
Automatic PR created in microsoft/winget-pkgs
    ↓
Winget team reviews + merges (usually within 24h)
    ↓
Package available via: winget install crisnsz.usbipd-win
```

## Local Scripts (Legacy - Not Needed for CI/CD)

The following scripts are available for local development but are NOT needed for the automated CI/CD process:

### Release-Usbipd.ps1

Build and test locally without pushing to GitHub.

```powershell
# Build everything and prepare release folder
.\Release-Usbipd.ps1

# With custom version (for testing)
.\Release-Usbipd.ps1 -Version "5.4.0"
```

### Create-WingetManifest.ps1

Generate winget manifests locally (usually done automatically).

```powershell
.\scripts\Create-WingetManifest.ps1 `
    -Version "5.4.0" `
    -GitHubReleaseUrl "https://github.com/crisnsz/usbipd-win/releases/download/v5.4.0"
```

## Troubleshooting

### Tag Not Triggering Release Workflow

Ensure the tag format matches: `v[0-9]+.[0-9]+.[0-9]+*`

Valid examples:
- `v5.4.0` ✅
- `v5.4.1` ✅
- `v5.4.0-beta.1` ✅
- `v5.4.0-rc.1` ✅

Invalid examples:
- `5.4.0` ❌ (missing `v` prefix)
- `release-5.4.0` ❌ (wrong prefix)
- `v5.4` ❌ (missing patch version)

### Release Workflow Failed

Check the GitHub Actions log:
1. Go to: **Actions** tab on GitHub
2. Find the failed run in **Release** workflow
3. Click to see detailed error logs
4. Common issues:
   - Tests failed → Fix code and create new tag
   - Build failed → Check compiler errors in logs
   - Artifact upload failed → Ensure MSI files were created

### Winget PR Not Created

Verify:
1. GitHub release was created successfully
2. `WINGET_TOKEN` secret is configured
3. MSI files are named correctly:
   - `usbipd-win_{version}_x64.msi`
   - `usbipd-win_{version}_arm64.msi`

## Version Numbers

Versioning follows [Semantic Versioning](https://semver.org/):
- Major.Minor.Patch (e.g., `5.4.0`)
- Pre-release versions: `5.4.0-alpha.1`, `5.4.0-beta.1`, `5.4.0-rc.1`

Version is automatically detected from git via `Dorssel.GitVersion.MsBuild` package.

## Security Considerations

- All commits must be **GPG signed** (enforced by branch protection)
- MSI files are automatically signed as part of the build process
- GitHub Release artifacts have automatic provenance attestation
- winget package includes SHA256 verification

## References

- [Windows Package Manager (winget)](https://learn.microsoft.com/en-us/windows/package-manager/)
- [Winget-pkgs Repository](https://github.com/microsoft/winget-pkgs)
- [Semantic Versioning](https://semver.org/)
- [GitHub Actions Documentation](https://docs.github.com/en/actions)
- [usbipd-win GitHub](https://github.com/crisnsz/usbipd-win)
