# SPDX-FileCopyrightText: 2026 crisnsz
#
# SPDX-License-Identifier: GPL-3.0-only

#Requires -Version 7.0

param(
    [Parameter(Mandatory=$true)]
    [string]$Version,

    [Parameter(Mandatory=$true)]
    [string]$GitHubReleaseUrl,

    [string]$OutputPath = "."
)

$ErrorActionPreference = "Stop"

# Validar versión
if (-not ($Version -match '^\d+\.\d+\.\d+(-.*)?$')) {
    throw "Invalid version format. Expected semantic versioning (e.g., 1.0.0)"
}

Write-Host "Creating winget manifests for version: $Version" -ForegroundColor Cyan

# Limpiar versión (remover 'v' prefix si existe)
$cleanVersion = $Version -replace '^v', ''

# URLs de los instaladores
$x64Url = "$GitHubReleaseUrl/usbipd-win_${cleanVersion}_x64.msi"
$arm64Url = "$GitHubReleaseUrl/usbipd-win_${cleanVersion}_arm64.msi"

Write-Host "Downloading MSI files to calculate checksums..." -ForegroundColor Yellow

# Descargar y calcular checksums
try {
    $tempDir = New-TemporaryDirectory
    $x64File = "$tempDir\x64.msi"
    $arm64File = "$tempDir\arm64.msi"

    Write-Host "Downloading x64 installer..." -ForegroundColor Gray
    Invoke-WebRequest -Uri $x64Url -OutFile $x64File -ErrorAction Stop
    $x64Hash = (Get-FileHash -Path $x64File -Algorithm SHA256).Hash

    Write-Host "Downloading ARM64 installer..." -ForegroundColor Gray
    Invoke-WebRequest -Uri $arm64Url -OutFile $arm64File -ErrorAction Stop
    $arm64Hash = (Get-FileHash -Path $arm64File -Algorithm SHA256).Hash

    $x64Size = (Get-Item $x64File).Length
    $arm64Size = (Get-Item $arm64File).Length

    Remove-Item $tempDir -Recurse -Force
}
catch {
    Write-Host "Error downloading files. Make sure the release exists." -ForegroundColor Red
    Write-Host "  x64: $x64Url" -ForegroundColor Yellow
    Write-Host "  ARM64: $arm64Url" -ForegroundColor Yellow
    exit 1
}

# Versión del manifiesto de winget
$manifestVersion = $cleanVersion

# Crear directorios
$manifestDir = "$OutputPath\crisnsz.usbipd-win\$manifestVersion"
if (-not (Test-Path $manifestDir)) {
    New-Item -ItemType Directory -Force $manifestDir | Out-Null
}

# === Versión del instalador (Installer.yaml) ===
$installerYaml = @"
# Created by: Create-WingetManifest.ps1
# yaml-language-server: `$schema=https://raw.githubusercontent.com/microsoft/winget-cli/master/schemas/json/manifests/v1.6.0/manifest.installer.6.0.schema.json

PackageIdentifier: crisnsz.usbipd-win
PackageVersion: $manifestVersion
InstallerLocale: en-US
Installers:
  - Architecture: x64
    InstallerType: msi
    InstallerUrl: $x64Url
    InstallerSha256: $x64Hash
    ProductCode: '{EA1D5623-E6A7-4E4A-9259-E39722$($([System.Byte]::Parse($cleanVersion.Split('.')[0])).ToString('X2'))$($([System.Byte]::Parse($cleanVersion.Split('.')[1])).ToString('X2'))$($([System.Byte]::Parse($cleanVersion.Split('.')[2])).ToString('X2'))}'
  - Architecture: arm64
    InstallerType: msi
    InstallerUrl: $arm64Url
    InstallerSha256: $arm64Hash
    ProductCode: '{EA1D5623-E6A7-4E4A-9259-E39722$($([System.Byte]::Parse($cleanVersion.Split('.')[0])).ToString('X2'))$($([System.Byte]::Parse($cleanVersion.Split('.')[1])).ToString('X2'))$($([System.Byte]::Parse($cleanVersion.Split('.')[2])).ToString('X2'))}'
MinimumOSVersion: 10.0.17763
InstallModes:
  - silent
  - silentWithProgress
UpgradeBehavior: install
"@

