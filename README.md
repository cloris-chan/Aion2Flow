<p align="center">
  <img src="./assets/Aion2Flow.png" alt="Aion2Flow" width="256">
</p>

<p align="center">
  <a href="./README.zh-TW.md">繁體中文</a>
</p>

<p align="center">
  <a href="https://github.com/cloris-chan/Aion2Flow/releases">
    <img alt="Release" src="https://img.shields.io/github/v/release/cloris-chan/Aion2Flow?display_name=release&style=flat-square">
  </a>
  <a href="./LICENSE.txt">
    <img alt="License: GPL-3.0" src="https://img.shields.io/badge/License-GPLv3-blue.svg?style=flat-square">
  </a>
</p>

**Aion2Flow** is a real-time combat analysis tool for **AION 2 (TW)**.

## Highlights

- Live combat list with battle timer, DPS, total damage, and contribution share
- Per-combatant detail flyout for outgoing and incoming damage, healing, and shield
- Per-skill breakdown with crits, hit rate, multi-hit, back/parry/block, evade, invincible, and more
- Auto-archived recent battles with quick switching between history and live view
- UI localization for Traditional Chinese, English, and Korean

## Safety

- No game file modification
- No process injection
- No memory reading
- No separate Npcap / WinPcap setup required
- Traffic analysis stays on the local machine

## Download

Prebuilt builds: [GitHub Releases](https://github.com/cloris-chan/Aion2Flow/releases)

## Requirements

- Windows x64
- Administrator privileges to start
- .NET 10 SDK if building from source

## Build

```bash
dotnet build -c Release
```

## Run

```bash
dotnet run --project src/Aion2Flow
```

## Test

```bash
dotnet test
```

## AOT Publish

```bash
dotnet publish src/Aion2Flow -c Release -r win-x64 -p:PublishAot=true
```

Output:

```text
src/Aion2Flow/bin/Release/net10.0-windows/win-x64/publish/
```
