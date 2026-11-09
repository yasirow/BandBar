<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="docs/assets/logo-dark.svg">
    <img src="docs/assets/logo-light.svg" alt="BandBar" width="360">
  </picture>
</p>

<h3 align="center">Live NetLimiter upload and download speed on your Windows taskbar</h3>

<p align="center">
  A tiny, standalone taskbar speed meter for NetLimiter users on Windows 11 and 10.<br>
  Offline, no administrator rights, no hooks — and out of the way of your games.
</p>

<p align="center">
  <a href="https://github.com/yasirow/BandBar/releases"><img src="https://img.shields.io/github/v/release/yasirow/BandBar?include_prereleases&style=flat-square&label=release" alt="Latest release"></a>
  <a href="https://github.com/yasirow/BandBar/releases"><img src="https://img.shields.io/github/downloads/yasirow/BandBar/total?style=flat-square&label=downloads" alt="Downloads"></a>
  <a href="https://github.com/yasirow/BandBar/actions/workflows/ci.yml"><img src="https://img.shields.io/github/actions/workflow/status/yasirow/BandBar/ci.yml?branch=main&style=flat-square&label=build" alt="Build status"></a>
  <img src="https://img.shields.io/badge/Windows-10%20%C2%B7%2011-0078D4?style=flat-square" alt="Windows 10 and 11">
  <img src="https://img.shields.io/badge/.NET%20Framework-4.8-512BD4?style=flat-square" alt=".NET Framework 4.8">
  <img src="https://img.shields.io/badge/requires-NetLimiter%205-3C8D2F?style=flat-square" alt="Requires NetLimiter 5">
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-blue?style=flat-square" alt="MIT license"></a>
  <img src="https://img.shields.io/badge/status-early%20alpha-D9A21B?style=flat-square" alt="Status: early alpha">
</p>

<p align="center">
  <a href="https://github.com/yasirow/BandBar/releases/latest"><img src="https://img.shields.io/badge/Download%20for%20Windows-2EA44F?style=for-the-badge&logo=windows&logoColor=white" alt="Download for Windows"></a>
</p>

<p align="center">
  <a href="#install">Install</a> ·
  <a href="#update">Update</a> ·
  <a href="CHANGELOG.md">Changelog</a> ·
  <a href="docs/DESIGN.md">How it works</a> ·
  <a href="docs/TESTING.md">Tests</a> ·
  <a href="SECURITY.md">Security</a>
</p>

<p align="center">
  <img src="docs/assets/preview.svg" alt="BandBar on a dark taskbar, next to the notification area: a red up arrow with the upload speed above a green down arrow with the download speed">
</p>

> [!NOTE]
> **Unofficial project.** BandBar is independent and open source. It is not affiliated with, endorsed by or
> supported by Locktime Software, the developer of NetLimiter. NetLimiter is their trademark. You need your own
> licensed copy of NetLimiter 5. When an official NetLimiter taskbar bar is available for your Windows version,
> prefer it.

## Why

NetLimiter's own taskbar bar is a taskbar toolbar (a "deskband"), and Windows 11 removed taskbar toolbars. BandBar
fills that gap: it shows the live upload and download speed using NetLimiter's official public API, as an ordinary,
separate window that never modifies the taskbar or any other program.

## Features

- **Compact two-line display** — red ⬆ upload above green ⬇ download, transparent background,
  B/s → KB/s → MB/s with one decimal. The text turns dark on a light taskbar and back when you switch themes.
- **Follows the taskbar** — sits just before the notification area on a top, bottom, left or right taskbar, and
  moves when the taskbar, the tray icons, the resolution or the scaling change, or when Explorer restarts. Drag it
  anywhere along the taskbar and it remembers the place. On a narrow left or right taskbar it stacks the number
  and the unit; with an auto-hiding taskbar it hides and shows together with it.
- **Gets out of the way of games** — hides completely, and stops reading, while a full-screen or borderless
  windowed application is in front on its monitor, also when the game was already running before BandBar started.
  Optionally it hides for programs you list, and with a show/hide hotkey.
- **Light** — one ~100 KB executable, no runtime to install; about 0.02 % CPU while idle.
- **Safe by design** — no administrator rights, no network, no hooks or injection, no writes outside its own
  folder. NetLimiter's API is loaded from your own NetLimiter installation and its signature is verified first.
- **Resilient** — if the NetLimiter service stops, it shows `—` and reconnects by itself; it never shows an error
  dialog.

## Requirements

