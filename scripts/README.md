<!-- SPDX-FileCopyrightText: 2026 crisnsz -->
<!-- SPDX-License-Identifier: GPL-3.0-only -->

# usbipd-win Scripts

Utility scripts for building, releasing, and packaging usbipd-win.

## ⚠️ Note: These Scripts Are Legacy

**The recommended way to release is via GitHub Actions using git tags.** See [docs/RELEASE.md](../docs/RELEASE.md) for automated CI/CD process.

These scripts are primarily for **local development and testing**. The GitHub Actions workflows handle production releases automatically.

---

## Scripts

### Release-Usbipd.ps1

Located in the project root. Local build and release preparation script.

**Usage (Local Development Only):**
```powershell
# Build everything and prepare release folder locally
.\Release-Usbipd.ps1

# Skip tests
.\Release-Usbipd.ps1 -SkipTests

# Override detected version
.\Release-Usbipd.ps1 -Version "5.4.0"
```

**Output:**
- `./release/` folder with MSI files and checksums

**Note**: This does NOT upload to GitHub. For actual releases, use git tags instead.

---

### Create-WingetManifest.ps1

Generate Windows Package Manager (winget) manifest files for a release.

**Usage (Manual Only - Usually Automatic):**
```powershell
.\scripts\Create-WingetManifest.ps1 `
    -Version "5.4.0" `
    -GitHubReleaseUrl "https://github.com/crisnsz/usbipd-win/releases/download/v5.4.0"

# Specify output directory
.\scripts\Create-WingetManifest.ps1 `
    -Version "5.4.0" `
    -GitHubReleaseUrl "https://github.com/crisnsz/usbipd-win/releases/download/v5.4.0" `
    -OutputPath ".\winget-manifests"
```

**Output:**
```
crisnsz.usbipd-win/
  5.4.0/
    crisnsz.usbipd-win.yaml
    crisnsz.usbipd-win.installer.yaml
    crisnsz.usbipd-win.locale.en-US.yaml
```

**Parameters:**
- `Version` (required): Semantic version number (e.g., `5.4.0`)
- `GitHubReleaseUrl` (required): URL to GitHub release assets
- `OutputPath` (optional): Directory for output manifests. Defaults to current directory.

**Requirements:**
- PowerShell 7.0+
- Internet connection (downloads MSI files to calculate checksums)

**Note**: This is automatically done by the `winget.yml` GitHub Action workflow.

---

## Recommended Release Workflow (Automated via GitHub)

1. **Merge PR to master** (GitHub compiles and tests automatically)

2. **Create git tag** and push:
   ```bash
   git tag -a v5.4.0 -m "Release version 5.4.0"
   git push origin v5.4.0
   ```

3. **GitHub Actions handles everything automatically:**
   - Compiles code
   - Tests build
   - Creates MSI installers
   - Creates GitHub Release
   - Sends to winget package manager

See [docs/RELEASE.md](../docs/RELEASE.md) for complete documentation.

---

## When to Use These Scripts

| Scenario | Tool |
|----------|------|
| Local testing before PR | `Release-Usbipd.ps1` |
| Manual winget manifest | `Create-WingetManifest.ps1` |
| Production release | `git tag v5.4.0` → GitHub Actions |
| CI/CD build | GitHub Actions (automatic) |

---

## Legacy Information

These scripts were created to support **local development workflows**. However, the GitHub Actions workflows (`release.yml` and `winget.yml`) are now the recommended way to create and publish releases.

Benefits of GitHub Actions automation:
- ✅ No manual script execution needed
- ✅ Consistent builds on GitHub runners
- ✅ Automatic winget integration
- ✅ Better audit trail and history
- ✅ Faster release process
