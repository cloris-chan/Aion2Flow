<p align="center">
  <img src="./assets/Aion2Flow.png" alt="Aion2Flow" width="256">
</p>

<p align="center">
  <a href="./README.zh-TW.md">繁體中文</a>
</p>

---

**Aion2Flow** is a real-time combat analysis tool for **AION 2 (TW)**.

---

## Features

* **Real-time DPS & HPS**

  * Damage per second (DPS)
  * Healing per second (HPS)
  * Total output and effective time

* **Multiple Analysis Perspectives**

  * **Dealt** (Damage / Support)
  * **Taken** (Damage / Support)
  * Easily switch between perspectives for deeper insights

* **Skill Breakdown**

  * Per-skill damage & healing contribution
  * Hit count, critical rate, and distribution

* **Player & Summon Attribution**

  * Automatically attributes summon damage to the owner

* **Combat History**

  * Review previous fights
  * Analyze performance over time

---

## No External Dependencies

* ✅ No Npcap / WinPcap required
* ✅ Works out of the box

---

## Safe & Non-Intrusive

* ❌ Does **not** modify game files
* ❌ Does **not** inject into the game process
* ❌ Does **not** access game memory

> Aion2Flow only analyzes network traffic locally.

---

## Usage

1. Launch the game
2. Start **Aion2Flow**
3. The tool will automatically begin capturing combat data
4. View real-time statistics in the UI

---

## Disclaimer

* This tool is intended for **personal analysis only**
* Use at your own risk according to your region and game policies
* The author is not responsible for any account-related issues

---

## Technical Overview

* **.NET 10**
* UI: **Avalonia**
* Packet capture: **WinDivert**

---

## Build

```bash
dotnet build -c Release
```

---

## Run

```bash
dotnet run --project Aion2Flow
```

---

## Test

```bash
dotnet test
```

---

## AOT Publish (Optional)

```bash
dotnet publish -c Release -r win-x64 -p:PublishAot=true
```

Output directory:

```
bin/Release/net10.0/win-x64/publish/
```

---

## License

Licensed under **GPL-3.0**.

You are free to use, modify, and distribute this project under GPL terms:

* Modified versions must also be open-source
* Proper attribution is required

---