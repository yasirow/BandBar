# Changelog

All notable changes to BandBar are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html). While the version is below 1.0.0, each roadmap
milestone is a minor release (`0.x.0`) and fixes in between are patch releases (`0.x.y`).

## [Unreleased]

## [0.2.0] - 2026-11-09

### Added
- **Drag the bar** with the left mouse button to any place along the taskbar. It stays inside the taskbar, a plain
  click never moves it, and the place is remembered (`anchorOffsetDip`) across restarts and scaling changes.
- **Reset position** in the right-click menu puts the bar back before the notification area.
- **Stacked layout for left and right taskbars:** when the two lines do not fit a vertical taskbar, the number and
  the unit go on separate lines and the font shrinks until the bar fits the taskbar's width.
- **Auto-hiding taskbars:** the bar hides and stops reading while the taskbar is hidden, and comes back when it
  slides in. New setting `followAutoHideTaskbar` (default `true`); `false` keeps the bar at the screen edge.
- **Settings apply without a restart:** saving `settings.json` takes effect within a moment. A file with a mistake
  is ignored (with a log entry) and the current settings stay.
- **Open settings file** and **Open log folder** in the right-click menu. Whenever BandBar creates the settings file
  (by this menu item or by the first drag), it contains every key at its default, so all options are in view.
- Tooltip hint: drag to move, right-click for the menu.

### Fixed
- After a click on the desktop (for example to close Start), the taskbar could stay above the bar for a second or
  more. The desktop covers the whole screen, so the bar mistook it for a full-screen application and skipped its
  check; the desktop is now told apart (`GetShellWindow`), and the bar is back within a few milliseconds.

### Changed
- When Explorer restarts, the bar now stays where it was until the taskbar is back, instead of jumping to the
  bottom of the screen for a few seconds.
- With `"logLevel": "debug"`, the log records when the taskbar got above the bar and how long after which event, to
  help diagnose taskbar setups.
- `settings.json` must be one complete JSON object; a file cut off half-way through a save is no longer accepted.
- Unit tests (MSTest 3.1.1, test-time only) for speed formatting, settings and the log scrubber run on every build
  in CI and before every release.
- The build also keeps its temporary files inside the repository.

### Security
- New rule for starting other programs (docs/DESIGN.md §7.4): only when the user picks a menu item, only fixed
  targets (BandBar's own settings file in the user's editor or Notepad, its own folder in File Explorer), never
  elevated, nothing kept afterwards.
- BandBar writes `settings.json` only on a user action, changes only the key concerned, never overwrites a file it
  cannot read, and writes through a temporary file so the file is never left half-written.
- The auto-hide support adds one more out-of-context WinEvent (`EVENT_OBJECT_LOCATIONCHANGE`), restricted to the
  taskbar's own thread and installed only while the taskbar auto-hides. Still no hooks into other processes.
- MSTest 3.1.1 was chosen because later versions depend on a telemetry package.

### Known limitations
- With an auto-hiding taskbar the bar appears once the taskbar has finished sliding in; it does not slide along.
- While Start or Search is open on Windows 11, the bar is hidden. Measured: Windows moves the taskbar into the Start
  menu's own layer, above every application window, for that time. Only Windows' shell components can draw there.

## [0.1.5] - 2026-10-16

### Added
- The text colour follows the Windows taskbar: dark text on a light taskbar, white text on a dark one, and a
  readable choice when the taskbar shows the accent colour. It changes as soon as you switch themes. New default
  `"theme": "auto"`; `"dark"` and `"light"` force a choice.
- BandBar has its own icon (red and green arrows), shown on the setup, in the Start menu and in Windows Settings →
  Apps. It is embedded in the executable.
- The setup checks that the .NET Framework 4.8 is installed and explains what to do if it is not, instead of
  installing an app that cannot start.

### Changed
- Taskbar watching now reacts to menu and mouse-capture events only when they happen on the taskbar, so clicks in
  other programs no longer trigger any work.
- After such an event the bar checks about once per frame (~16 ms, previously 50 ms) whether the taskbar has covered
  it, which shortens the brief disappearance when Start or a jump list opens. A short flicker can remain: Windows
  reports the taskbar moving to the front only after it happened.
- An error in a window handler no longer closes the bar; BandBar logs it and keeps running, and exits only if errors
  keep repeating (more than three within a minute).
- Removed three settings keys that had no effect yet (`followAutoHideTaskbar`, `fullscreenExeList`, `hotkey`); they
  will return with the features that use them.
- Requirements clarified: Windows 11, or Windows 10 version 1903 or later (1809 with .NET Framework 4.8 installed).

### Fixed
- The log could mask parts of ordinary words that contained the computer name (for example "Windows" on a PC named
  "WIN"); the name is now masked only as a whole word.
- The error code of a failed redraw was read after clean-up calls had overwritten it.
- On a very thin taskbar the layout was rebuilt on every repositioning; it is now rebuilt only when the DPI or the
  taskbar thickness changes.

