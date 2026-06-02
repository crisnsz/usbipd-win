# usbipd-win Scripts

Utility scripts for building, releasing, and packaging usbipd-win.

## Scripts

### Release-Usbipd.ps1

Located in the project root. Build and release management script.

**Usage:**
```powershell
# Build everything and prepare release folder
.\Release-Usbipd.ps1

# Build and create GitHub release (draft)
.\Release-Usbipd.ps1 -CreateRelease

# Skip build step (use existing artifacts)
.\Release-Usbipd.ps1 -SkipBuild

# Skip tests
.\Release-Usbipd.ps1 -SkipTests

# Override detected version
.\Release-Usbipd.ps1 -Version "4.1.0"
```

**Output:**
- `./release/` folder with MSI files and checksums
- (Optional) GitHub draft release with uploads

**Requirements:**
- .NET SDK 10.0.300+
- GitHub CLI (for -CreateRelease flag)

---

### Create-WingetManifest.ps1

Generate Windows Package Manager (winget) manifest files for a release.

**Usage:**
```powershell
.\scripts\Create-WingetManifest.ps1 `
    -Version "4.1.0" `
    -GitHubReleaseUrl "https://github.com/crisnsz/usbipd-win/releases/download/v4.1.0"

# Specify output directory
.\scripts\Create-WingetManifest.ps1 `
    -Version "4.1.0" `
    -GitHubReleaseUrl "https://github.com/crisnsz/usbipd-win/releases/download/v4.1.0" `
    -OutputPath ".\winget-manifests"
```

**Output:**
```
crisnsz.usbipd-win/
  4.1.0/
    crisnsz.usbipd-win.yaml
    crisnsz.usbipd-win.installer.yaml
    crisnsz.usbipd-win.locale.en-US.yaml
```

**Parameters:**
- `Version` (required): Semantic version number (e.g., `4.1.0`)
- `GitHubReleaseUrl` (required): URL to GitHub release assets (e.g., `https://github.com/crisnsz/usbipd-win/releases/download/v4.1.0`)
- `OutputPath` (optional): Directory for output manifests. Defaults to current directory.

**Requirements:**
- PowerShell 7.0+
- Internet connection (downloads MSI files to calculate checksums)

**Next Steps:**
1. Submit manifests to [microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs)
2. Create pull request with the manifest folder

---

## Complete Release Workflow

1. **Prepare release locally:**
   ```powershell
   .\Release-Usbipd.ps1
   ```

2. **Create GitHub release:**
   ```powershell
   .\Release-Usbipd.ps1 -CreateRelease
   ```

3. **Publish release on GitHub** (edit and confirm in web UI)

4. **Automatic winget update:**
   - GitHub Actions workflow `winget.yml` automatically submits to winget

5. **(Optional) Manual winget manifest:**
   ```powershell
   .\scripts\Create-WingetManifest.ps1 -Version "4.1.0" -GitHubReleaseUrl "https://github.com/crisnsz/usbipd-win/releases/download/v4.1.0"
   ```

See [docs/RELEASE.md](../docs/RELEASE.md) for detailed documentation.
