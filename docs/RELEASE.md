# Release Process for usbipd-win

This document describes how to create and publish releases for usbipd-win to both GitHub Releases and Windows Package Manager (winget).

## Overview

The release process involves:
1. Building the project
2. Creating MSI installers for x64 and ARM64
3. Publishing to GitHub Releases
4. Automatically updating winget package (via GitHub workflow)

## Prerequisites

- PowerShell 5.1 or later
- Visual Studio 2022 or .NET SDK 10.0.300+
- GitHub CLI (`gh`) - for automated GitHub release creation
- Git configured and authenticated with GitHub

### Install GitHub CLI

```powershell
winget install GitHub.cli
```

## Release Steps

### 1. Prepare Release (Local Development)

Build and prepare release artifacts locally:

```powershell
# Build everything and prepare release folder
.\Release-Usbipd.ps1

# Or skip tests for faster builds
.\Release-Usbipd.ps1 -SkipTests

# Or with custom version (for testing)
.\Release-Usbipd.ps1 -Version "4.1.0-beta.1"
```

This will:
- Build the project (x64 and ARM64)
- Run tests
- Generate MSI installers
- Copy files to `.\release\` directory
- Generate checksums

### 2. Create GitHub Release

```powershell
# Build everything and create draft release on GitHub
.\Release-Usbipd.ps1 -CreateRelease
```

This will:
- Create git tag: `v{version}`
- Create draft release on GitHub with MSI files
- Generate checksums

**Important**: The release is created in **DRAFT** mode. You must:
1. Review the release on GitHub: https://github.com/crisnsz/usbipd-win/releases
2. Add release notes with changes
3. Click "Publish release"

### 3. Winget Automatic Update

Once you **publish** the GitHub release, the workflow `winget.yml` automatically:
- Detects the new release
- Creates a pull request in [microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs)
- Updates the winget package manifest

**Note**: The `winget.yml` workflow requires:
- `WINGET_TOKEN` secret configured in GitHub
- A personal access token from winget team

## Manual Winget Manifest Creation

If you need to create winget manifests manually or for testing:

```powershell
# Generate manifest files
.\scripts\Create-WingetManifest.ps1 `
    -Version "4.1.0" `
    -GitHubReleaseUrl "https://github.com/crisnsz/usbipd-win/releases/download/v4.1.0"

# Output structure:
# crisnsz.usbipd-win/
#   4.1.0/
#     crisnsz.usbipd-win.yaml
#     crisnsz.usbipd-win.installer.yaml
#     crisnsz.usbipd-win.locale.en-US.yaml
```

To submit manually to winget:
1. Fork https://github.com/microsoft/winget-pkgs
2. Create branch: `submit/crisnsz.usbipd-win/{version}`
3. Copy manifest folder to: `manifests/d/dorssel/usbipd-win/{version}/`
4. Create pull request

## Release Workflow

### Timeline

```
Local Development
    ↓
Build + Test (Release-Usbipd.ps1)
    ↓
Review artifacts in ./release/
    ↓
Create Draft GitHub Release (-CreateRelease)
    ↓
Review and Edit Release Notes on GitHub
    ↓
Publish Release
    ↓
GitHub Actions: winget.yml triggers
    ↓
Automatic PR created in microsoft/winget-pkgs
    ↓
Winget team reviews + merges (usually within 24h)
    ↓
Package available via: winget install crisnsz.usbipd-win
```

## Advanced Usage

### Skip Build (Use Existing Build)

If you've already built and just want to create a release:

```powershell
.\Release-Usbipd.ps1 -SkipBuild
```

### Skip Tests

For faster builds during development:

```powershell
.\Release-Usbipd.ps1 -SkipTests
```

### Override Version Detection

For testing release scripts with a specific version:

```powershell
.\Release-Usbipd.ps1 -Version "4.1.0-beta.1"
```

## Troubleshooting

### "GitHub CLI (gh) is not installed"

```powershell
# Install GitHub CLI
winget install GitHub.cli

# Or install with chocolatey
choco install gh

# Verify installation
gh --version
```

### "dotnet build-server shutdown" fails

This is a harmless warning during version detection. The process continues normally.

### GitHub Release Creation Fails

The script will provide a manual command to run:

```powershell
# Use gh to create release manually
gh release create v4.1.0 `
    --title "usbipd-win v4.1.0" `
    --notes "Your release notes here" `
    --draft `
    .\release\*.msi
```

### Winget PR Not Created

Verify that:
1. Release was published (not in draft)
2. `WINGET_TOKEN` secret is configured in GitHub repository settings
3. Token hasn't expired (tokens need renewal periodically)

For manual submission:
1. Go to https://github.com/microsoft/winget-pkgs
2. Fork the repository
3. Add manifest files from `Create-WingetManifest.ps1`
4. Create pull request

## Files Modified During Release

- `./release/` - Temporary directory with MSI files and checksums
  - `usbipd-win_X.Y.Z_x64.msi`
  - `usbipd-win_X.Y.Z_arm64.msi`
  - `checksums.txt`

## Security Considerations

- MSI files are automatically signed as part of the build process
- Checksums in `checksums.txt` should be verified by users
- GitHub Release artifacts have automatic provenance attestation
- winget package includes SHA256 verification

## Version Numbers

Versioning follows [Semantic Versioning](https://semver.org/):
- Major.Minor.Patch (e.g., `4.1.0`)
- Pre-release versions: `4.1.0-beta.1`, `4.1.0-rc.1`

Version is automatically detected from git via `Dorssel.GitVersion.MsBuild` package.

To set/override version in git:
```powershell
# Create annotated tag
git tag -a v4.1.0 -m "Release 4.1.0"
git push origin v4.1.0
```

## References

- [Windows Package Manager (winget)](https://learn.microsoft.com/en-us/windows/package-manager/)
- [Winget-pkgs Repository](https://github.com/microsoft/winget-pkgs)
- [Semantic Versioning](https://semver.org/)
- [usbipd-win GitHub](https://github.com/crisnsz/usbipd-win)
