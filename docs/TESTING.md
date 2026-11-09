# Testing

BandBar talks to a Windows service and to the Windows shell, so most of its behaviour can only be verified on a
real desktop. Every release goes through the automated checks below and the manual test plan.

## Automated checks (every build, locally and in CI)

| Check | Where | Fails the build when |
|---|---|---|
| Warnings as errors | `Directory.Build.props` | Any compiler or MSBuild warning |
| NetLimiter API pin | `VerifyNetLimiterApi` target | The compile-time `NetLimiter.dll` does not match the pinned SHA-256 |
| Locked restore | `build.ps1` | A package differs from `packages.lock.json` |
| Single-file output | `VerifyOutputIsSingleExe` target | The output contains anything besides `BandBar.exe` and `BandBar.exe.config` |
| Unit tests | `tests/BandBar.Tests` via `build.ps1 -Test` (always run by `tools/package.ps1`, so by CI and the release workflow) | Speed formatting, `settings.json` reading and writing (strict JSON, keys and order kept, invalid files never overwritten, no temporary file left), the log scrubber, the program list (including reading a name from the process snapshot) or hotkey parsing misbehave |
| Installer build | `tools/package.ps1 -RequireInstaller` (CI) | The Inno Setup script does not compile |
| Tag matches version | Release workflow | The pushed tag differs from `<Version>` in the project |

## Manual test plan

Run the build to test from `src\BandBar\bin\Release\net48\BandBar.exe`, or install the release candidate.
The log (`bandbar.log`, next to the executable) should contain no `WARN` or `ERROR` lines after a normal session.

### Basics

| # | Steps | Expected |
|---|---|---|
| B1 | Start BandBar with NetLimiter running | Two lines appear just before the notification area: red ⬆ upload on top, green ⬇ download below; values follow NetLimiter's Internet zone |
| B2 | Hover the bar | Tooltip: `BandBar <version>`, zone, `Connected`, NetLimiter version |
| B3 | Type in any window, left-click the bar, keep typing | The typing window keeps focus |
| B4 | Right-click the bar; click elsewhere; right-click again and choose Exit | Menu opens, closes on the outside click, Exit closes the bar |
| B5 | Start the executable a second time | Still exactly one bar |
| B6 | Lock (Win+L) and unlock | The bar keeps updating after unlock |
| B7 | Settings → Personalization → Colors: switch the Windows mode between Light and Dark | The text turns dark on the light taskbar and white on the dark one, without restarting BandBar |
| B8 | Show the accent colour on the taskbar (Colors → "Show accent color on Start and taskbar") with a light and with a dark accent | The text stays readable |
| B9 | Settings → Accessibility → Contrast themes: apply a dark one (for example Night sky) and a light one (Desert), then switch contrast themes off | The text takes the contrast theme's text colour each time and returns to normal afterwards, without restarting BandBar; the arrows keep their colours |

### Taskbar

| # | Steps | Expected |
|---|---|---|
| T1 | Click the taskbar, open Start, right-click taskbar buttons, switch windows; close Start by clicking the desktop, with Esc and with the Start button | The bar stays visible (at most a brief flicker); jump lists appear above it. While Start or Search is open on Windows 11 the bar is hidden (known limitation); it is back as soon as the menu closes, whichever way |
| T2 | Start a program that adds a tray icon, then close it | The bar moves left/right within about a second |
| T3 | Restart Windows Explorer (Task Manager) | The bar returns to its place |
| T4 | Move the taskbar to each screen edge (if your setup allows) | The bar follows on the taskbar surface |
| T5 | Change display scaling or resolution | The bar re-scales and stays inside the taskbar |
| T6 | Switch virtual desktops (Win+Ctrl+← / →) | The bar stays on the taskbar on every desktop |
| T7 | On Windows 10, open many windows so taskbar buttons reach the notification area | Known limitation: the bar can overlap buttons; nothing breaks; the bar can be dragged to a free place |
| T8 | Restart Windows Explorer and watch the bar while the taskbar is gone | The bar stays where it was (it does not jump to the bottom of the screen) and is back on the taskbar when it returns |

### Position and layout

| # | Steps | Expected |
|---|---|---|
| P1 | Drag the bar left and right with the left mouse button | It follows the mouse along the taskbar only, never leaves the taskbar, and focus stays in the window you were using |
| P2 | Click the bar without moving the mouse | It does not move |
| P3 | Without a `settings.json`, drag it somewhere, exit BandBar and start it again | It comes back at the same place; `settings.json` was created with every key at its default and `anchorOffsetDip` set |
| P4 | Right-click → Reset position | It returns before the notification area; `anchorOffsetDip` is `null` in `settings.json`, nothing else changed |
| P5 | With a dragged position, change display scaling | The bar keeps its distance from the end of the taskbar |
| P6 | Move the taskbar to the left or right edge (if your setup allows) | The bar fits the taskbar's width: two lines if they fit, otherwise four stacked lines (number, unit); dragging works up and down |

