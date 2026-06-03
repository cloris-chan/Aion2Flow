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

**Aion2Flow** is a real-time combat analysis tool for **AION 2 (TW)**.

## Highlights

- Live combat list with stable numeric columns and animated contribution bars.
- Boss focus bars with visible damage-share segments when boss HP and contributors are known.
- Combatant detail flyout with direction scopes, counterpart filters, and per-skill rows.
- Skill detail scopes for damage, healing, shield, and absorbed shield views.
- Settings for language, always-on-top behavior, visible row count, combatant sort metric, and a global battle reset hotkey.
- Update checks for Velopack-managed builds.

## Safety

Aion2Flow is designed as a local packet analyzer.

- No game file modification.
- No game process injection.
- No game memory reading.
- No Npcap or WinPcap installation required.
- Administrator privileges are required so WinDivert can open the capture driver.

## Requirements

- Windows x64.
- Administrator privileges when starting the app.
- .NET 10 SDK for building or testing from source.

## Download

Prebuilt releases are available on [GitHub Releases](https://github.com/cloris-chan/Aion2Flow/releases).

## Build

```bash
dotnet build -c Release
```

## Run

```bash
dotnet run --project src/Aion2Flow
```

Run the process as administrator if capture startup fails.

## Test

```bash
dotnet test
```

The tests include protocol parser checks, scene-runtime aggregation checks, UI view-model checks, and replay fixtures based on captured stream logs.

## Publish

Native AOT publishing is enabled for the desktop app:

```bash
dotnet publish src/Aion2Flow -c Release
```

Output:

```text
src/Aion2Flow/bin/Release/net10.0-windows/win-x64/publish/
```

## Project Layout

```text
src/Aion2Flow              Avalonia desktop app, view models, settings, update flow
src/Aion2Flow.Capture      WinDivert capture pipeline, TCP stream handling, replay logs
src/Aion2Flow.Protocol     Packet parsers and protocol-level structures
src/Aion2Flow.SceneRuntime Scene model, combat aggregation, identity, archive snapshots
src/Aion2Flow.Resources    Embedded resource database access for skills, NPCs, maps
src/Aion2Flow.WinDivert    WinDivert P/Invoke wrapper and native files
src/Aion2Flow.Tests        Parser, replay, scene-runtime, and UI tests
```

## Localization

The UI supports:

- Traditional Chinese
- English
- Korean

Game resource display data is loaded from the embedded resource database and follows the selected UI language where data is available.

## Notes

- The parser is tuned for the current AION 2 Taiwan client protocol and can require updates when the game changes packet layouts.
- Resource references from packets are treated as display/debug context, not as stable parser logic.
- This project is not affiliated with, endorsed by, or sponsored by NCSOFT or any AION 2 publisher.

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
