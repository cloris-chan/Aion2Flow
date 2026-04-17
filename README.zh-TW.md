<p align="center">
  <img src="./assets/Aion2Flow.png" alt="Aion2Flow" width="256">
</p>

<p align="center">
  <a href="./README.md">English</a>
</p>

<p align="center">
  <a href="https://github.com/cloris-chan/Aion2Flow/releases">
    <img alt="Release" src="https://img.shields.io/github/v/release/cloris-chan/Aion2Flow?display_name=release&style=flat-square">
  </a>
  <a href="./LICENSE.txt">
    <img alt="License: GPL-3.0" src="https://img.shields.io/badge/License-GPLv3-blue.svg?style=flat-square">
  </a>
</p>

**Aion2Flow** 是一款專為 **AION 2 (TW)** 設計的即時戰鬥分析工具。

## 特色

- 即時戰鬥列表，顯示戰鬥時間、DPS、總傷害與貢獻占比
- 角色細節面板，可查看造成與承受的傷害、治療與屏障
- 技能明細統計，包含暴擊、命中率、多段打擊、背後、格擋、迴避、無敵等資訊
- 自動歸檔最近戰鬥，可快速在歷史與即時視圖間切換
- 介面支援繁體中文、English、한국어

## 安全性

- 不修改遊戲檔案
- 不注入遊戲進程
- 不讀取遊戲記憶體
- 不需額外安裝 Npcap / WinPcap
- 僅分析本機流量

## 下載

預編譯版本：[GitHub Releases](https://github.com/cloris-chan/Aion2Flow/releases)

## 需求

- Windows x64
- 啟動需要系統管理員權限
- 若從原始碼建置，需要 .NET 10 SDK

## 編譯

```bash
dotnet build -c Release
```

## 執行

```bash
dotnet run --project src/Aion2Flow
```

## 測試

```bash
dotnet test
```

## AOT 發佈

```bash
dotnet publish src/Aion2Flow -c Release -r win-x64 -p:PublishAot=true
```

輸出目錄：

```text
src/Aion2Flow/bin/Release/net10.0-windows/win-x64/publish/
```
