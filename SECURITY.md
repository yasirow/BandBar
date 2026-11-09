# Security policy

## Supported versions

BandBar is in early development. Only the latest release receives fixes.

| Version | Supported |
|---|---|
| Latest release | ✅ |
| Older releases | ❌ |

## Reporting a vulnerability

Please **do not** open a public issue for security problems.

Use GitHub's private reporting instead: on this repository open **Security → Report a vulnerability**
([direct link](https://github.com/yasirow/BandBar/security/advisories/new)). Include the version, what an attacker
could do, and the steps to reproduce. You will get an answer as soon as possible, and credit in the release notes
if you want it.

Vulnerabilities in NetLimiter itself should be reported to its developer, Locktime Software.

## What BandBar is designed to guarantee

These are the properties a report would typically challenge; see [docs/DESIGN.md §7](docs/DESIGN.md#7-security-model)
for the full model.

- Runs with the user's normal rights; never requests or uses administrator rights.
- Opens no network connection and listens on no port. It talks only to the local NetLimiter service over its named
  pipe, with NetLimiter's read-only Monitor right, and only reads: it never changes NetLimiter's settings or
  features and never answers Blocker requests.
- Loads NetLimiter's API only from the installed NetLimiter, only an allow-list of eight assemblies, each verified
  with Authenticode against the expected publisher before it is loaded; exits if an API assembly is loaded from
  anywhere else.
- Installs no hooks and injects nothing into other processes, including Explorer and games, and never opens a
  handle to another process. The optional program list reads names from Windows' process list; the optional
  hotkey is a regular `RegisterHotKey` hotkey.
- Writes only to its own folder (or `%LocalAppData%\BandBar`), never to the registry or other programs' files.
- Starts another program only when the user asks for it (a menu item, or a click on the bar during a Blocker
  alert), and only fixed targets: its own settings file in the user's editor, its own folder in File Explorer,
  NetLimiter's own window (only if its signature is valid and from Locktime Software) and the project's Releases
  page in the browser; never elevated.
- Logs mask user names, profile paths and the computer name.

## Verifying a download

Every release publishes `SHA256SUMS.txt`. In PowerShell:

```powershell
Get-FileHash .\BandBar-Setup-0.6.0.exe -Algorithm SHA256
```

Compare the result with the line for that file in `SHA256SUMS.txt`. Releases are built by GitHub Actions from the
tagged commit; the build log is public on the release's workflow run.
