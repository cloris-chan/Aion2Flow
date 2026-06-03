<p align="center">
  <img src="./assets/Aion2Flow.png" alt="Aion2Flow" width="256">
</p>

<p align="center">
  <a href="./README.md">English</a>
</p>

<p align="center">
  <a href="https://github.com/cloris-chan/Aion2Flow/releases">
    <img alt="Release" src="https://img.shields.io/github/v/release/cloris-chan/Aion2Flow?display_name=release">
  </a>
  <a href="./LICENSE.txt">
    <img alt="License: GPL-3.0" src="https://img.shields.io/badge/License-GPLv3-blue.svg">
  </a>
</p>

**Aion2Flow** 是一款專為 **AION 2 (TW)** 設計的即時戰鬥分析工具。

## 特色

- 即時戰鬥列表，使用穩定寬度數字欄與動畫貢獻條。
- Boss 焦點血條；在能辨識 Boss 血量與貢獻者時，會顯示傷害占比區段。
- 角色細節面板，包含方向範圍、對象篩選與技能列。
- 技能明細支援傷害、治療、屏障、吸收屏障等範圍。
- 設定可調整語言、置頂模式、顯示列數、角色排序依據，以及全域戰鬥重置熱鍵。
- Velopack 管理的版本可在設定中檢查更新。

## 安全性

Aion2Flow 的定位是本機封包分析器。

- 不修改遊戲檔案。
- 不注入遊戲進程。
- 不讀取遊戲記憶體。
- 不需要額外安裝 Npcap 或 WinPcap。
- 啟動時需要系統管理員權限，讓 WinDivert 開啟擷取 driver。

## 需求

- Windows x64。
- 啟動程式時需要系統管理員權限。
- 若要從原始碼編譯或測試，需要 .NET 10 SDK。

## 下載

預編譯版本可在 [GitHub Releases](https://github.com/cloris-chan/Aion2Flow/releases) 下載。

## 編譯

```bash
dotnet build -c Release
```

## 執行

```bash
dotnet run --project src/Aion2Flow
```

如果擷取啟動失敗，請用系統管理員權限執行。

## 測試

```bash
dotnet test
```

測試包含 protocol parser、場景統計聚合、UI view model，以及以 stream log fixture 驅動的 replay 測試。

## 發佈

桌面 app 已啟用 Native AOT 發佈：

```bash
dotnet publish src/Aion2Flow -c Release
```

輸出目錄：

```text
src/Aion2Flow/bin/Release/net10.0-windows/win-x64/publish/
```

## 專案結構

```text
src/Aion2Flow              Avalonia 桌面 app、ViewModel、設定與更新流程
src/Aion2Flow.Capture      WinDivert 擷取流程、TCP stream 處理、replay log
src/Aion2Flow.Protocol     封包解析器與 protocol-level 結構
src/Aion2Flow.SceneRuntime 場景模型、戰鬥聚合、身分推斷、歸檔快照
src/Aion2Flow.Resources    內嵌資源資料庫，提供技能、NPC、地圖資料
src/Aion2Flow.WinDivert    WinDivert P/Invoke 包裝與 native 檔案
src/Aion2Flow.Tests        Parser、replay、場景 runtime 與 UI 測試
```

## 在地化

介面支援：

- 繁體中文
- English
- 한국어

遊戲資源顯示資料來自內嵌資源資料庫，會在資料可用時跟隨介面語言。

## 注意事項

- Parser 目前針對 AION 2 台服客戶端 protocol 調整；遊戲更新封包格式時可能需要同步更新。
- 封包中的 resource reference 只視為顯示或除錯資訊，不作為穩定解析邏輯。
- 本專案與 NCSOFT 或任何 AION 2 發行商沒有從屬、背書或贊助關係。

## 贊助

如果 Aion2Flow 對你有幫助，可以透過 Ko-fi 或微信讚賞支持開發。

<p>
  <a href="https://ko-fi.com/cloris">
    <img alt="Support me on Ko-fi" src="https://ko-fi.com/img/githubbutton_sm.svg">
  </a>
</p>

<p>
  <img alt="微信讚賞碼" src="https://raw.githubusercontent.com/cloris-chan/.github/main/assets/sponsors/wechat-reward.png" width="180">
</p>