- Windows 11, or Windows 10 version 1903 or later — 64-bit (x64)
- A licensed installation of [NetLimiter 5](https://www.netlimiter.com/)

Nothing else: the .NET Framework 4.8 it uses is part of these Windows versions. (Windows 10 version 1809 works too
once .NET Framework 4.8 is installed from Microsoft; the setup checks for it and tells you if it is missing.)

## Install

1. Download the newest **`BandBar-Setup-x.y.z.exe`** from [Releases](https://github.com/yasirow/BandBar/releases/latest).
2. Run it. It installs for your user only and does **not** ask for administrator rights. Tick
   *Start BandBar when I sign in to Windows* if you want it to start automatically.
3. The bar appears on your taskbar, just before the clock and tray icons.

| Download | Use it when |
|---|---|
| `BandBar-Setup-x.y.z.exe` | Recommended. Start menu entry, optional autostart, clean uninstall. |
| `BandBar-x.y.z-portable.zip` | You prefer no installation: unzip anywhere and run `BandBar.exe`. |
| `SHA256SUMS.txt` | To verify your download. |

> [!TIP]
> Releases are not code-signed, so Windows SmartScreen may warn on first run — choose **More info → Run
> anyway**. To check that a file is exactly what the build produced, run
> `Get-FileHash .\BandBar-Setup-x.y.z.exe -Algorithm SHA256` in PowerShell and compare the hash with
> `SHA256SUMS.txt`. Every release is built by [GitHub Actions](https://github.com/yasirow/BandBar/actions) from
> the tagged source code.

## Update

BandBar never checks for updates by itself, because it never connects to the internet.

- **Get notified:** on this page, click **Watch → Custom → Releases**.
- **Update:** download the newer setup and run it. It closes the running bar, upgrades it in place and keeps your
  settings. With the portable ZIP, replace `BandBar.exe`.
- **What changed:** each release lists its changes, and they are all collected in [CHANGELOG.md](CHANGELOG.md).
- **Which version do I have?** Hover the bar; the first line of the tooltip shows it.
- **Updating NetLimiter itself?** Exit BandBar first (right-click → Exit). While it runs it keeps NetLimiter's API
  files open, and the NetLimiter installer would find them in use.

> [!IMPORTANT]
> Version 0.1.3 was published under the project's earlier name. If you installed it, exit it and uninstall it from
> **Windows Settings → Apps** (it is listed under that earlier name) before installing BandBar 0.1.4 or later.

## Uninstall

Open **Windows Settings → Apps → Installed apps → BandBar → Uninstall**. This removes the program, its shortcuts,
its uninstall entry and its own settings and log files — and nothing else. NetLimiter is not touched.
For the portable version, exit the bar and delete its folder.

## Using it

- **Hover** the bar for details: version, zone, connection status and NetLimiter version.
- **Drag** the bar with the left mouse button to move it along the taskbar. It stays where you drop it, also after
  a restart.
- **Right-click** the bar for the menu:
  - *Zone* — choose which NetLimiter zone the bar shows (the **Internet** zone by default).
  - *Limiter*, *Blocker*, *Priorities* — ticked when on in NetLimiter (shown only; change them in NetLimiter).
  - *Open NetLimiter*, *Reset position* (back to its place before the notification area), *Open settings file*,
    *Open log folder*, *Releases page (GitHub)* and *Exit*.
- When NetLimiter's **Blocker** has connections waiting for your approval, the bar turns amber; click it to open
  NetLimiter and answer there. BandBar never answers for you.

| The bar shows | Meaning |
|---|---|
| Speeds with coloured arrows | Connected and up to date |
| `…` | Connecting to NetLimiter |
| `—` | The NetLimiter service is not running; BandBar reconnects automatically |
| `WFP?` | Connected, but Windows' Base Filtering Engine service is stopped, so NetLimiter cannot see any traffic |
| `denied` | NetLimiter refused access (the Windows user needs NetLimiter's *Monitor* right) |
| `API?` | NetLimiter's files failed the signature check, or this NetLimiter version is not compatible |
| `NL?` | NetLimiter is not installed |

### Settings

Optional. Right-click the bar and choose **Open settings file**: it opens `settings.json` (created with every
setting at its default the first time) in your editor. Save the file and the bar applies the change at once, no
restart needed. For example, to show two decimals, use units of 1000 and a different green:

```json
{ "decimals": 2, "unitBase": 1000, "colors": { "download": "#00C853" } }
```

If the file has a mistake, the bar keeps its current settings until you fix it (the log says why). BandBar itself
only ever changes the position (`anchorOffsetDip`) when you drag the bar, and leaves the rest of the file as it is.

The text colour follows your Windows taskbar (light or dark, or the text colour of a Windows contrast theme). If a
taskbar tool paints its own colours and the text is hard to read, set `"theme": "dark"` (white text) or `"theme": "light"` (dark text). With an auto-hiding taskbar,
`"followAutoHideTaskbar": false` keeps the bar visible at the screen edge instead of hiding it with the taskbar.

To hide the bar while particular programs are in front (for example a game you play in a window), and to show or
hide it with a hotkey:

```json
{ "fullscreenExeList": ["game.exe"], "hotkey": "Ctrl+Alt+B" }
```

All keys are listed in [docs/DESIGN.md](docs/DESIGN.md#81-settingsjson).

## Games, anti-cheat and privacy

BandBar is built to be invisible to games and anti-cheat systems:

- It installs no hooks, injects nothing, opens no handles to games and draws no overlay. The optional program list
  is matched against Windows' process list, without touching the programs.
- It learns about full-screen applications from the same Windows notification the taskbar uses, then hides
  completely so it cannot interfere with full-screen optimisations.
- The optional hotkey is a regular Windows hotkey, not a keyboard hook.
- It runs as your normal user and never asks for administrator rights.
- It opens no network connections and collects no data. Its log stays on your PC and masks your user name, paths
  and computer name.

The full list of rules, and every Windows function the app is allowed to call, is in
[docs/DESIGN.md](docs/DESIGN.md#7-security-model).

## FAQ

<details>
<summary><b>Windows SmartScreen warns when I run the setup. Is that expected?</b></summary>

Yes. Releases are not code-signed, and SmartScreen warns about unsigned programs it has not seen often. Choose
**More info → Run anyway**. To make sure the file is the one GitHub Actions built from the published source, compare
its SHA-256 hash with `SHA256SUMS.txt` (see [Install](#install)).
</details>

<details>
<summary><b>Can it get me banned by an anti-cheat system?</b></summary>

BandBar is built so that it has nothing to do with games: no hooks, no injection, no handles to other processes, no
overlay, and it hides completely while a full-screen game is in front. See
[Games, anti-cheat and privacy](#games-anti-cheat-and-privacy). No independent tool can promise what a game
publisher's anti-cheat will decide, but BandBar does nothing that such systems look for.
</details>

<details>
<summary><b>Why a separate window instead of a real taskbar toolbar?</b></summary>

Windows 11 removed taskbar toolbars ("deskbands"), the mechanism NetLimiter's own bar uses. Putting anything inside
the taskbar now means changing Explorer — injecting code into it or re-parenting windows into the taskbar — which
can break with a Windows update and take the whole taskbar down with it. BandBar stays an ordinary window that only
sits on top of the taskbar. The price: it cannot reserve space there, so with many taskbar buttons you may need to
drag it to a free place.
</details>

<details>
<summary><b>The bar disappears while Start or Search is open.</b></summary>

That is Windows 11: while Start or Search is open, it lifts the taskbar into the Start menu's own layer, above every
application window, and only Windows' shell components can draw there. The bar is back as soon as the menu closes.
</details>

<details>
<summary><b>I use a tool that changes or recolours the taskbar.</b></summary>

BandBar reads the taskbar's position from Windows, so a moved taskbar is followed. If a tool hides the notification
area from Windows, the bar falls back to a place near the end of the taskbar — drag it where you want it. If a tool
paints its own colours, set `"theme": "dark"` or `"light"` (see [Settings](#settings)).
</details>

<details>
<summary><b>Does it work with several monitors?</b></summary>

The bar sits on the main monitor's taskbar. A full-screen game or video on another monitor does not hide it; one on
the bar's monitor does. Taskbars on other monitors are on the [roadmap](#roadmap).
</details>

<details>
<summary><b>The NetLimiter update says files are in use.</b></summary>

Exit BandBar first (right-click → Exit), update NetLimiter, then start BandBar again. While it runs, BandBar keeps
NetLimiter's API files loaded.
</details>

## Build from source

```powershell
git clone https://github.com/yasirow/BandBar.git
cd BandBar
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

You need the .NET SDK 10; NetLimiter is only needed to run the app. [CONTRIBUTING.md](CONTRIBUTING.md) shows how to
install the SDK inside the repository folder, how to create the release packages, and the rules every change follows.

## Roadmap

| Version | Planned |
|---|---|
| 0.6.0 | Taskbars on secondary monitors |
| 1.0.0 | Stable release |

Done: 0.2.0 — drag and remember the position, stacked layout for vertical taskbars, auto-hiding taskbars, settings
applied without a restart. 0.3.0 — full screen per monitor and at start-up, program list, show/hide hotkey.
0.4.0 — zone menu, Limiter/Blocker/Priorities states, Blocker alert, open NetLimiter. 0.5.0 — `WFP?` status,
contrast themes, FAQ. See [CHANGELOG.md](CHANGELOG.md) for every release.

## Contributing

Bug reports, ideas and pull requests are welcome — please read [CONTRIBUTING.md](CONTRIBUTING.md) first. Security
issues go through [private reporting](SECURITY.md), not public issues.

## License

[MIT](LICENSE) © BandBar contributors. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for NetLimiter and the
build tools.
