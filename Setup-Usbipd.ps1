# SPDX-FileCopyrightText: 2026 crisnsz
#
# SPDX-License-Identifier: GPL-3.0-only

param(
    [switch]$Build
)

$ErrorActionPreference = "Stop"

$projectRoot = "D:\projects\usbipd-win"
$publishPath = "$projectRoot\Usbipd\bin\publish\x64"
$installerOutput = "$projectRoot\Installer\bin\x64\Release"

if ($Build) {
    Write-Host "Cleaning old build artifacts..." -ForegroundColor Cyan

    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $publishPath
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $installerOutput

    Write-Host "Publishing usbipd..." -ForegroundColor Cyan

    dotnet publish "$projectRoot\Usbipd\Usbipd.csproj" `
        --configuration Debug `
        --runtime win-x64 `
        --self-contained true `
        -p:Platform=x64 `
        --output $publishPath

    if (-not (Test-Path $publishPath)) {
        throw "Publish failed: output folder not found -> $publishPath"
    }

    Write-Host "Building installer..." -ForegroundColor Cyan

    dotnet build "$projectRoot\Installer\Installer.wixproj" `
        --configuration Release `
        --no-restore `
        -p:Platform=x64
}
else {
    Write-Host "Build step skipped (use -Build to build before install)." -ForegroundColor DarkYellow
}

Write-Host "Locating MSI..." -ForegroundColor Cyan

$msiPath = Get-ChildItem "$installerOutput\*.msi" -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if (-not $msiPath -or -not (Test-Path $msiPath.FullName)) {
    throw "MSI was not generated correctly in: $installerOutput"
}

Write-Host "Using MSI: $($msiPath.FullName)" -ForegroundColor Green

Write-Host "Checking existing installation..." -ForegroundColor Yellow

$uninstallKeys = @(
    "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*",
    "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*"
)

$app = Get-ItemProperty $uninstallKeys -ErrorAction SilentlyContinue |
    Where-Object { $_.DisplayName -like "usbipd-win*" } |
    Select-Object -First 1

if ($app) {
    Write-Host "Found installed version: $($app.DisplayName)" -ForegroundColor Cyan

    $productCode = $null

    if ($app.PSChildName -match "^\{.*\}$") {
        $productCode = $app.PSChildName
    }
    elseif ($app.UninstallString -match "\{.*\}") {
        $productCode = ($app.UninstallString | Select-String -Pattern "\{.*\}" -AllMatches).Matches.Value
    }

    if ($productCode) {
        Write-Host "Uninstalling MSI product: $productCode" -ForegroundColor Cyan
        Start-Process "msiexec.exe" -ArgumentList "/x $productCode /norestart" -Wait
    }
    else {
        Write-Host "Falling back to uninstall string..." -ForegroundColor Yellow
        Start-Process "cmd.exe" -ArgumentList "/c $($app.UninstallString)" -Wait
    }

    Write-Host "Previous version removed." -ForegroundColor Green
}
else {
    Write-Host "No previous version found." -ForegroundColor DarkYellow
}

Write-Host "Installing new version..." -ForegroundColor Cyan

Start-Process "msiexec.exe" -ArgumentList "/i `"$($msiPath.FullName)`" /norestart" -Wait

Write-Host "Installation completed." -ForegroundColor Green

$appAfter = Get-ItemProperty $uninstallKeys -ErrorAction SilentlyContinue |
    Where-Object { $_.DisplayName -like "usbipd-win*" } |
    Select-Object -First 1

if (-not $appAfter) {
    Write-Host "Warning: installation may not have registered correctly in registry." -ForegroundColor Yellow
}
else {
    Write-Host "Installed version: $($appAfter.DisplayName)" -ForegroundColor Green
}

Write-Host "Done." -ForegroundColor Green