# === Metadatos del paquete (Locale.yaml) ===
$localeYaml = @"
# Created by: Create-WingetManifest.ps1
# yaml-language-server: `$schema=https://raw.githubusercontent.com/microsoft/winget-cli/master/schemas/json/manifests/v1.6.0/manifest.locale.1.6.0.schema.json

PackageIdentifier: crisnsz.usbipd-win
PackageVersion: $manifestVersion
PackageLocale: en-US
Publisher: crisnsz
PublisherUrl: https://github.com/crisnsz/usbipd-win
PrivacyUrl: https://github.com/crisnsz/usbipd-win/blob/master/COPYING.md
Author: crisnsz
AuthorUrl: https://github.com/crisnsz
PackageName: usbipd-win
PackageUrl: https://github.com/crisnsz/usbipd-win
License: GPL-3.0-only
LicenseUrl: https://github.com/crisnsz/usbipd-win/blob/master/COPYING.md
Copyright: 2020 Frans van Dorsselaer
CopyrightUrl: https://github.com/crisnsz/usbipd-win/blob/master/COPYING.md
ShortDescription: USB/IP server for Windows
Description: |
  usbipd-win allows you to share USB devices with a Hyper-V guest or WSL 2 instance.
  This tool is part of the usbip project.

  Connect USB devices across machine boundaries.
  - Share USB devices on a Windows machine to any machine on the network.
  - Access shared USB devices from WSL 2 or Hyper-V guests.
  - Built for WSL 2 but also works with Hyper-V virtual machines.

  For more information and usage instructions, visit https://github.com/crisnsz/usbipd-win
Moniker: usbipd
Tags:
  - usb
  - usbip
  - wsl
  - hyperv
  - usb-sharing
ReleaseNotes: |
  See https://github.com/crisnsz/usbipd-win/releases/tag/v$manifestVersion
ReleaseNotesUrl: https://github.com/crisnsz/usbipd-win/releases/tag/v$manifestVersion
PurchaseUrl: https://github.com/crisnsz/usbipd-win
Documentations:
  - DocumentLabel: GitHub Wiki
    DocumentUrl: https://github.com/crisnsz/usbipd-win/wiki
ManifestType: locale
ManifestVersion: 1.6.0
"@

# === Metadatos de la versión (Default.yaml) ===
$defaultYaml = @"
# Created by: Create-WingetManifest.ps1
# yaml-language-server: `$schema=https://raw.githubusercontent.com/microsoft/winget-cli/master/schemas/json/manifests/v1.6.0/manifest.default.1.6.0.schema.json

PackageIdentifier: crisnsz.usbipd-win
PackageVersion: $manifestVersion
ManifestType: default
ManifestVersion: 1.6.0
"@

# Guardar archivos
Write-Host "Writing manifest files..." -ForegroundColor Cyan

$installerFile = "$manifestDir\crisnsz.usbipd-win.installer.yaml"
$localeFile = "$manifestDir\crisnsz.usbipd-win.locale.en-US.yaml"
$defaultFile = "$manifestDir\crisnsz.usbipd-win.yaml"

$installerYaml | Out-File -FilePath $installerFile -Encoding UTF8 -NoNewline
$localeYaml | Out-File -FilePath $localeFile -Encoding UTF8 -NoNewline
$defaultYaml | Out-File -FilePath $defaultFile -Encoding UTF8 -NoNewline

Write-Host "✓ Manifest files created:" -ForegroundColor Green
Write-Host "  $installerFile" -ForegroundColor Green
Write-Host "  $localeFile" -ForegroundColor Green
Write-Host "  $defaultFile" -ForegroundColor Green

Write-Host "`nManifest structure:" -ForegroundColor Cyan
Write-Host "  crisnsz.usbipd-win/" -ForegroundColor Gray
Write-Host "    $manifestVersion/" -ForegroundColor Gray
Write-Host "      crisnsz.usbipd-win.installer.yaml" -ForegroundColor White
Write-Host "      crisnsz.usbipd-win.locale.en-US.yaml" -ForegroundColor White
Write-Host "      crisnsz.usbipd-win.yaml" -ForegroundColor White

Write-Host "`nTo submit to winget, create a pull request at:" -ForegroundColor Cyan
Write-Host "  https://github.com/microsoft/winget-pkgs" -ForegroundColor Cyan

Write-Host "`nFolder to upload:" -ForegroundColor Yellow
Write-Host "  $manifestDir" -ForegroundColor Yellow
