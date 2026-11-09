# Getting help

BandBar is a small volunteer project. Before asking, these usually answer the question:

1. **What the bar shows:** the status table and the FAQ in the [README](README.md#using-it) explain `—`, `denied`,
   `API?`, `NL?` and `WFP?`, the right-click menu and every setting.
2. **Known limitations:** [CHANGELOG.md](CHANGELOG.md) (each release's *Known limitations*) and
   [docs/DESIGN.md §11](docs/DESIGN.md#11-known-limitations).
3. **The log:** `bandbar.log`, next to `BandBar.exe` or in `%LocalAppData%\BandBar` (right-click the bar →
   *Open log folder*). For more detail, set `"logLevel": "debug"` in `settings.json`.

## Where to ask

| Topic | Where |
|---|---|
| Something does not work as described | [Bug report](https://github.com/yasirow/BandBar/issues/new?template=bug_report.yml) |
| An idea or a missing feature | [Feature request](https://github.com/yasirow/BandBar/issues/new?template=feature_request.yml) |
| A security problem | Privately, as described in [SECURITY.md](SECURITY.md) — never in a public issue |
| NetLimiter itself (licences, rules, the service, its own taskbar bar) | Locktime Software, the developer of NetLimiter: [netlimiter.com](https://www.netlimiter.com/) |

BandBar is not affiliated with Locktime Software; please do not contact them about BandBar.

## A good report

- BandBar and NetLimiter versions (hover the bar: the tooltip shows both), Windows version, taskbar position.
- What you did, what you expected and what happened.
- The relevant lines of `bandbar.log`. The log masks your user name, profile paths and computer name, but please
  read it before posting, and leave out anything else personal (screenshots included).
