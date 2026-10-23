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

This is a fork of [dorssel/usbipd-win](https://github.com/dorssel/usbipd-win) that lets non-admin users share USB devices.

### The problem

In upstream, `usbipd bind` / `unbind` need an elevated terminal. On a machine where you are not a local administrator, such as a managed work laptop, every bind needs an admin prompt. Docker Desktop has the same kind of problem and solves it with a privileged service plus a `docker-users` group. VMware lets a VM take a USB device without admin rights at runtime. This fork applies the same model to usbipd.

The request was discussed upstream in [dorssel/usbipd-win#1315](https://github.com/dorssel/usbipd-win/issues/1315). The maintainer added a simple group check on a separate branch, but declined a service/IPC redesign because a dedicated driver is planned. This fork implements that redesign.

My use case: opening untrusted cloned repositories inside a WSL distro, instead of on the Windows host or in a VM, while still passing USB devices through.

### What it changes

- **Privilege separation over a named pipe:** the elevated `usbipd` service does the privileged work. The CLI sends requests to it over the `usbipd-bind` named pipe instead of needing elevation itself.
- **Group-based authorization:** the service checks the caller's Windows token. Only members of `Administrators` or the local `usbipd-users` group are accepted; anyone else is rejected and logged.
- **Small, fixed command set:** the pipe accepts only `Bind`, `Unbind` and `UnbindAll`. Any other command is rejected.
- **Pipe hardening:** maximum string length (20 KiB), request read timeout (50 s) and a cap on concurrent pipe instances. These limit malformed or slow clients.
- **Stability fix:** devices disconnected under high USB traffic. Testing versions one by one traced it to the device I/O code, which was reverted to the v5.3.0 implementation (`Usbipd/DeviceFile.cs`).
- **CI/CD:** release automation (MSI for x64 and ARM64, checksums), SHA-pinned actions, and [zizmor](https://github.com/zizmorcore/zizmor) workflow scanning.

### Setup

The installer creates the `usbipd-users` group **empty**. An administrator adds users once:

```powershell
# elevated, once per user
Add-LocalGroupMember -Group usbipd-users -Member "$env:USERDOMAIN\$env:USERNAME"
```

The user then signs out and back in, so the new group membership is part of their token. From then on, `usbipd bind` (including `--force`) and `usbipd unbind` work from a normal terminal.

### Security notes

This is an extra privileged endpoint on the machine, so it adds attack surface. WSL in particular is not a sandbox. For that reason I did not propose it for use at work without a deeper review. Known gaps:

- `bind --force` goes through the service too, so a `usbipd-users` member can make the service force-install the bundled VirtualBox USB stub driver on a device. Upstream deliberately keeps this admin-only. The service only ever installs that one bundled driver, never an arbitrary one, but the risk should still be weighed.
- The server's handling of busid/instance-ID input has not been reviewed adversarially, and there is no audit trail beyond the service log.

Install this fork: download the `.msi` from the [latest release](https://github.com/crisnsz/usbipd-win/releases/latest).

## How to install

This software requires
Microsoft Windows 10 (x64 or ARM64) / Microsoft Windows Server 2019, version 1809 or newer;
it does not depend on any other software.

Run the installer (.msi) from the [latest release](https://github.com/crisnsz/usbipd-win/releases/latest)
on the Windows machine where your USB device is connected.

> [!NOTE]
> This fork is not published to the Windows Package Manager (winget) yet — the manifest tooling exists
> (`scripts/Create-WingetManifest.ps1`), but the package has not been submitted to
> [microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs). `winget install usbipd` installs the **upstream** project, not this fork.

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
To lookup and share devices, run the following commands with administrator privileges, or as a member of `usbipd-users` (see [Setup](#setup)):

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


## Development & Releases

### Automated Release Process

This project uses **fully automated CI/CD** with GitHub Actions:

1. **Merge PR to master** → GitHub Actions compiles and tests automatically
2. **Create git tag** → `git tag v5.4.0 && git push origin v5.4.0`
3. **GitHub Actions releases automatically:**
   - Builds MSI installers (x64 + ARM64)
   - Creates GitHub Release with checksums

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
