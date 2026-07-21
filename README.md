<p align="center">
  <img src="./assets/Aion2Flow.png" alt="Aion2Flow" width="256">
</p>

<p align="center">
  <a href="./README.zh-TW.md">繁體中文</a>
</p>

<p align="center">
  <a href="https://github.com/cloris-chan/Aion2Flow/releases">
    <img alt="Release" src="https://img.shields.io/github/v/release/cloris-chan/Aion2Flow?display_name=release">
  </a>
  <a href="./LICENSE.txt">
    <img alt="License: GPL-3.0" src="https://img.shields.io/badge/License-GPLv3-blue.svg">
  </a>
</p>

**Aion2Flow** is a real-time combat analysis tool for **AION 2**.

## Overlay Preview

https://github.com/user-attachments/assets/955dc75a-6dcc-487f-9081-ed9434895b36

## Features

- Live DPS and total-damage ranking with animated contribution bars.
- Configurable player scope: self, party, force, or everyone in the encounter.
- Standard and boss-only scene modes, with boss HP and damage-share displays when the required data is available.
- Combatant details with outgoing/incoming views, target/source filters, and per-skill damage, healing, shield, and shield-absorption breakdowns.
- In-session history for the latest 10 valid encounters, with archived result browsing and experimental timeline playback.
- Capture status and game round-trip latency shown directly on the overlay.
- Interactive, click-through, and hidden overlay modes, plus configurable global shortcuts for display mode and battle reset.
- UI scale, topmost behavior, visible row count, sorting, metric visibility, compact numbers, and player-name display settings.
- Traditional Chinese, English, and Korean UI and game-data display.
- Automatic update checks and downloads for Velopack-managed releases.

## Requirements

- Windows x64.
- The current AION 2 client.
- Administrator approval when Aion2Flow starts. This is required for its bundled WinDivert capture driver.

Prebuilt releases do not require a separate .NET installation. Npcap and WinPcap are not required.

## Download

[**Download the latest portable ZIP**](https://github.com/cloris-chan/Aion2Flow/releases/latest/download/Aion2Flow-stable-Portable.zip)

Other release files and version history are available on [GitHub Releases](https://github.com/cloris-chan/Aion2Flow/releases).

## Getting Started

1. Download the latest release and unpack it if necessary.
2. Launch `Aion2Flow.exe` and accept the Windows administrator prompt.
3. Start AION 2. Aion2Flow waits for `Aion2.exe`, detects the active game connection, and begins updating when combat data arrives.
4. Check the three status indicators at the bottom of the overlay if no data appears: capture driver, game port, and battle connection.

## Using the Overlay

- Drag the title area to move the overlay.
- Use the reset button or your configured global shortcut to end the current encounter and start a fresh one.
- Select a combatant to open detailed outgoing/incoming statistics and skill breakdowns.
- Open **Settings** to adjust scene mode, statistics scope, sorting, visible metrics, player names, UI scale, topmost behavior, and shortcuts.
- Use the pin control or display-mode shortcut to cycle between interactive, click-through, and hidden modes. The pin remains available so the overlay can be restored.
- Open **History** to inspect an archived encounter. Use its play button to open the experimental timeline playback window.

Encounter history is kept in memory only and is cleared when Aion2Flow exits.

## Safety and Data

Aion2Flow analyzes game network traffic locally.

- It does not modify game files.
- It does not inject into the game process.
- It does not read game memory.
- Update checks connect to this project's GitHub Releases page.

## Build from Source

Building, testing, or publishing from source requires the .NET 10 SDK.

```powershell
dotnet build Aion2Flow.slnx -c Release
dotnet test Aion2Flow.slnx -c Release -m:1
dotnet publish src/Aion2Flow/Aion2Flow.csproj -c Release
```

The desktop app is published as a Native AOT Windows x64 build. The default publish output is:

```text
src/Aion2Flow/bin/Release/net10.0-windows/win-x64/publish/
```

## Current Limitations

- Experimental playback and encounter history are available only for encounters captured during the current app session.
- If Aion2Flow starts after the game, it may miss initial data and temporarily be unable to show some names, icons, boss HP values, or scene names. Return to character selection and re-enter the game, or teleport once, to refresh that data. Content may still be unavailable when the bundled display data has not yet been updated for the current game version.
- A game update can change combat data and temporarily affect statistics. Install the latest Aion2Flow release after major client updates.

This project is not affiliated with, endorsed by, or sponsored by NCSOFT or any AION 2 publisher.

## Sponsor

If Aion2Flow helps you, you can support development through Ko-fi or WeChat.

<p>
  <a href="https://ko-fi.com/cloris">
    <img alt="Support me on Ko-fi" src="https://ko-fi.com/img/githubbutton_sm.svg">
  </a>
</p>

<p>
  <img alt="WeChat reward QR code" src="https://raw.githubusercontent.com/cloris-chan/.github/main/assets/sponsors/wechat-reward.png" width="180">
</p>
