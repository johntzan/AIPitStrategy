# CLAUDE.md

Operating notes for Claude (or any AI assistant) working in this repo.

## What this is

`PitStrategy` is an AI pit-strategy plugin for [SimHub](https://www.simhubdash.com/) targeting iRacing. It produces a single **PIT NOW / STAY OUT** decision card with confidence and reason, plus diagnostic properties (pit-loss decomposition, traffic projection, undercut/FCY alerts).

Phase 1 + 2 are code-complete and CI-green; Phase 3 (Monte Carlo + endurance polish) is deferred. See `docs/PLAN.md` for the full design and history.

## Repo layout

```
PitStrategy/
├── CLAUDE.md                              # ← this file
├── README.md                              # user-facing intro
├── PitStrategy.sln
├── Directory.Build.props                  # LangVersion=10, Nullable=enable
├── global.json                            # SDK 8.0.0 with rollForward: major
├── src/
│   ├── PitStrategy.Core/                  # netstandard2.0 — pure logic, fully unit-tested
│   │   ├── Inputs/                        # RaceState, RaceConfig, RivalState, etc.
│   │   ├── Outputs/                       # PitDecision, PitLossBreakdown, TrafficProjection, ...
│   │   ├── Fuel/, Pit/, PitWindow/        # math primitives
│   │   ├── Strategy/StrategyEngine.cs     # orchestrator with 6-rule decision tree
│   │   ├── Traffic/RejoinPredictor.cs
│   │   ├── Analysis/{UndercutDetector,FcyAnalyzer}.cs
│   │   └── Util/                          # IsExternalInit polyfill, RollingWindow, Statistics
│   └── PitStrategy.Plugin/                # net48 — Windows-only SimHub DLL (thin shim)
├── tests/PitStrategy.Core.Tests/          # xUnit on net8.0
├── dashboards/                            # SimHub Dash Studio templates
├── lib/simhub-sdk/                        # CI fetches SimHub.Plugins.dll + GameReaderCommon.dll here
├── docs/                                  # PLAN.md, INSTALL.md, IRACING-FIELDS.md
└── .github/workflows/ci.yml               # windows-latest, auto-fetches SDK, builds + packages artifacts
```

## Build & test

You only need `dotnet` for the Core library; the SimHub Plugin DLL targets `net48` and only builds on Windows (CI handles that).

```bash
# Restore + build the Core library + tests (works on macOS, Linux, Windows)
dotnet build src/PitStrategy.Core/PitStrategy.Core.csproj
dotnet test  tests/PitStrategy.Core.Tests/PitStrategy.Core.Tests.csproj
```

### Toolchain prerequisites — pick whichever applies

- **.NET 8 SDK is required to *run* the test host** (`net8.0` is the test target framework). On Windows or recent Linux, install via the official installer at <https://dotnet.microsoft.com/download/dotnet/8.0>. On macOS, `brew install dotnet` ships **only .NET 10**, which can build the project (thanks to `global.json`'s `rollForward: major`) but will fail to launch a `net8.0` test host with `You must install or update .NET to run this application`. Either install the .NET 8 SDK directly from microsoft.com, or run `dotnet test` against an installed .NET 8 SDK from another path. macOS users sometimes have a bundled .NET 8 from Unity, Unreal Engine, or Visual Studio for Mac — any of those work.
- **Plugin DLL build (Windows only)**: requires Visual Studio 2022 with the *.NET desktop development* workload (gives you the .NET Framework 4.8 targeting pack + WPF), plus `SimHub.Plugins.dll` and `GameReaderCommon.dll` either committed to `lib/simhub-sdk/` (gitignored) or copied from a local SimHub install (default `C:\Program Files (x86)\SimHub\`). The CI workflow handles this automatically; locally you'll need to drop the DLLs in yourself.

## Continuous integration

`.github/workflows/ci.yml`, single job on `windows-latest`. Triggers: push to `main`, PRs, tags `v*`, `workflow_dispatch`.

Pipeline:
1. Resolve the latest [SHWotever/SimHub release](https://github.com/SHWotever/SimHub/releases) zip via the GitHub API (authenticated with `secrets.GITHUB_TOKEN` to avoid the 60/hr anonymous limit).
2. Cache the extracted SDK DLLs by SimHub release tag (`actions/cache@v4`).
3. Cache miss path: download the 217 MB zip, `7z x` it to extract `SimHubSetup_<v>.exe`, run it with `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /NOICONS`, and copy `SimHub.Plugins.dll` + `GameReaderCommon.dll` from the install dir into `lib/simhub-sdk/`.
4. `dotnet restore` → `dotnet build` Core → `dotnet test` Core → `dotnet build` Plugin (net48).
5. Upload artifacts (every push):
   - `PitStrategy-dev-<sha>-dll` — bare DLL
   - `PitStrategy-dev-<sha>-package` — DLL + dashboard template + INSTALL.md + README
   - `simhub-sdk-dlls` — vendored SDK DLLs (1-day retention; useful for local API decompilation, see below)
   - `test-results` — xUnit `.trx`
6. Tag pushes (`v*`) additionally zip the package and create a GitHub Release with auto-generated notes via `softprops/action-gh-release@v2`.

Cold-cache run is ~4 min (silent install dominates); warm-cache run is ~2 min.

## Verified API surfaces (read this before changing plugin code)

`PitStrategy.Plugin` references SimHub's plugin SDK (`SimHub.Plugins.dll`) and GameReaderCommon. Both are proprietary binaries with no public C# header. **Don't guess the API** — decompile the real DLLs.

### How to inspect locally

```bash
# Once: install ilspycmd as a global tool
dotnet tool install -g ilspycmd

# Pull the SDK DLLs from any recent CI run
gh run list --repo johntzan/AIPitStrategy --limit 5
gh run download <run-id> -n simhub-sdk-dlls -D scratch/simhub-sdk --repo johntzan/AIPitStrategy

# Decompile (set DOTNET_ROOT to your dotnet install root if ilspycmd complains)
ilspycmd scratch/simhub-sdk/SimHub.Plugins.dll --type SimHub.Plugins.PluginManager
ilspycmd scratch/simhub-sdk/GameReaderCommon.dll --type GameReaderCommon.StatusDataBase
```

`scratch/` is gitignored.

### Most-relevant types (verified against `SimHub.Plugins.dll` v9.11.11)

```csharp
// SimHub.Plugins.PluginManager
public void AddProperty(string name, Type pluginType, Type propertyType, string description = null);
public void AddProperty<T>(string name, Type pluginType, T value, string description = null);
public void SetPropertyValue(string name, Type pluginType, object value);
public void AddAction(string actionName, Type pluginType,
                      Action<PluginManager, string> actionStart,
                      Action<PluginManager, string> actionEnd = null);
public EventTrigger AddEvent(string eventName, Type pluginType);
public void AttachDelegate<T>(string name, Type pluginType, Func<T> valueProvider, ...);

// Extension methods on IPlugin (in SimHub.Plugins)
public static void SaveCommonSettings<T>(this IPlugin plugin, string settingsName, T settingsObject);
public static T    ReadCommonSettings<T>(this IPlugin plugin, string settingsName, Func<T> defaultValueFactory);

// Interfaces
public interface IPlugin            { PluginManager PluginManager { set; } void Init(...); void End(...); }
public interface IDataPlugin        : IPlugin { void DataUpdate(PluginManager pm, ref GameData data); }
public interface IWPFSettings       { Control GetWPFSettingsControl(PluginManager pm); }
public interface IWPFSettingsV2     : IWPFSettings { ImageSource PictureIcon { get; } string LeftMenuTitle { get; } }
```

### `GameReaderCommon.StatusDataBase` (the type behind `data.NewData`) — gotchas

| Field | Type | Note |
|---|---|---|
| `IsInPit`, `IsInPitLane` | **`int` (0/1)** | *not bool* — convert with `> 0` |
| `Flag_Yellow`, `Flag_Green`, `Flag_Blue`, `Flag_White`, `Flag_Checkered`, `Flag_Black`, `Flag_Orange` | `int` (0/1) | same |
| `LapDistPct` | **not exposed directly** | derive from `Opponents.First(o => o.IsPlayer).TrackPositionPercent` |
| `PositionInClass` | **not on `StatusDataBase`** | read from the player's `Opponent` row |
| `PitCount` | **not exposed** | track manually via `IsInPit` 0→1 transitions |
| `Position`, `CurrentLap`, `CompletedLaps`, `RemainingLaps`, `TotalLaps` | `int` | direct |
| `Fuel`, `MaxFuel`, `FuelPercent` | `double` | litres |
| `LastLapTime`, `BestLapTime`, `CurrentLapTime`, `SessionTimeLeft` | `TimeSpan` | |
| `Opponents` | `List<Opponent>` | includes the player; filter by `IsPlayer` |
| `OpponentsAheadOnTrack`, `OpponentsBehindOnTrack`, `OpponentsPlayerClass` | `List<Opponent>` | useful for class filtering |

### `GameReaderCommon.Opponent` — gotchas

| Field | Type | Note |
|---|---|---|
| `IsPlayer` | `bool` | use to find the player's row |
| `Position`, `PositionInClass`, `LivePosition` | `int` | |
| `CarClass`, `CarClassID` | `string` | parse to int when needed |
| `CurrentLap` | `int?` | nullable |
| `TrackPositionPercent` | `double?` | nullable; 0.0–1.0 |
| `LastLapTime`, `BestLapTime` | `TimeSpan` | |
| `GaptoPlayer`, `GaptoLeader` | **`double?` (seconds)** | *not TimeSpan* — wrap with `TimeSpan.FromSeconds(x ?? 0)` |
| `IsCarInPit`, `IsCarInPitLane` | `bool` | |
| `LapsToPlayer`, `LapsToLeader` | `int?` | for lap-down detection |

### Things that are NOT in the SDK we vendor

- **`SimHub.Logging.Current.Error/Warn/Info`** — the `Logging` class isn't in `SimHub.Plugins.dll` on this version. It probably lives in `WoteverCommon.dll`. The plugin currently swallows exceptions silently; wire logging back up by also extracting `WoteverCommon.dll` in the CI fetch step.
- **iRacing-specific raw types** (`DataSampleEx`, `Telemetry.SessionFlags`, `CarIdxLapDistPct[]`, etc.) — `data.NewData.GetRawDataObject()` exists but the concrete type comes from whichever iRSDK wrapper SimHub ships internally. Use `dynamic` for runtime access. The `Microsoft.CSharp` reference is already in the plugin csproj for this purpose.

## Toolchain quirks (don't re-learn these)

1. **PowerShell on `windows-latest`** rejects C#-style digit separators: `50_000_000` is a parser error. Use `50000000`.
2. **The SimHub release zip is NOT a portable layout** — it contains a single `SimHubSetup_<version>.exe` (Inno Setup 6.4.3 installer). `Expand-Archive` and `7z x` both extract just the EXE. The actual SDK DLLs only appear after running the installer with `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /NOICONS`.
3. **GitHub API anonymous rate limit (60/hr) is shared across the Azure runner IP pool** — anonymous `Invoke-RestMethod` to `api.github.com` will rate-limit fast. Pass `${{ secrets.GITHUB_TOKEN }}` as `Authorization: Bearer …` (1,000/hr per repo).
4. **`Microsoft.CSharp` reference is required** for the C# `dynamic` keyword on net48. SDK-style csprojs don't include it by default.
5. **`brew install innoextract`** ships v1.9, which only supports Inno Setup ≤ 6.0.5; SimHub uses 6.4.3. Don't try to extract on macOS — use the CI-uploaded `simhub-sdk-dlls` artifact.
6. **`brew install dotnet`** on macOS installs **only .NET 10**, no .NET 8 runtime. Build works (with `rollForward: major`); test runs require a real .NET 8 SDK.
7. **The plugin csproj has `<NoBuild>true</NoBuild>` on non-Windows** so `dotnet build` of the solution doesn't trip on `net48`. On macOS you can build the Core library directly without touching the plugin.

## Open TODOs (pickable)

- **Tire wear telemetry** (`LFwearL/M/R` etc.) requires raw `DataSampleEx` access. Currently `TireWearSnapshot.Unknown` is hardcoded in `IRacingMapper`.
- **FCY detection** beyond `Flag_Yellow > 0` — read the raw iRacing `SessionFlags` bitmask (bit `0x4000` = caution-waving) via `data.NewData.GetRawDataObject() as dynamic`.
- **`PitCount` / `CompletedPitStops`** — track manually in the plugin by detecting `IsInPit` 0→1 transitions across ticks.
- **Pit-lane geometry** — currently a 350 m / 60 km/h default with override settings. Should read `WeekendInfo.TrackPitLaneTotalLength` and `TrackPitSpeedLimit` from the iRacing SessionInfo YAML.
- **Logging integration** — find which DLL exposes `SimHub.Logging` (likely `WoteverCommon.dll`), vendor it alongside the SDK fetch, wire `Logging.Current.Info(...)` into the plugin's catch blocks.
- **Phase 3** — Monte Carlo simulator at `src/PitStrategy.Core/Simulation/`, MC-augmented `Confidence` on the headline `PitDecision`, driver-swap detection, weather windows.
- **Validate the plugin loads in SimHub end-to-end** — Phase 1+2 is code-complete and CI-green, but the DLL hasn't been deployed and exercised in iRacing yet.

## Conventions

- **Don't guess at SimHub or iRacing APIs.** Decompile the actual DLLs (`scratch/simhub-sdk/`) with `ilspycmd` and cite signatures in commit messages.
- **Don't rewrite published history.** `main` is the default branch; force-pushing rewrites would invalidate any downstream clones, including CI-built artifacts. Make new commits forward-only.
- **Commit messages**: Concise subject line; body explains the *why* if non-obvious. End with the standard footer:
  ```
  Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
  ```
- **Tests**: Mac users run `dotnet test` against the Core library only. The Plugin shim is exercised in CI on `windows-latest`. Add new tests as plain C# (no JSON fixtures yet); see `tests/PitStrategy.Core.Tests/Strategy/StrategyEngineTests.cs` for the canonical scenario-test pattern.

## Pointers

- **`docs/PLAN.md`** — full design doc, decision history, phased delivery plan, session log.
- **`docs/INSTALL.md`** — end-user install guide (drop the DLL into SimHub, build a dashboard, troubleshoot).
- **`docs/IRACING-FIELDS.md`** — load-bearing list of iRacing telemetry fields with mapping notes.
- **`README.md`** — user-facing intro and property reference.