### Auto-hiding taskbar

| # | Steps | Expected |
|---|---|---|
| H1 | Turn on "Automatically hide the taskbar" (Windows Settings → Personalization → Taskbar) | While the taskbar is hidden the bar is hidden too; when you point at the screen edge and the taskbar slides in, the bar appears on it |
| H2 | Set `"followAutoHideTaskbar": false` | The bar stays visible at the screen edge while the taskbar is hidden, and stays above the taskbar when it slides in |
| H3 | Turn auto-hide off again | The bar behaves as before (always visible on the taskbar) |

### Settings and menu

| # | Steps | Expected |
|---|---|---|
| C1 | Without a `settings.json`, right-click → Open settings file | The file is created with every key at its default and opens in the `.json` editor, or Notepad |
| C2 | Change `decimals` to `2` and save | The bar shows two decimals within a second, without a restart |
| C3 | Save the file with a mistake (delete a closing brace) | The bar keeps its current settings; the log has a `WARN` explaining why |
| C4 | Fix the file and save; then drag the bar | The fix applies; dragging changes only `anchorOffsetDip` and keeps the rest of the file, including its order |
| C5 | Right-click → Open log folder | File Explorer opens BandBar's data folder with `bandbar.log` selected |

### NetLimiter menu and Blocker alert

| # | Steps | Expected |
|---|---|---|
| N1 | Right-click → Zone | NetLimiter's zones are listed, the shown one ticked; choosing another changes the values within a few seconds and saves `zoneFilterId` (the Internet zone as `null`) |
| N2 | Look at Limiter / Blocker / Priorities in the menu, switch one in NetLimiter, open the menu again after ~10 s | Greyed out, ticked as in NetLimiter; clicking them does nothing |
| N3 | Right-click → Open NetLimiter | NetLimiter's window opens (no administrator prompt from BandBar) |
| N4 | Right-click → Releases page | The browser opens the project's Releases page |
| N5 | With the Blocker set to ask, start a program that NetLimiter has not seen | The bar turns amber within a second or two, the tooltip counts the waiting connections, a click opens NetLimiter; answering there turns the bar back to normal. BandBar never answers |
| N6 | Disconnect (stop the NetLimiter service) and open the menu | Zone shows "not connected"; the three states are hidden |

### Full screen and games

| # | Steps | Expected |
|---|---|---|
| F1 | Start a game in borderless windowed mode | The bar disappears; the log shows `Full-screen application opened; hiding` |
| F2 | Leave the game or Alt+Tab to the desktop | The bar comes back |
| F3 | Repeat F1–F2 with exclusive full screen | Same as F1–F2 |
| F4 | Play a game protected by anti-cheat | No warning, kick or stutter |
| F5 | Start BandBar while a full-screen game is already in front (for example from a shortcut key or a script) | The bar starts hidden (`A full-screen application is already in front` in the log) and appears when the game closes or you switch away |
| F6 | Drag the bar, then start a full-screen game | The bar hides as usual and comes back at its dragged place |
| F7 | With two monitors: a full-screen video or game on the *other* monitor | The bar stays visible (`on another monitor; staying visible`); moving the full-screen window to the bar's monitor hides it |
| F8 | Set `"fullscreenExeList": ["notepad.exe"]`, then switch between Notepad and another window | The bar hides while Notepad is in front and returns when it is not; the log does not name the program |
| F9 | Set `"hotkey": "Ctrl+Alt+B"` and press it twice | The bar hides and comes back; removing `hotkey` from the file while hidden brings the bar back |
| F10 | Set a hotkey another program already uses | A `WARN` explains it; the bar keeps working |

### Service and failure handling

| # | Steps | Expected |
|---|---|---|
| S1 | Stop the NetLimiter service (`services.msc`, needs administrator; protection pauses briefly) | Bar shows grey arrows and `—`; no dialog |
| S2 | Start the service again | Speeds return within a few seconds |
| S3 | Start BandBar on a PC without NetLimiter — or start a **Debug** build with `BANDBAR_API_DIR` set to an empty folder (see CONTRIBUTING.md) | Bar shows `NL?`; no dialog |
| S4 | Start a **Debug** build with `BANDBAR_API_DIR` set to a folder that holds an unsigned file named `NetLimiter.dll` | Bar shows `API?`; the log says the signature is not valid; nothing from that folder is loaded |
| S5 | On a test PC only: stop the *Base Filtering Engine* service (needs administrator; it also stops Windows Firewall), then start it again | Bar shows `WFP?` with grey arrows and the tooltip explains it; the speeds return when the service runs again |

### Windows 10 and several monitors

