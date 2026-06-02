<!--
SPDX-FileCopyrightText: 2020 Frans van Dorsselaer
SPDX-FileCopyrightText: 2026 crisnsz (fork with service bridge)

SPDX-License-Identifier: GPL-3.0-only
-->

# usbipd-win (crisnsz fork)

[![Build](https://github.com/crisnsz/usbipd-win/actions/workflows/build-installer.yml/badge.svg?branch=master)](https://github.com/crisnsz/usbipd-win/actions/workflows/build-installer.yml?query=branch%3Amaster)
[![Release](https://github.com/crisnsz/usbipd-win/actions/workflows/release.yml/badge.svg)](https://github.com/crisnsz/usbipd-win/actions/workflows/release.yml)
[![CodeQL](https://github.com/crisnsz/usbipd-win/actions/workflows/codeql-analysis.yml/badge.svg?branch=master)](https://github.com/crisnsz/usbipd-win/actions/workflows/codeql-analysis.yml?query=workflow%3ACodeQL+branch%3Amaster)
[![GitHub all releases](https://img.shields.io/github/downloads/crisnsz/usbipd-win/total?logo=github)](https://github.com/crisnsz/usbipd-win/releases)

Windows software for sharing locally connected USB devices to other machines, including Hyper-V guests and WSL 2.

## About This Fork

This is a fork of the original [dorssel/usbipd-win](https://github.com/dorssel/usbipd-win) project with enhancements:
- **Windows Service Bridge**: Improved permission delegation via service that runs with elevated rights
- **Group-based Access**: Users in `usbipd-users` group can execute commands without requiring admin privileges (similar to `docker-users` group)
- **Automated CI/CD**: Release automation via GitHub Actions with automatic winget package updates
- **v5.4.0+**: Full backward compatibility with the original project while adding new features

Install this fork: `winget install crisnsz.usbipd-win`

## How to install

This software requires
Microsoft Windows 10 (x64 or ARM64) / Microsoft Windows Server 2019, version 1809 or newer;
it does not depend on any other software.

Run the installer (.msi) from the [latest release](https://github.com/crisnsz/usbipd-win/releases/latest)
on the Windows machine where your USB device is connected.

Alternatively, use the Windows Package Manager:

```powershell
winget install crisnsz.usbipd-win
```

This will install:

- A service called `usbipd` (display name: USBIP Device Host).\
  You can check the status of this service using the Services app from Windows.
- A command line tool `usbipd`.\
  The location of this tool will be added to the `PATH` environment variable.
- A firewall rule called `usbipd` to allow all local subnets to connect to the service.\
  You can modify this firewall rule to fine tune access control.

> [!NOTE]
> If you are using a third-party firewall, you may have to reconfigure it to allow
> incoming connections on TCP port 3240.

## How to use

> [!TIP]
> See the [wiki](https://github.com/crisnsz/usbipd-win/wiki/Enable-Tab-Completion) on how to enable tab completion
for PowerShell.

### Share Devices

By default devices are not shared with USBIP clients.
To lookup and share devices, run the following commands with administrator privileges:

```powershell
usbipd --help
usbipd list
usbipd bind --busid=<BUSID>
```

Sharing a device is persistent; it survives reboots.

> [!TIP]
> See the [wiki](https://github.com/crisnsz/usbipd-win/wiki/Tested-Devices) for a list of tested devices.

### Connecting Devices

Attaching devices to a client is non-persistent. You will have to re-attach after a reboot,
or when the device resets or is physically unplugged/replugged.

#### Non-WSL 2

From another (possibly virtual) machine running Linux, use the `usbip` client-side tool:

```bash
usbip list --remote=<HOST>
sudo usbip attach --remote=<HOST> --busid=<BUSID>
```

> [!NOTE]
> Client-side tooling exists for other operating systems such as Microsoft Windows, but not as part of this project.

#### WSL 2

You can attach the device from within Windows with the following command, which does not require administrator privileges:

```powershell
usbipd attach --wsl --busid=<BUSID>
```

> [!TIP]
> Update WSL 2 with `wsl --update` to get the latest kernel, which supports most USB devices.
> See the [wiki](https://github.com/crisnsz/usbipd-win/wiki/WSL-support) on how to add drivers
> for USB devices that are not supported by the default WSL 2 kernel.

### GUI

See the [wiki](https://github.com/crisnsz/usbipd-win/wiki/Graphical-User-Interfaces)
for a list of GUI and IDE integration tools in case you prefer that over a CLI.

## How to remove

Uninstall via Add/Remove Programs or via Settings/Apps.

Alternatively, use the Windows Package Manager:

```powershell
winget uninstall crisnsz.usbipd-win
```

## Development & Releases

### Automated Release Process

This project uses **fully automated CI/CD** with GitHub Actions:

1. **Merge PR to master** → GitHub Actions compiles and tests automatically
2. **Create git tag** → `git tag v5.4.0 && git push origin v5.4.0`
3. **GitHub Actions releases automatically:**
   - Builds MSI installers (x64 + ARM64)
   - Creates GitHub Release with checksums
   - Triggers automatic winget package update

See [docs/RELEASE.md](docs/RELEASE.md) for detailed release documentation.

### Building Locally (Development)

```powershell
# Prerequisites: .NET SDK 10.0.300+, WiX Toolset 7.0+

# Build
dotnet build --configuration Release

# Test
dotnet test --configuration Release

# Create installer
dotnet build Installer --configuration Release

# Output: Installer/bin/x64/release/*.msi
```

### Contributing

1. Fork this repository
2. Create a feature branch: `git checkout -b feature/your-feature`
3. Commit with signed commits: `git commit -S -m "description"`
4. Create a Pull Request to `master`
5. After merge, releases are automated via git tags

See [docs/RELEASE.md](docs/RELEASE.md) for release procedures.

## License

This software is licensed under the GNU General Public License v3.0 (GPL-3.0-only).
See [COPYING.md](COPYING.md) for details.

Original project by [Frans van Dorsselaer](https://github.com/dorssel/usbipd-win).
Fork maintained by [crisnsz](https://github.com/crisnsz/usbipd-win) with service bridge enhancements.