### Known limitations
- Exit BandBar before updating NetLimiter: while it runs, it keeps NetLimiter's API files open.

## [0.1.4] - 2026-10-15

### Changed
- **The project is now called BandBar.** It was renamed at the request of the NetLimiter team, so that it cannot be
  confused with NetLimiter's own taskbar bar. The executable, installer, release files, log file
  (`bandbar.log`) and fallback data folder (`%LocalAppData%\BandBar`) carry the new name.
- The installer is a new product entry. If you installed 0.1.3, which was published under the earlier name, exit it
  and uninstall it from Windows Settings → Apps first (see README → Update).
- Releases are no longer marked as pre-releases, so the README's download button and `/releases/latest` lead
  straight to the newest version. The "early alpha" status badge still describes the project's maturity.

## [0.1.3] - 2026-10-14

First public release.

### Added
- Per-user installer (no administrator rights), portable ZIP and `SHA256SUMS.txt` for every release.
- GitHub Actions: a build on every push and pull request, and a release pipeline that builds, packages and
  publishes the files for a version tag.
- Documentation: README, design notes, test plan, contributing guide, security policy, third-party notices.

### Changed
- No change to the application's behavior since 0.1.2.

## [0.1.2] - 2026-10-14

### Fixed
- The bar could briefly disappear when the Start button was clicked or a taskbar button was right-clicked,
  because the taskbar raises itself above other always-on-top windows. The bar now also listens to the shell's
  menu and mouse-capture notifications (out of context, no hooks into other processes) and, for about 600 ms after
  each one, checks every 50 ms whether the taskbar is above it.

### Changed
- When it has to come back to the front, the bar is placed directly above the taskbar instead of at the very top,
  so the Start menu, jump lists and tooltips stay above it.
- The bar only re-orders itself when it is actually covered; the check reads the window order and changes nothing.

## [0.1.1] - 2026-10-14

### Fixed
- After a click on the taskbar, or after a full-screen application closed, the taskbar could stay on top of the
  bar and hide it completely, so the app looked closed while it was running. The bar now moves back to the front
  whenever the foreground window changes, unless the new foreground window is full screen.

## [0.1.0] - 2026-10-14

Initial preview.

### Added
- Connection to the local NetLimiter 5 service through its official API, over the service's local named pipe only:
  no administrator rights, no remote connection, no TCP.
- Live upload and download speed of the selected zone (Internet by default), updated once per second.
- Compact two-line display: red ⬆ upload on top, green ⬇ download below, transparent background, units
  B/s → KB/s → MB/s → GB/s with one decimal.
- Placement on the taskbar surface, just before the notification area, for taskbars on any screen edge.
- Event-driven repositioning when the taskbar moves or resizes, when Explorer restarts, when DPI, resolution or
  work area change, and when the notification area grows.
- Hides itself and stops reading while a full-screen application (including borderless windowed games) is in
  front; comes back when it closes.
- Stops reading while the session is locked.
- No crash when the NetLimiter service is unavailable: shows `—` and reconnects with an exponential back-off
  (2 s → 30 s). Short status texts for missing permission, untrusted or incompatible API, and missing NetLimiter.
- Tooltip with version, zone, link status and NetLimiter version.
- Single instance; right-click menu with Exit.
- English or Turkish UI, following the Windows display language.

### Security
- The NetLimiter API is loaded read-only from the installed NetLimiter. Only eight known assemblies are accepted,
  and each one must carry a valid Authenticode signature from the expected publisher (checked without network
  access). If an API assembly is ever loaded from anywhere else, the app exits.
- No hooks, injection or drivers. The bar is a tool window that never takes focus. Windows error dialogs are
  suppressed so nothing pops up over a game.
- The log masks the user name, profile paths and computer name.
- Build: the compile-time NetLimiter package is pinned by SHA-256, the output contains only the executable, and
  the build writes nothing to the user profile.

### Known limitations
- The position cannot be dragged or saved yet.
- An auto-hiding taskbar is not supported yet.
- If the app is started while a full-screen game is already running, it does not hide until that game closes.

[Unreleased]: https://github.com/yasirow/BandBar/compare/v0.2.0...HEAD
[0.2.0]: https://github.com/yasirow/BandBar/releases/tag/v0.2.0
[0.1.5]: https://github.com/yasirow/BandBar/releases/tag/v0.1.5
[0.1.4]: https://github.com/yasirow/BandBar/releases/tag/v0.1.4
[0.1.3]: https://github.com/yasirow/BandBar/blob/main/CHANGELOG.md#013---2026-10-14
[0.1.2]: https://github.com/yasirow/BandBar/blob/main/CHANGELOG.md#012---2026-10-14
[0.1.1]: https://github.com/yasirow/BandBar/blob/main/CHANGELOG.md#011---2026-10-14
[0.1.0]: https://github.com/yasirow/BandBar/blob/main/CHANGELOG.md#010---2026-10-14
