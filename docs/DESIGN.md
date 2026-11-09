# BandBar design

This document describes how BandBar works and, more importantly, the rules it follows. Contributions are
reviewed against it; changing a rule means changing this document first, in the same pull request.

- [1. Goals and priorities](#1-goals-and-priorities)
- [2. Architecture](#2-architecture)
- [3. NetLimiter API](#3-netlimiter-api)
- [4. Window and rendering](#4-window-and-rendering)
- [5. Taskbar tracking](#5-taskbar-tracking)
- [6. Full screen and z-order](#6-full-screen-and-z-order)
- [7. Security model](#7-security-model)
- [8. Files and settings](#8-files-and-settings)
- [9. Build and supply chain](#9-build-and-supply-chain)
- [10. Packaging](#10-packaging)
- [11. Known limitations](#11-known-limitations)

## 1. Goals and priorities

Windows 11 no longer supports taskbar toolbars (deskbands), so NetLimiter's own taskbar bar cannot be shown there.
BandBar is an independent, separate small window that sits on the taskbar and shows the upload and download speed.

When requirements conflict, the higher one wins:

1. **Lightweight.** No runtime dependency other than NetLimiter and the .NET Framework that ships with Windows.
2. **Never disturb full-screen games or applications.**
3. **Follow the taskbar** on any screen edge.
4. **Never trigger anti-cheat systems.**
5. **Never touch other programs** — their files, folders, registry keys, processes or windows. BandBar only reads
   data from NetLimiter.
6. **Show two values:** upload (red ⬆) and download (green ⬇).
7. **Security first.**

## 2. Architecture

One process, one executable, C# on .NET Framework 4.8 with WinForms, x64.

| Component | File | Role |
|---|---|---|
| Entry point | `Program.cs` | Single-instance mutex, error handling, starts the API loader and the bar |
| API loader | `Api/NlApiLoader.cs`, `Api/Authenticode.cs` | Finds the installed NetLimiter, verifies and loads its API assemblies |
| Session | `Api/NlSession.cs` | The **only** code that uses NetLimiter types: connect, read the zone rate |
| Monitor | `Api/NlMonitor.cs` | Background thread: connects, reads once per second, reconnects, publishes readings |
| Bar | `BarForm.cs`, `BarLayout.cs`, `LayeredWindow.cs` | The window, its drawing and its placement |
| Taskbar | `Taskbar.cs` | Read-only taskbar and notification-area geometry |
| Settings, log | `Settings.cs`, `Log.cs`, `AppPaths.cs` | `settings.json`, the rotating log, where files may be written |
| Win32 | `NativeMethods.cs` | Every native call, see the allow-list in [§7](#7-security-model) |

Threads: the UI thread (window, shell notifications) and one monitor thread (NetLimiter). WCF callbacks from the
NetLimiter client only set a flag; they never touch the UI.

**Why .NET Framework 4.8:** the official NetLimiter API targets .NET Framework 4.6.2 and uses WCF
(`System.ServiceModel`), which .NET 5+ does not provide. .NET Framework 4.8 is part of Windows 10 and 11, so
nothing extra has to be installed.

**Why x64:** `NetLimiter.dll` and `NLInterop.dll` are built for AMD64 only. On ARM64 Windows, an x64 process runs
under emulation and can load them; an AnyCPU process would start as ARM64 and fail.

## 3. NetLimiter API

### 3.1 Where it comes from

At run time the API is loaded **read-only from the installed NetLimiter**, so it always matches the running
service. No NetLimiter file is ever copied into this repository, the build output or a release package.

- **Install folder:** the `InstallLocation` of NetLimiter's uninstall entry under
  `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall` (both registry views; read only), or the default
  `%ProgramFiles%\Locktime Software\NetLimiter`.
- **Allow-list:** `AppDomain.AssemblyResolve` loads only these names from that folder, with `Assembly.LoadFile` so
  that every dependency passes through the same check:

  | Assembly | Expected signer (O=) | Why it is loaded |
  |---|---|---|
  | `NetLimiter` | Locktime Software s.r.o. | The API |
  | `NLInterop`, `CoreLibNet` | Locktime Software s.r.o. | Dependencies of `NetLimiter.dll` |
  | `VirusTotalNet` | Locktime Software s.r.o. | Referenced by `CoreLibNet` types; loaded but **never called** |
  | `Microsoft.Extensions.Logging.Abstractions`, `System.Memory`, `System.Buffers`, `System.Runtime.CompilerServices.Unsafe` | Microsoft Corporation | Dependencies of `NetLimiter.dll` |

  Any other name is refused and logged; the bar then shows `API?`.
- **Signature check:** before loading, `WinVerifyTrust` verifies the file's embedded Authenticode signature and its
  chain to a trusted root, **without network access** (`WTD_REVOKE_NONE`, `WTD_CACHE_ONLY_URL_RETRIEVAL`). The
  signer certificate's organization must match the table. Certificate thumbprints are not pinned, because
  certificates are renewed.
- **Location check:** if an allow-listed assembly is ever loaded by anything other than this loader (for example a
  `NetLimiter.dll` dropped next to the executable), the app logs it and exits. Microsoft assemblies loaded from the
  Global Assembly Cache are accepted. This check fails closed but runs after the load: whoever can write next to
  the executable can also replace the executable itself, so that case is outside the threat model.
- **Type isolation:** only `NlSession` references NetLimiter types, and it is first used inside a `try` block after
  the loader is installed. A missing, refused or incompatible API becomes a status in the bar, never a crash.

### 3.2 Connection

- `new NLClient().Connect()` with no arguments connects to the **local** service over its named pipe. The host,
  port and credential parameters are never used; remote connections and the TCP binding are never enabled.
- Only NetLimiter's **Monitor** right is needed, which a standard user normally has. BandBar never calls
  NetLimiter's elevation functions and never changes NetLimiter's settings or access table.
- The connection state comes from the `StateChanged` event. A lost connection is retried with an exponential
  back-off of 2, 4, 8, 16 and then every 30 seconds.

### 3.3 Reading the speed

```csharp
NodeLoader loader = client.CreateNodeLoader();
loader.Filters.SelectAll();      // filter nodes only; applications and connections are never requested
loader.Load();
foreach (FilterNode node in loader.Filters.Nodes)
    if (node.FilterId == zone.InternalId) { down = node.TransferRate.In; up = node.TransferRate.Out; }
```

- The zone is NetLimiter's Internet zone unless `zoneFilterId` is set.
- The API has no rate event, so the rate is read once per second on the monitor thread. A read that takes longer
  than 2 seconds counts as a lost connection. Typical cost: connecting ~0.3 s, each read 1–10 ms.
- Reading stops completely while the bar is hidden for a full-screen application and while the session is locked.
- The bar is redrawn only when the displayed text changes.

### 3.4 What else is read (menu and Blocker alert)

All of it read-only, inside the same 2-second time limit as the speed:

- **Zones** for the *Zone* menu: the filters whose `Type` is `FilterType.Zone` (name and ID) and
  `GetInternetZone()`.
- **Feature states** shown in the menu: `IsLimiterEnabled`, `IsFwEnabled` (Blocker), `IsPriorityEnabled`.
- Both are read when the connection opens, then every tenth reading (about every 10 s), and at the next reading
  after the menu is opened; the menu shows the last values.
- **Pending Blocker requests:** while the Blocker is on, the number of entries in `FirewallRequests` with every
  reading. BandBar **never answers a request** (`ReplyFirewallRequest`), never registers as a request handler
  (`SubscribeAsFwReqeustHandler`) and does not subscribe to `FirewallRequestChange` — subscribing could make the
  service route requests to BandBar and wait for an answer that never comes. NetLimiter's own client stays the
  only place where requests are answered.
- If the service refuses one of these reads (for example because the Monitor right does not cover it), that one
  feature is switched off for the session and logged once; the speed keeps working.

### 3.5 States

| Bar shows | Meaning |
|---|---|
| speeds, coloured arrows | Connected |
| `…` | Connecting |
| `—` | NetLimiter service not reachable (retrying) |
| `denied` / `izin yok` | NetLimiter refused the Monitor right |
| `API?` | API assembly refused (signature) or incompatible NetLimiter version |
| `NL?` | NetLimiter is not installed |

The tooltip shows the full description and the exception type behind a failure — never an exception message,
because messages can contain paths.

## 4. Window and rendering

- A WinForms form created as a **popup** (`WS_POPUP`) tool window: `WS_EX_TOOLWINDOW` (not in Alt+Tab or on the
  taskbar), `WS_EX_NOACTIVATE` and `WM_MOUSEACTIVATE` → `MA_NOACTIVATE` (**never takes focus**), `WS_EX_TOPMOST`,
  `WS_EX_LAYERED`. WinForms' default overlapped style would make Windows enforce a minimum window height that does
  not fit a thin taskbar.
- **Transparent background:** the content is drawn with GDI+ into a 32-bit ARGB bitmap and pushed with
  `UpdateLayeredWindow` (per-pixel alpha), only when it changes. The background alpha is 1/255: invisible, but every
  pixel stays clickable. Text uses grayscale anti-aliasing, because ClearType fringes on a transparent background.
- **Text colour:** with `theme: "auto"` (default) the text is dark on a light taskbar and white otherwise. Windows'
  own values are read, never written: `SystemUsesLightTheme` and, when the taskbar shows the accent colour
  (`ColorPrevalence`), the brightness of `AccentColor` (both under `HKCU`). They are read again only when Windows
  announces a change (`WM_SETTINGCHANGE` "ImmersiveColorSet", `WM_DWMCOLORIZATIONCOLORCHANGED`). Taskbar tools
  that paint their own colours cannot be detected; `theme` or `colors.text` override the choice.
- **Icon:** the application icon is embedded in the executable (generated by `tools/make-icon.ps1`); the bar itself
  draws no image files.
- **Layout:** two lines; upload with a red up arrow on top, download with a green down arrow below; text
  left-aligned after the arrow. Arrows are drawn shapes (triangle head and stem), not font glyphs. Segoe UI 9 pt,
  scaled for DPI and reduced (down to 7 pt) when two lines do not fit the taskbar.
- **Stacked layout (left and right taskbars):** when the two-line layout is wider than a vertical taskbar, each
  value is split into its number and its unit on separate lines — four lines in total, the arrow beside the number
  — and the font is reduced (down to 7 pt) until the bar fits the taskbar's width.
- **Fixed width:** the text column is measured from the widest possible value, so the bar never resizes.
- **Units:** B/s → KB/s → MB/s → GB/s, base 1024 (or 1000), one decimal by default. Values are truncated, never
  rounded up. The decimal separator follows the Windows region settings.
- **Menu:** a native Win32 menu (`TrackPopupMenuEx` via WinForms `ContextMenu`):
  - *Zone* ▸ the NetLimiter zones, the shown one ticked; choosing one saves `zoneFilterId` ([§8.1](#81-settingsjson)).
  - *Limiter*, *Blocker*, *Priorities*: greyed out, ticked when on — information only ([§3.4](#34-what-else-is-read-menu-and-blocker-alert)).
  - *Open NetLimiter*.
  - *Reset position* (available while a dragged position is saved), *Open settings file*, *Open log folder*,
    *Releases page*.
  - *Exit*.

  The bar calls `SetForegroundWindow` only when the user right-clicks it, so the menu can close when the user clicks
  elsewhere. The *Open* items and *Releases page* start another program, under the rules in
  [§7.4](#74-starting-other-programs).
- **Blocker alert:** while NetLimiter's Blocker has connections waiting for approval, the bar's background turns a
  translucent amber, the tooltip says how many are waiting, and a plain left-click (not a drag) opens NetLimiter.
- **UI language:** Turkish when the Windows display language is Turkish, English otherwise.

## 5. Taskbar tracking

- **Geometry:** `SHAppBarMessage(ABM_GETTASKBARPOS)` gives the edge and rectangle; if it fails, the rectangle of the
  `Shell_TrayWnd` window and its monitor are used. Both are read-only.
- **Default position:** just before the notification area — `FindWindowEx(Shell_TrayWnd, "TrayNotifyWnd")` — on
  the taskbar surface, where a taskbar toolbar would sit. If the notification area cannot be found, 220 DIP from the end of
  the taskbar.
- **AppBar registration:** the bar registers with `ABM_NEW` **without** `ABM_SETPOS`: no screen space is reserved
  and the taskbar is not changed. This only subscribes the bar to shell notifications:
  `ABN_POSCHANGED`, `ABN_STATECHANGE`, `ABN_FULLSCREENAPP`. It unregisters with `ABM_REMOVE`.
- **Other triggers:** `WM_DISPLAYCHANGE`, `WM_DPICHANGED`, `WM_SETTINGCHANGE` (`SPI_SETWORKAREA`) and the
  `TaskbarCreated` message (Explorer restarted: register again). Bursts are coalesced into one reposition after
  100 ms. There is no polling timer.
- **Growing notification area:** the shell sends no notification when the notification area grows (a new tray
  icon, a wider clock). In the default position the notification area's rectangle is compared on each once-per-second
  reading the bar already receives — one `GetWindowRect`, no extra timer.
- **Vertical taskbars:** the bar is centred on the taskbar, in the two-line layout if it fits the taskbar's width
  and in the stacked layout ([§4](#4-window-and-rendering)) otherwise.
- **Explorer restarting:** while the taskbar does not exist, the bar keeps its last position (it only falls back to
  an estimate, the bottom of the primary screen, if it has never been placed) and waits for `TaskbarCreated`.

### 5.1 Moving the bar

- **Dragging:** the user drags the bar with the left mouse button **along the taskbar only** (left–right on a
  horizontal taskbar, up–down on a vertical one), and the bar stays inside the taskbar. A movement smaller than the
  system drag threshold is a click and moves nothing. While dragging, the bar uses the mouse capture of its own
  window (standard WinForms mouse events); no hook is involved and the bar is still never activated.
- **Remembering:** on release, the distance between the bar and the taskbar's end (right or bottom) is saved in
  DIP as `anchorOffsetDip` ([§8.1](#81-settingsjson)), so the place survives restarts and scaling changes. Moving
  the taskbar to another edge keeps the same distance on the new edge.
- **Reset position** in the menu sets `anchorOffsetDip` back to `null`: the bar returns to its default place before
  the notification area.

### 5.2 Auto-hiding taskbar

- The taskbar's auto-hide state is read with `SHAppBarMessage(ABM_GETSTATE)` at start-up and on every
  `ABN_STATECHANGE`. Its position — like that of any taskbar that is still partly off its monitor, for example right
  after auto-hide is turned off — is taken as the place where it appears when shown (the monitor edge), never from
  where it is while hidden. The same applies to the notification area used for the default position.
- The shell sends no notification when an auto-hiding taskbar slides in or out. **Only while auto-hide is on**, the
  bar therefore listens to one more out-of-context WinEvent, `EVENT_OBJECT_LOCATIONCHANGE`, **restricted to the
  taskbar's own thread** (`GetWindowThreadProcessId` of `Shell_TrayWnd`, used for nothing else) and ignored for any
  window other than the taskbar itself. Like the other WinEvent range ([§6.2](#62-staying-above-the-taskbar)), it is
  posted to the bar's own message loop; nothing is loaded into Explorer.
- `followAutoHideTaskbar: true` (default): the bar is shown only while the taskbar is fully shown, and reading stops
  while it is hidden — the same hiding as for full screen ([§6.1](#61-full-screen)). `false`: the bar stays visible
  at the screen edge where the taskbar appears.
- When the taskbar slides in, the bar checks the window order as after any other taskbar event, so it does not stay
  under the taskbar.

## 6. Full screen and z-order

### 6.1 Full screen

- **Signal:** `ABN_FULLSCREENAPP` from the shell — the same signal the taskbar uses to hide itself. It covers
  borderless windowed games. No hook, no polling.
- **Only the bar's own monitor counts.** When the shell reports a full-screen application, the bar looks at the
  foreground window (`GetForegroundWindow`, `MonitorFromWindow`). If that window covers a *different* monitor, the
  bar stays visible and remembers that a full-screen application is open elsewhere; a later foreground change to a
  window that covers the bar's monitor, while the shell still reports full screen
  (`SHQueryUserNotificationState` → busy or Direct3D full screen), hides it. When in doubt — no foreground window, or
  one that covers no monitor — the bar hides: not disturbing a game ranks above being visible (§1).
- **Already in full screen at start-up:** the shell reports only changes, so when the bar starts it asks once
  (`SHQueryUserNotificationState`). If a full-screen application covers the bar's monitor, the bar starts hidden;
  it comes back on the shell's "closed" notification or, as a fallback, on the first foreground change after which
  the shell no longer reports full screen.
- **Listed programs (`fullscreenExeList`, optional):** the bar also hides while one of these programs is the
  foreground window, full screen or not. The program is identified by the image name of the foreground window's
  process: `GetWindowThreadProcessId`, then that process ID in a snapshot of Windows' process list
  (`CreateToolhelp32Snapshot`, `Process32First`/`Process32Next`). **No handle to the program is ever opened** (no
  `OpenProcess`), so anti-cheat software has nothing to see. The answer is cached per process ID, and with an empty
  list nothing of this runs.
- **Hiding means hiding:** `SWP_HIDEWINDOW` and leaving the topmost band (`HWND_NOTOPMOST`), and the monitor stops
  reading. Making the bar transparent would not be enough: Windows' compositor still counts an overlapping window,
  which can disable full-screen optimisations.
- **Coming back:** shown again with `SWP_SHOWWINDOW | SWP_NOACTIVATE`, back in the topmost band, one reading
  immediately.

### 6.1.1 Show/hide hotkey

- Optional (`hotkey`, for example `"Ctrl+Alt+B"`; none by default). Registered with `RegisterHotKey` for the bar's
  own window, with `MOD_NOREPEAT`; **never a keyboard hook**. Pressing it hides the bar (and stops reading) or shows
  it again. The hidden state lasts until the hotkey is pressed again, the hotkey is removed from the settings, or
  BandBar restarts.
- At least one modifier is required, so a plain key is never taken from games. If another program already owns the
  combination, registration fails, a warning is logged and nothing else happens.

### 6.2 Staying above the taskbar

The taskbar is an always-on-top window as well. When it is clicked, when Start or a jump list opens, or when it
shows itself again after a full-screen application, it raises itself above other always-on-top windows. Windows
offers no "the taskbar moved above you" notification.

- The bar listens to one range of WinEvents **out of context** (`WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS`):
  `EVENT_SYSTEM_FOREGROUND` through `EVENT_SYSTEM_CAPTUREEND` — foreground changes, menu start/end and mouse capture
  start/end. Out of context means Windows posts the events to the bar's own message loop; nothing is loaded into
  any other process.
- Menu and mouse-capture events count only when they come from a window of the taskbar
  (`GetAncestor(hwnd, GA_ROOT)` is the taskbar), so clicks in other programs cost nothing. Foreground changes count
  from any window, because Start menus and jump lists are separate windows.
- On each relevant event, and then about once per frame (~16 ms, the Windows timer resolution) for about 600 ms, the
  bar checks whether the taskbar is above it by reading the window order (`GetWindow(GW_HWNDPREV)`). Only if it is
  covered does it move itself directly above the taskbar (not to the very top), so Start, jump lists and tooltips
  stay above the bar.
- **Fallback:** the taskbar was once seen above the bar with no event that would have revealed it. The same check
  therefore also runs on each once-per-second reading the bar already receives while it is visible — a few
  `GetWindow` calls, no extra timer — so such a case lasts at most about a second.
- If the new foreground window covers its monitor, the bar does not come forward; the full-screen notification
  hides it instead. The desktop itself (`GetShellWindow`) is the exception: it covers the monitor too, and clicking
  it — for example to close Start — is exactly when the taskbar raises itself.
- While Start or Search is open, Windows 11 moves the taskbar into the z-order band of the Start menu, which lies
  above the desktop band that holds every application window, always-on-top ones included (measured: the taskbar
  switches from band 1 to band 6 while Start is open, the bar stays in band 1). The bar is therefore hidden for that
  time even though it is above the taskbar in the window order, and is visible again when Windows moves the taskbar
  back. Only shell components can create windows in that band; not even UI Access reaches it.
- A few milliseconds of overlap can remain. Avoiding even that would require becoming part of the taskbar, which
  rule 5 forbids. Making the taskbar the bar's owner window is also rejected: Explorer restarts would destroy the bar.

## 7. Security model

### 7.1 Never

- `SetWindowsHookEx` of any kind (including low-level keyboard and mouse hooks), in-context WinEvent hooks, DLL
  injection, `SetParent` into the taskbar, making the taskbar the bar's owner, or any interaction with another
  process' memory or windows beyond reading positions.
- Opening a handle to another process (`OpenProcess`), games included.
- Process memory access, overlays, DirectX/OpenGL/Vulkan layers, drivers.
- Requesting administrator rights or elevating (`asInvoker` manifest; NetLimiter's elevation calls are never used).
- Any network connection or listening port. The API's internet features (VirusTotal, DNS, HTTP) are never called.
- NetLimiter's remote connection, its TCP binding, or changes to its settings, rules or access table — including
  switching the Limiter, Blocker or Priorities on or off (the menu only shows their state; the owner decided so in
  0.4.0), answering Blocker requests or registering as their handler.
- Writing to another program's files, folders or registry keys.
- Starting another program, except as described in [§7.4](#74-starting-other-programs).
- Telemetry, automatic updates, crash reporting, or downloading anything.
- Storing credentials (the named pipe uses the Windows identity).

### 7.2 Allowed Win32 calls

| Purpose | Calls |
|---|---|
| Taskbar geometry and notifications | `SHAppBarMessage` (`ABM_NEW`, `ABM_REMOVE`, `ABM_GETTASKBARPOS`, `ABM_GETSTATE`; never `ABM_SETPOS`), `FindWindow`, `FindWindowEx`, `GetWindowRect`, `RegisterWindowMessage("TaskbarCreated")` |
| Monitors and DPI | `MonitorFromRect`, `MonitorFromWindow`, `GetMonitorInfo`, `GetDpiForMonitor` |
| Our own window | `SetWindowPos`, `UpdateLayeredWindow`, `GetDC`, `ReleaseDC`, `CreateCompatibleDC`, `DeleteDC`, `SelectObject`, `DeleteObject`, `SetForegroundWindow` (only on right-click); mouse capture of our own window while it is dragged (WinForms) |
| Window order | `SetWinEventHook` (out of context only: `EVENT_SYSTEM_FOREGROUND`…`EVENT_SYSTEM_CAPTUREEND`, and `EVENT_OBJECT_LOCATIONCHANGE` for the taskbar's thread only while it auto-hides), `UnhookWinEvent`, `GetWindow(GW_HWNDPREV)`, `GetAncestor(GA_ROOT)`, `GetWindowThreadProcessId` (the taskbar window, to restrict that WinEvent; the foreground window, only with `fullscreenExeList`), `GetShellWindow` (to tell the desktop from a full-screen application) |
| Full screen | `GetForegroundWindow` (on a full-screen notification, a foreground change and at start-up), `SHQueryUserNotificationState`; with `fullscreenExeList` only: `CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS)`, `Process32First`, `Process32Next`, `CloseHandle` (of that snapshot) — never `OpenProcess` |
| Hotkey | `RegisterHotKey`, `UnregisterHotKey` (our own window; only when `hotkey` is set) |
| Our own process | `SetErrorMode` (no error dialogs), `WinVerifyTrust` (API signature, no network) |
| Our own files | Read, write and watch `settings.json` in our data folder ([§8](#8-files-and-settings)); `FileSystemWatcher` on that folder, for that file name only |
| Starting a program | `Process.Start` (`ShellExecuteEx`) under the rules in [§7.4](#74-starting-other-programs) |
| Registry (read only) | NetLimiter's uninstall entry under `HKLM` (install folder); `HKCU` `Themes\Personalize` and `DWM` values (text colour) |

The game list was first planned with `OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)`; since 0.3.0 the process
snapshot gives the same name without a handle, so `OpenProcess` stays on the "never" list.

### 7.3 Failure behaviour

- Every thread boundary catches and logs exceptions and turns them into a status.
- An exception on the UI thread is logged and the bar keeps running; more than three within 60 seconds means its
  state is broken, and the process then exits.
- An unhandled exception on any other thread is logged and ends only this process (`Environment.Exit`). Windows
  error dialogs are suppressed (`SetErrorMode`), so nothing can pop up over a game.

### 7.4 Starting other programs

BandBar starts another program only to show the user something they asked for:

- **Only on a click.** Only as the direct result of the user choosing a menu item — never at start-up, never from a
  timer, a setting, a file or a NetLimiter event.
- **Only fixed targets.** The target is built by the app, never taken from a setting or from external input:

  | Menu item | Starts | Since |
  |---|---|---|
  | *Open settings file* | Our own `settings.json` with the program Windows associates with `.json` files, or Windows' Notepad (`%SystemRoot%\System32\notepad.exe`) when there is none. If the file does not exist yet, it is first created with every key at its default ([§8.1](#81-settingsjson)). | 0.2.0 |
  | *Open log folder* | Windows' File Explorer (`%SystemRoot%\explorer.exe`) showing our own data folder, with `bandbar.log` selected | 0.2.0 |
  | *Open NetLimiter*, or a left-click on the bar while Blocker requests are waiting | `NLClientApp.exe` from the NetLimiter folder found for the API ([§3.1](#31-where-it-comes-from)), only if its Authenticode signature is valid and from Locktime Software s.r.o. — the same check as for the API files | 0.4.0 |
  | *Releases page* | The fixed project URL `https://github.com/yasirow/BandBar/releases` in the default browser; BandBar itself still never connects | 0.4.0 |

- **No elevation.** Never the `runas` verb or anything else that could ask for administrator rights.
- **Fire and forget.** The started program runs on its own: BandBar keeps no handle to it, does not wait for it and
  never sends it input. A failure to start is logged and nothing else happens (no error dialog).

## 8. Files and settings

BandBar writes only next to its executable when that folder is writable (the installer's default), otherwise to
`%LocalAppData%\BandBar`. It never writes the registry and never registers itself to start with Windows; only the
installer's optional, unchecked "start with Windows" task creates a shortcut in the user's Startup folder.

### 8.1 `settings.json`

Optional. Read at start-up; ignored (with a log entry) if it is invalid or larger than 64 KB.

- **Changes apply at once.** A `FileSystemWatcher` on the data folder, for this one file name, reports changes; the
  file is read again 300 ms after the last one and applied without a restart. If the changed file is invalid (for
  example saved half-way through an edit), the current settings stay in effect and a warning is logged; a deleted
  file means the defaults.
- **What the app writes, and when.** Only on a user action: dragging the bar (`anchorOffsetDip`), *Reset position*
  (sets `anchorOffsetDip` to `null`), choosing a zone (`zoneFilterId`; the Internet zone is written as `null`) and
  *Open settings file*. When no file exists yet, whichever of these comes
  first creates it with every key below at its default (and the new position), so the user always finds the full
  list in it; resetting with no file creates nothing. An existing file is changed **only in the key concerned**:
  every other key, unknown keys, and their order are kept. An existing file that is invalid or larger than 64 KB is
  never overwritten; the new position then lasts for the session and a warning is logged.
- **Atomic.** The new content is written to `settings.json.tmp` in the same folder and then swapped in
  (`File.Replace`, or `File.Move` when there is no file yet), so a crash or power loss leaves either the old or the
  new file, never half of one.

| Key | Default | Meaning |
|---|---|---|
| `zoneFilterId` | `null` | NetLimiter filter ID of the zone to show; `null` = Internet zone. Set by the *Zone* menu |
| `unitBase` | `1024` | `1024` or `1000` |
| `decimals` | `1` | 0–3 |
| `theme` | `"auto"` | `"auto"` (follow the Windows taskbar), `"dark"` or `"light"` — the taskbar's colour, which decides the text colour |
| `colors.upload` / `colors.download` | `"#E74C3C"` / `"#2ECC71"` | Arrow colours |
| `colors.text` | `null` | `null` = from `theme` (`#FFFFFF` on a dark taskbar, `#1A1A1A` on a light one) |
| `colors.background` | `null` | `null` = transparent; a colour is drawn with `opacity` |
| `opacity` | `0.85` | Background opacity when a background colour is set |
| `anchorOffsetDip` | `null` | `null` = before the notification area; otherwise DIP from the taskbar's end (right or bottom). Set by dragging the bar |
| `hideOnFullscreen` | `true` | Hide while a full-screen application is in front on the bar's monitor |
| `fullscreenExeList` | `[]` | Programs that also hide the bar while in front, full screen or not, for example `["game.exe"]`; names only, case-insensitive, `.exe` optional, at most 64 |
| `followAutoHideTaskbar` | `true` | With an auto-hiding taskbar: `true` = show the bar only while the taskbar is shown; `false` = keep it visible at the screen edge |
| `hotkey` | `null` | Show/hide hotkey, for example `"Ctrl+Alt+B"`: modifiers `Ctrl`, `Alt`, `Shift`, `Win` (at least one) plus a letter, digit or `F1`–`F24` |
| `logLevel` | `"info"` | `debug`, `info`, `warn`, `error` |

Example:

```json
{ "decimals": 2, "unitBase": 1000, "colors": { "download": "#00C853" } }
```

### 8.2 `bandbar.log`

At most 256 KB plus one backup (`bandbar.log.1`). Before a line is written, the user profile, AppData and temp
paths, the user name (as a path segment) and the computer name are replaced with placeholders, so a log can be
attached to a public issue.

## 9. Build and supply chain

- **SDK:** .NET SDK 10 (pinned by `global.json`, feature roll-forward). `build.ps1` keeps all SDK and NuGet state
  inside the repository: it points `USERPROFILE`, `APPDATA`, `LOCALAPPDATA`, `DOTNET_CLI_HOME` and the NuGet cache
  variables at `.dotnet-home`, `.packages` and `.nuget` for the build processes, disables telemetry, first-run
  side effects and background update checks, and leaves no MSBuild or compiler server running.
- **NetLimiter API at compile time:** the official `NetLimiter` package from nuget.org (owner LocktimeSoftware) is
  fetched with `PackageDownload`, so none of its dependencies are downloaded. The compiler references
  `runtimes/win-x64/lib/net462/NetLimiter.dll` with `Private=false`, and the build fails unless the file's SHA-256
  matches the value pinned in the project (pinned after verifying the package's author and repository signatures
  with `dotnet nuget verify`).
- **Other packages:** only Microsoft's .NET Framework reference assemblies (build-time only). They are referenced
  explicitly, so a clean PC and a CI runner with the targeting pack installed compile against the same files; they
  are locked by `packages.lock.json` and restored in locked mode.
- **Unit tests:** `tests/BandBar.Tests` (MSTest, test-time only, never part of a release) covers speed formatting,
  reading and writing `settings.json` and the log scrubber. It uses MSTest **3.1.1** on purpose: from 3.2 on, MSTest
  depends on a telemetry package, and this project does not take one even at test time. Its packages are locked like
  the app's. `build.ps1 -Test` runs the tests; `tools/package.ps1`, and with it CI and the release workflow, always
  does.
- **Temporary files:** `build.ps1` also points `TEMP` and `TMP` at `.dotnet-home\Temp`, so build and test runs
  leave nothing in the user's temp folder.
- **Output check:** the build fails if its output contains anything other than `BandBar.exe` and
  `BandBar.exe.config`.
- **Reproducibility and privacy:** `Deterministic`, `PathMap` and no PDB in Release, so no build-machine path or user
  name is embedded in the executable. Warnings are errors.
- **CI:** GitHub-hosted `windows-2025` runners, actions pinned by commit SHA, read-only token except for the
  release job.

## 10. Packaging

Each release has three files, built by the release workflow from the tagged commit:

| File | Contents |
|---|---|
| `BandBar-Setup-x.y.z.exe` | Per-user Inno Setup installer |
| `BandBar-x.y.z-portable.zip` | The executable and its notices, to run from any folder |
| `SHA256SUMS.txt` | SHA-256 of both files |

**Installer:** installs to `%LocalAppData%\Programs\BandBar` without administrator rights
(`PrivilegesRequired=lowest`), adds a Start menu shortcut and, only if the user ticks it, a Startup shortcut. It
closes a running BandBar first. Installing a newer version over an older one upgrades in place and keeps
`settings.json`. Before installing it checks (read only) that the .NET Framework 4.8 is present and, if not,
explains what to do; it never downloads anything. It never writes `HKLM`, `Program Files`, NetLimiter, or any other
program.

**Uninstall** (Windows Settings → Apps) removes exactly: the installed files, the shortcuts it created, its own
uninstall entry under `HKCU`, and — by name — `settings.json`, `bandbar.log` and `bandbar.log.1` in the install
folder and in `%LocalAppData%\BandBar`, then those folders if they are empty. Nothing else.

**Portable:** unzip anywhere and run; remove by deleting the folder.

**Signing:** releases are not code-signed yet, so Windows SmartScreen may warn on first run. Each release publishes
SHA-256 checksums.

## 11. Known limitations

- Only the primary taskbar is used.
- The bar cannot reserve space on the taskbar; with many taskbar buttons it can overlap them (most likely on
  Windows 10, where taskbar buttons can reach the notification area). It can be dragged to a free place.
- With an auto-hiding taskbar the bar appears when the taskbar has finished sliding in; it does not slide with it.
- While Start or Search is open on Windows 11, the bar is hidden: Windows puts the taskbar into a layer above all
  application windows for that time ([§6.2](#62-staying-above-the-taskbar)).
- While it runs, BandBar keeps NetLimiter's API files loaded. Exit BandBar before updating NetLimiter, so the
  NetLimiter installer does not find those files in use.
