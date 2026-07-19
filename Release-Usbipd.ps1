# SPDX-FileCopyrightText: 2026 crisnsz
#
# SPDX-License-Identifier: GPL-3.0-only

param(
    [Parameter(Mandatory=$false)]
    [string]$Version,

    [switch]$CreateRelease,
    [switch]$SkipBuild,
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"

$projectRoot = "D:\projects\usbipd-win"
$publishPathX64 = "$projectRoot\Usbipd\bin\publish\x64"
$publishPathARM64 = "$projectRoot\Usbipd\bin\publish\arm64"
$installerOutput = "$projectRoot\Installer\bin"
$releaseDir = "$projectRoot\release"

# Detectar versión actual del git
Write-Host "Detecting version from git..." -ForegroundColor Cyan
$gitVersion = & dotnet build-server shutdown 2>$null; dotnet msbuild /t:GetVersion "$projectRoot\Installer\Installer.wixproj" /p:Platform=x64 | Select-String "Version detected as" | ForEach-Object { $_ -replace '.*Version detected as ', '' }

if (-not $gitVersion) {
    throw "Failed to detect version"
}

$gitVersion = $gitVersion.Trim()
Write-Host "Detected version: $gitVersion" -ForegroundColor Green

if ($Version) {
    Write-Host "Overriding with specified version: $Version" -ForegroundColor Yellow
    $gitVersion = $Version
}

# Limpiar artefactos viejos
Write-Host "Cleaning old build artifacts..." -ForegroundColor Cyan
Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $publishPathX64
Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $publishPathARM64
Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $installerOutput

if (-not $SkipBuild) {
    Write-Host "Building and publishing for x64..." -ForegroundColor Cyan
    dotnet publish "$projectRoot\Usbipd\Usbipd.csproj" `
        --configuration Release `
        --runtime win-x64 `
        --self-contained true `
        -p:Platform=x64

    if (-not (Test-Path $publishPathX64)) {
        throw "Publish x64 failed"
    }

    Write-Host "Building and publishing for ARM64..." -ForegroundColor Cyan
    dotnet publish "$projectRoot\Usbipd\Usbipd.csproj" `
        --configuration Release `
        --runtime win-arm64 `
        --self-contained true `
        -p:Platform=arm64

    if (-not (Test-Path $publishPathARM64)) {
        throw "Publish ARM64 failed"
    }

    Write-Host "Publishing PowerShell module..." -ForegroundColor Cyan
    dotnet publish "$projectRoot\Usbipd.PowerShell\Usbipd.PowerShell.csproj" `
        --configuration Release

    if (-not $SkipTests) {
        Write-Host "Running tests..." -ForegroundColor Cyan
        dotnet test --configuration Release -p:Platform=x64
    }
}

Write-Host "Building installers..." -ForegroundColor Cyan

dotnet build "$projectRoot\Installer\Installer.wixproj" `
    --configuration Release `
    --no-restore `
    -p:Platform=x64

dotnet build "$projectRoot\Installer\Installer.wixproj" `
    --configuration Release `
    --no-restore `
    -p:Platform=ARM64

# Localizar MSIs
Write-Host "Locating MSI files..." -ForegroundColor Cyan

$msiFiles = @()
$msiFiles += Get-ChildItem "$installerOutput\x64\release\*.msi" -ErrorAction SilentlyContinue
$msiFiles += Get-ChildItem "$installerOutput\ARM64\release\*.msi" -ErrorAction SilentlyContinue

if ($msiFiles.Count -eq 0) {
    throw "No MSI files generated in: $installerOutput"
}

Write-Host "Found $($msiFiles.Count) MSI files:" -ForegroundColor Green
$msiFiles | ForEach-Object { Write-Host "  - $($_.Name)" -ForegroundColor Green }

# Crear directorio de release
if (-not (Test-Path $releaseDir)) {
    New-Item -ItemType Directory -Force $releaseDir | Out-Null
}

# Copiar MSIs
Write-Host "Copying MSI files to release directory..." -ForegroundColor Cyan
Copy-Item -Path $msiFiles.FullName -Destination $releaseDir -Force

# Generar checksums
Write-Host "Generating checksums..." -ForegroundColor Cyan
$checksumFile = "$releaseDir\checksums.txt"
Remove-Item $checksumFile -ErrorAction SilentlyContinue

$msiFiles | ForEach-Object {
    $hash = (Get-FileHash -Path $_.FullName -Algorithm SHA256).Hash
    $copiedFile = Join-Path $releaseDir $_.Name
    Add-Content -Path $checksumFile -Value "$hash  $($_.Name)"
    Write-Host "  $($_.Name): $hash" -ForegroundColor DarkGray
}

Write-Host "Release artifacts ready in: $releaseDir" -ForegroundColor Green

if ($CreateRelease) {
    Write-Host "Creating GitHub release..." -ForegroundColor Cyan

    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
        throw "GitHub CLI (gh) is not installed. Install it or run 'gh release create' manually."
    }

    $tag = "v$gitVersion"
    $releaseNotes = @"
## Release v$gitVersion

### Changes
- Run this release creation and then edit the release notes on GitHub.

### Files
$(Get-ChildItem $releaseDir -Filter "*.msi" | ForEach-Object { "- $($_.Name)" })

### Verification
Verify file integrity using the checksums.txt file.
"@

    try {
        Write-Host "Creating tag: $tag" -ForegroundColor Yellow
        git tag -a $tag -m "Release $gitVersion" 2>$null
        git push origin $tag

        Write-Host "Creating GitHub release..." -ForegroundColor Yellow
        $releaseNotes | gh release create $tag `
            --title "usbipd-win v$gitVersion" `
            --notes - `
            --draft `
            ($msiFiles | ForEach-Object { Join-Path $releaseDir $_.Name })

        Write-Host "✓ GitHub release created (draft mode)" -ForegroundColor Green
        Write-Host "  Review and publish at: https://github.com/crisnsz/usbipd-win/releases/tag/$tag" -ForegroundColor Cyan
    }
    catch {
        Write-Host "Warning: GitHub release creation failed" -ForegroundColor Yellow
        Write-Host "  Manual: gh release create v$gitVersion --draft -t 'usbipd-win v$gitVersion'" -ForegroundColor Yellow
        Write-Host "  Then upload files from: $releaseDir" -ForegroundColor Yellow
    }
}

Write-Host "`n✓ Release process completed" -ForegroundColor Green
Write-Host "  Version: $gitVersion" -ForegroundColor Green
Write-Host "  Location: $releaseDir" -ForegroundColor Green

if (-not $CreateRelease) {
    Write-Host "`n  To create a GitHub release, run:" -ForegroundColor Cyan
    Write-Host "    .\Release-Usbipd.ps1 -CreateRelease" -ForegroundColor Yellow
}
