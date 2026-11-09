# Contributing to BandBar

Thanks for helping! BandBar is small on purpose, and it runs next to games, anti-cheat systems and a network
filter driver. Every change is judged first by whether it keeps the app lightweight, safe and invisible to other
programs. Please read [docs/DESIGN.md](docs/DESIGN.md) before writing code.

## Ground rules

These come from the design priorities and are not negotiable in a pull request:

1. **No new runtime dependency.** The release is one executable. Adding a NuGet package, DLL or tool needs an
   issue and agreement first.
2. **No hooks into other processes.** No `SetWindowsHookEx`, no in-context WinEvents, no injection, no
   `SetParent` into the taskbar, no reading another process' memory. WinEvents only with `WINEVENT_OUTOFCONTEXT`.
3. **Never disturb full-screen applications.** Anything visible must hide while a full-screen application is in
   front.
4. **No network.** No HTTP, no sockets, no telemetry, no update checks from the app.
5. **No administrator rights.** The manifest stays `asInvoker`; nothing elevates.
6. **Touch nothing that is not ours.** No writes to other programs' files, folders or registry keys; NetLimiter is
   only read.
7. **No polling where an event exists.** Timers are acceptable only for the once-per-second rate read (the API has
   no rate event) and for short, self-stopping bursts after an event.
8. **Win32 calls are allow-listed.** A new P/Invoke goes into `NativeMethods.cs` and into the table in
   [docs/DESIGN.md §7.2](docs/DESIGN.md#72-allowed-win32-calls) in the same pull request, with a reason.
9. **Privacy.** Logs, issues, screenshots and test reports must not contain user names, paths, computer names or
   anything else personal. Log messages go through the scrubber in `Log.cs`; never log exception messages from
   file-system or network calls without it.

## Building

Requirements: Windows 10 or 11 (x64) and the .NET SDK 10. NetLimiter is only needed to run the app, not to build it.

The repository keeps the SDK and all of its state inside the repository, so building does not change your user
profile. To install the SDK into `.dotnet\` without administrator rights and without touching `PATH`:

```powershell
New-Item -ItemType Directory -Force .dotnet | Out-Null
Invoke-WebRequest https://dot.net/v1/dotnet-install.ps1 -UseBasicParsing -OutFile .dotnet\dotnet-install.ps1
powershell -ExecutionPolicy Bypass -File .dotnet\dotnet-install.ps1 -Channel 10.0 -InstallDir .dotnet -NoPath
```

A system-wide .NET SDK 10 works too.

```powershell
# Build (Release by default; output: src\BandBar\bin\Release\net48\BandBar.exe)
powershell -ExecutionPolicy Bypass -File .\build.ps1

# Build and run the unit tests (tests\BandBar.Tests, MSTest)
powershell -ExecutionPolicy Bypass -File .\build.ps1 -Test

# Build, test and create release packages in artifacts\ (the installer needs Inno Setup 6)
powershell -ExecutionPolicy Bypass -File .\tools\package.ps1
```

The unit tests cover what can be checked without a desktop: speed formatting, reading and writing
`settings.json`, and the log scrubber. Everything that involves the taskbar is in the
[manual test plan](docs/TESTING.md).

## Making a change

1. Open an issue first for anything bigger than a small fix, so we can agree on the approach.
2. Keep the change focused; match the surrounding code style (`.editorconfig`).
3. If behaviour or a rule changes, update [docs/DESIGN.md](docs/DESIGN.md) in the same pull request.
4. Add or update unit tests for logic that does not need a desktop, and run `build.ps1 -Test`.
5. Run the relevant parts of the [manual test plan](docs/TESTING.md) and say which ones in the pull request.
6. Add an entry under `## [Unreleased]` in [CHANGELOG.md](CHANGELOG.md).
7. The build must pass with zero warnings, and all unit tests must pass.

## Versioning and releases

- The version lives in `<Version>` in `src/BandBar/BandBar.csproj` and is shown in the tooltip and the log.
- [Semantic Versioning](https://semver.org): below 1.0.0, each roadmap milestone is a minor release and fixes are
  patch releases.
- Every release has a [CHANGELOG.md](CHANGELOG.md) entry (Keep a Changelog sections: Added, Changed, Fixed,
  Security, Known limitations).
- To release: move the `Unreleased` entries under a new version heading, set `<Version>`, run the manual test plan,
  commit, then push a tag `vX.Y.Z`. The release workflow checks that the tag matches `<Version>`, builds the
  installer, the portable ZIP and checksums, and publishes them with the matching CHANGELOG section as release notes.

## Reporting bugs and security issues

- Bugs and ideas: [GitHub Issues](https://github.com/yasirow/BandBar/issues) (the forms ask for exactly what is
  needed).
- Security issues: please do **not** open a public issue; see [SECURITY.md](SECURITY.md).

By contributing you agree that your contributions are licensed under the [MIT License](LICENSE) and that you follow
the [Code of Conduct](CODE_OF_CONDUCT.md).
