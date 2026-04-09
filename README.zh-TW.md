<p align="center">
  <img src="./assets/Aion2Flow.png" alt="Aion2Flow" width="256">
</p>

<p align="center">
  <a href="./README.md">English</a>
</p>

---

**Aion2Flow** 是一款專為 **AION 2 (TW)** 設計的即時戰鬥分析工具。

---

## 功能特色

* **即時 DPS / HPS 統計**

  * 每秒傷害（DPS）
  * 每秒治療（HPS）
  * 總輸出與有效時間

* **多維度分析視角**

  * **造成（傷害 / 支援）**
  * **承受（傷害 / 支援）**
  * 可自由切換不同視角進行分析

* **技能細節分析**

  * 各技能傷害 / 治療占比
  * 命中次數、暴擊率與分佈

* **玩家與召喚物歸屬**

  * 自動將召喚物傷害歸屬至玩家

* **歷史戰鬥記錄**

  * 回顧過往戰鬥
  * 長期表現分析

---

## 無外部依賴

* ✅ 無需安裝 Npcap / WinPcap
* ✅ 開箱即用

---

## 安全與非侵入式設計

* ❌ 不修改遊戲檔案
* ❌ 不注入遊戲進程
* ❌ 不讀取遊戲記憶體

> 僅基於本地網路資料進行分析

---

## 使用方式

1. 啟動遊戲
2. 啟動 **Aion2Flow**
3. 工具會自動開始擷取戰鬥資料
4. 在 UI 中查看即時統計

---

## 注意事項

* 本工具僅供**個人數據分析用途**
* 請依據所在地區與遊戲規範自行判斷使用風險
* 本專案不對任何帳號相關問題負責

---

## 技術說明

* **.NET 10**
* UI 框架：**Avalonia**
* 封包擷取：**WinDivert**

---

## 編譯

```bash
dotnet build -c Release
```

---

## 執行

```bash
dotnet run --project Aion2Flow
```

---

## 測試

```bash
dotnet test
```

---

## AOT 發佈（可選）

```bash
dotnet publish -c Release -r win-x64 -p:PublishAot=true
```

輸出目錄：

```
bin/Release/net10.0/win-x64/publish/
```

---

## 授權

本專案採用 **GPL-3.0** 授權。

你可以自由使用、修改與散佈，但需遵守：

* 修改版本需同樣開源
* 必須保留原作者資訊

---