| # | Steps | Expected |
|---|---|---|
| W1 | On Windows 10 (22H2), run B1–B9, T1–T6 and F1–F2 | Same results as on Windows 11, except the Windows 10 limitation in T7 |
| W2 | On Windows 10, turn on "Use small taskbar buttons" | Both lines still fit (the font gets smaller); nothing is cut off |
| M1 | Two monitors, taskbar shown on all displays, no `taskbars` setting | The bar sits on the main monitor's taskbar only |
| M2 | Right-click → Display → All displays; then Other displays; then Main display | All: a bar on every taskbar, 220 DIP from the end of the other ones, without a restart. Others: the main taskbar's bar disappears. Main: only the main one again. `taskbars` in `settings.json` follows each choice |
| M3 | With bars on both taskbars: two monitors with different scaling (for example 100 % and 150 %) | Each bar has the size that fits its own taskbar |
| M4 | Settings → System → Display: make the other monitor the main display, then switch back | The main bar moves to the new main taskbar, the other bar to the other one |
| M5 | With *All displays*: drag the bar on the other taskbar, then Reset position on it | Only that bar moves (`otherAnchorOffsetDip` saved, then `null`); the main bar stays |
| M6 | With *All displays*: turn off "Show my taskbar on all displays", then on again; disconnect and reconnect a monitor | The other bar disappears and comes back by itself within about a second |
| M7 | With *Other displays* and a single monitor (or the option above off) | The main taskbar keeps its bar |
| M8 | With *All displays*: a full-screen game or video on one monitor; also `fullscreenExeList` with a program on one monitor | Only the bar on that monitor hides; the other keeps updating |

### Installer (release candidates)

| # | Steps | Expected |
|---|---|---|
| I1 | Run `BandBar-Setup-x.y.z.exe` | No administrator prompt; installs to `%LocalAppData%\Programs\BandBar`; Start menu entry |
| I2 | Install a newer version over it | Upgrades in place; `settings.json` (with a dragged position) kept |
| I3 | Uninstall from Windows Settings → Apps | The install folder, shortcuts and uninstall entry are gone, including `settings.json`; nothing else changed |
| I4 | Check `SHA256SUMS.txt` against the downloads | Hashes match |
| I5 | Look at the Start menu entry, the setup file and Windows Settings → Apps | BandBar's own icon (red and green arrows) is shown |
| I6 | Run the setup on Windows 10 1809 without .NET Framework 4.8 | A clear message, setup ends without installing anything |

## Results

| Version | Environment | Result |
|---|---|---|
| 0.1.0 | Windows 11, NetLimiter 5.3 | Automated checks; B1, B2, B5, F1–F2 (simulated full-screen window); no listening ports or network connections; idle CPU about 0.02 %, about 40 MB private memory |
| 0.1.1 | Windows 11, NetLimiter 5.3 | B1, B2, B4–B6, T2, T3, F1–F4 passed (F4 with a title using kernel-mode anti-cheat). T1 failed: brief disappearance on Start and on taskbar right-click |
| 0.1.2 | Windows 11, NetLimiter 5.3 | B3 passed. T1 re-test pending |
| 0.1.3 | Windows 11, NetLimiter 5.3 | I1 passed (per-user install, Start menu entry, optional startup shortcut). Installer built and checksums verified by CI |
| 0.1.4 | Windows 11, NetLimiter 5.3 | Rename release (no behaviour change). I1 passed (installed per user with startup shortcut, runs); I3 passed for the previous version (uninstall left no folder, shortcut or uninstall entry); I4 verified by CI and after download |
| 0.1.5 | Windows 11, NetLimiter 5.3 | B2, B7, B8 passed (text follows light/dark mode and accent colour without restart); normal use unaffected. T1: a brief flicker remains on Start and taskbar right-click (known limitation; check interval shortened from 50 to ~16 ms afterwards). I1–I5 pending on the published build; later I1 and I4 passed (clean install of the published setup, checksums match) |
| 0.2.0 | Windows 11, NetLimiter 5.3 | Automated checks and 94 unit tests pass; the build writes nothing to the user profile or temp folder. Passed: B1, B2, B4, P1–P4, C1 (opens in Notepad), C2, C3 (warning logged, settings kept, the fix applied at once), F1–F2, F6, T1 (after the desktop-click fix the bar is back within ~50 ms; hidden while Start is open, measured as a Windows band change). Pending: C4 (drag after a fix), C5, P5, H1–H3, T8. P6 needs a tool that moves the Windows 11 taskbar to a side |
| 0.3.0 | Windows 11, NetLimiter 5.3 | Automated checks and 123 unit tests pass. Manual tests pending (F5, F7–F10; F7 needs a second monitor) |
| 0.4.0 | Windows 11, NetLimiter 5.3 | Automated checks and 125 unit tests pass. Manual tests pending (N1–N6) |
| 0.5.0 | Windows 11, NetLimiter 5.3 | Automated checks and 125 unit tests pass. S3 (with the Debug override) and S4 passed. Connected normally: speeds as before, no `WFP?`. Pending: B9, S5 (test PC only), W1–W2 (Windows 10) |
| 0.6.0 | Windows 11, NetLimiter 5.3 | Automated checks and 130 unit tests pass. Manual tests pending (M1–M8) |
