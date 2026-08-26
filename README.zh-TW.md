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

**Aion2Flow** 是一款 **AION 2** 即時戰鬥分析工具。

## Overlay 預覽

https://github.com/user-attachments/assets/955dc75a-6dcc-487f-9081-ed9434895b36

## 功能特色

- 即時顯示 DPS 與總傷害排名，搭配動態貢獻比例條。
- 統計範圍可切換為自己、組隊、部隊或戰鬥中的所有玩家。
- 提供標準與僅 Boss 戰兩種場景模式；資料足夠時會顯示 Boss 血量與玩家傷害占比。
- 角色明細包含輸出/承受方向、目標/來源篩選，以及各技能的傷害、治療、屏障與屏障吸收統計。
- 本次執行期間保留最近 10 場有效戰鬥，可檢視歷史結果並開啟實驗性時間軸回放。
- Overlay 直接顯示擷取狀態與遊戲來回延遲。
- 支援互動、點擊穿透與隱藏三種顯示模式，並可設定顯示模式與戰鬥重置的全域快捷鍵。
- 可調整 UI 縮放、置頂行為、顯示列數、排序方式、數值欄位、精簡數字與玩家名稱顯示。
- 介面與遊戲資料顯示支援繁體中文、English 與 한국어。
- 由 Velopack 管理的發行版本會自動檢查並下載更新。

## 系統需求

- Windows x64。
- 當前版本的 AION 2 客戶端。
- 啟動 Aion2Flow 時必須允許系統管理員權限，供內附的 WinDivert 擷取驅動使用。

預編譯版本不需要另外安裝 .NET，也不需要 Npcap 或 WinPcap。

## 下載

[**直接下載最新便攜版 ZIP**](https://github.com/cloris-chan/Aion2Flow/releases/latest/download/Aion2Flow-stable-Portable.zip)

其他發行檔案與版本記錄可在 [GitHub Releases](https://github.com/cloris-chan/Aion2Flow/releases) 查看。

## 開始使用

1. 下載最新版本；若下載的是壓縮檔，請先解壓縮。
2. 啟動 `Aion2Flow.exe`，並接受 Windows 的系統管理員權限提示。
3. 啟動 AION 2。Aion2Flow 會等待 `Aion2.exe`、偵測目前的遊戲連線，並在收到戰鬥資料後開始更新。
4. 若沒有顯示資料，請查看 Overlay 底部的三個狀態指示：擷取驅動、遊戲連接埠與戰鬥連線。

## Overlay 操作

- 拖曳標題區域可移動 Overlay。
- 使用重置按鈕或已設定的全域快捷鍵，結束目前戰鬥並開始新的統計。
- 選取角色可查看輸出/承受明細與技能統計。
- 開啟「設定」可調整場景模式、統計範圍、排序、顯示數值、玩家名稱、UI 縮放、置頂行為與快捷鍵。
- 使用圖釘控制或顯示模式快捷鍵，可在互動、點擊穿透與隱藏模式間循環；Overlay 隱藏時仍可透過圖釘恢復。
- 開啟「歷史」可檢視已保存的戰鬥，點擊播放按鈕可開啟實驗性時間軸回放。

戰鬥歷史只保存在記憶體中，Aion2Flow 結束時會清除。

## 安全性與資料

Aion2Flow 只在本機分析遊戲網路流量。

- 不修改遊戲檔案。
- 不注入遊戲進程。
- 不讀取遊戲記憶體。
- 更新檢查會連線到本專案的 GitHub Releases。

## 從原始碼建立

從原始碼編譯、測試或發佈需要 .NET 10 SDK。

```powershell
dotnet build Aion2Flow.slnx -c Release
dotnet test --solution Aion2Flow.slnx -c Release --max-parallel-test-modules 1
dotnet publish src/Aion2Flow/Aion2Flow.csproj -c Release
```

桌面程式會以 Native AOT Windows x64 形式發佈，預設輸出目錄為：

```text
src/Aion2Flow/bin/Release/net10.0-windows/win-x64/publish/
```

## 目前限制

- 實驗性回放與戰鬥歷史只適用於本次程式執行期間擷取的戰鬥。
- 若在遊戲啟動後才啟動 Aion2Flow，可能因錯過初始資料而暫時無法顯示部分名稱、圖示、Boss 血量或場景名稱；返回角色選擇畫面並重新進入遊戲，或進行任意一次傳送，即可重新取得資料。若內附顯示資料尚未隨遊戲更新，相關內容仍可能缺失。
- 遊戲更新可能改變戰鬥資料並暫時影響統計；大型客戶端更新後請安裝最新的 Aion2Flow 版本。

本專案與 NCSOFT 或任何 AION 2 發行商沒有從屬、背書或贊助關係。

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
