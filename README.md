# arcdps Updater

A command-line tool that automatically updates [arcdps](https://www.deltaconnected.com/arcdps/) for Guild Wars 2.

## Features

- Auto-detects your Guild Wars 2 installation (registry, Steam, common paths, or running process)
- Shows local and remote arcdps versions
- Downloads the latest `d3d11.dll` with progress display
- Verifies downloads with MD5 checksums
- Backs up the previous version before replacing

## Requirements

- Windows

## Usage

Download the latest `arcdps-updater.exe` from the [Releases](https://github.com/alarian/arcdps-updater/releases) page and run it.

Example output:

```
  arcdps updater
  ==============

  Found GW2: J:\Program Files\Guild Wars 2
  Remote version: 1.2026.205.1033
  Local version:  1.2026.200.1012

  Proceed with update? [Y/n] y

  Downloading: 100% (1,234,567 / 1,234,567 bytes)
  MD5 verified.

  arcdps updated to 1.2026.205.1033.
  Previous version backed up to J:\Program Files\Guild Wars 2\d3d11.dll.backup
```

## How It Works

1. Locates your GW2 install via Windows registry, common paths, or running process
2. Fetches the latest version and MD5 checksum from deltaconnected.com
3. Compares the local `d3d11.dll` hash against the remote hash
4. If an update is available, prompts for confirmation
5. Downloads to a temp file, verifies the MD5, then swaps it in with a backup of the old version

## Building from source

Requires [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```
dotnet build
```
