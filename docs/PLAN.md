# AI Pit Strategy Engine — SimHub Plugin for iRacing

A design document for a SimHub plugin that runs a hybrid rule-based + Monte Carlo pit-strategy engine on iRacing telemetry, plus a SimHub dashboard that visualizes its recommendations.

---

## Status (current)

### What's shipping

- **Phase 1 + Phase 2 implemented and CI-green.** Phase 3 (Monte Carlo, endurance polish) deferred.
- **38 passing unit + golden tests** (xUnit on net48; runs in CI on `windows-latest`).
- **Single-DLL deployment** — pure-logic types live in the same assembly as the SimHub shim, so the install is a single `PitStrategy.dll` drop into `C:\Program Files (x86)\SimHub\`. (v0.1.0 shipped them as separate DLLs and broke at load time with `FileNotFoundException` on `PitStrategy.Core` — folded into the plugin in v0.1.1.)
- **Plugin DLL builds cleanly on `windows-latest`** with the actual SimHub SDK fetched at CI time — no manual DLL vendoring required.
- **Downloadable artifacts on every push to `main`** and every manual `workflow_dispatch`:
  - `PitStrategy-dev-<sha>-dll` — bare DLL
  - `PitStrategy-dev-<sha>-package` — DLL + dashboard template + `INSTALL.md` + `README.md`
  - `simhub-sdk-dlls` — vendored `SimHub.Plugins.dll` + `GameReaderCommon.dll` (1-day retention, kept for local API verification)
  - `test-results` — xUnit `.trx`
- **Tag pushes (`v*`)** additionally produce `PitStrategy-<tag>-windows.zip` and a real GitHub Release with auto-generated notes.

### Quick-start commands

```bash
# Build + test (Windows only — net48)
dotnet build PitStrategy.sln -c Release
dotnet test  tests/PitStrategy.Plugin.Tests/PitStrategy.Plugin.Tests.csproj -c Release

# macOS / Linux — restore only (build/test would need .NET Framework)
dotnet restore PitStrategy.sln

# Watch latest CI run
gh run list --repo johntzan/AIPitStrategy --limit 1
gh run watch <run-id> --repo johntzan/AIPitStrategy --exit-status

# Pull latest plugin DLL from a CI run
gh run download <run-id> -n "PitStrategy-dev-<sha>-dll" --repo johntzan/AIPitStrategy

# Decompile SimHub API to verify a signature before changing plugin code
dotnet tool install -g ilspycmd                                  # once
gh run download <run-id> -n simhub-sdk-dlls -D scratch/simhub-sdk --repo johntzan/AIPitStrategy
ilspycmd scratch/simhub-sdk/SimHub.Plugins.dll --type SimHub.Plugins.PluginManager
```

### Verified API surfaces (use these — don't guess)

Decompiled from `SimHub.Plugins.dll` (9.0 MB, version that ships with SimHub 9.11.11) and `GameReaderCommon.dll` via `ilspycmd`. Pull the DLLs locally with the CI artifact download command above.

**`SimHub.Plugins.PluginManager`** — relevant methods:

```csharp
public void AddProperty(string name, Type pluginType, Type propertyType, string description = null);
public void AddProperty<T>(string name, Type pluginType, T value, string description = null);
public void SetPropertyValue(string name, Type pluginType, object value);
public void AddAction(string actionName, Type pluginType,
                      Action<PluginManager, string> actionStart,
                      Action<PluginManager, string> actionEnd = null);
public EventTrigger AddEvent(string eventName, Type pluginType);
public void AttachDelegate<T>(string name, Type pluginType, Func<T> valueProvider,
                              string description = null, bool hidden = false,
                              SupportStatus supportStatus = SupportStatus.Supported);
```

**Key plugin extension methods**:

```csharp
// In SimHub.Plugins, on IPlugin:
public static void SaveCommonSettings<T>(this IPlugin plugin, string settingsName, T settingsObject);
public static T    ReadCommonSettings<T>(this IPlugin plugin, string settingsName, Func<T> defaultValueFactory);
// On IWPFSettings (the parent interface):
public static BitmapSource ToIcon(this IWPFSettings settings, Bitmap source); // takes Bitmap, NOT byte[]
```

**Interfaces** (verified):

```csharp
public interface IPlugin {
    PluginManager PluginManager { set; }
    void Init(PluginManager pluginManager);
    void End(PluginManager pluginManager);
}
public interface IDataPlugin : IPlugin {
    void DataUpdate(PluginManager pluginManager, ref GameData data); // 60 Hz
}
public interface IWPFSettings {
    Control GetWPFSettingsControl(PluginManager pluginManager);
}
public interface IWPFSettingsV2 : IWPFSettings {
    ImageSource PictureIcon { get; }   // returning null is fine — keeps default icon
    string LeftMenuTitle { get; }
}
```

**`GameReaderCommon.StatusDataBase`** (the type behind `data.NewData`) — most-relevant fields:

| Field | Type | Notes |
|---|---|---|
| `CurrentLap`, `CompletedLaps`, `RemainingLaps`, `TotalLaps` | `int` | direct reads, no nullables |
| `LapDistPct` | **NOT DIRECTLY EXPOSED** | derive from `Opponents.Where(IsPlayer).TrackPositionPercent` |
| `Fuel`, `MaxFuel`, `FuelPercent` | `double` | litres |
| `LastLapTime`, `BestLapTime`, `CurrentLapTime` | `TimeSpan` | |
| `IsInPit`, `IsInPitLane` | **`int` (0/1)** — *not bool* | use `> 0` to convert |
| `Position` | `int` | overall position |
| `PositionInClass` | **NOT ON StatusDataBase** | read from `Opponents.Where(IsPlayer).PositionInClass` |
| `PitCount` | **NOT EXPOSED** | track manually via `IsInPit` transitions; default 0 for Phase 2 |
| `Flag_Yellow`, `Flag_Green`, `Flag_Blue`, `Flag_White`, `Flag_Checkered`, `Flag_Black`, `Flag_Orange` | `int` (0/1) | |
| `SessionTimeLeft` | `TimeSpan` | |
| `SessionTypeName` | `string` | "Race", "Practice", etc. |
| `AirTemperature` | `double` | TrackTemperature isn't normalized — needs raw frame |
| `Opponents` | `List<Opponent>` | includes the player; filter by `IsPlayer` |
| `OpponentsAheadOnTrack`, `OpponentsBehindOnTrack`, `OpponentsPlayerClass` | `List<Opponent>` | useful for class filtering |

**`GameReaderCommon.Opponent`**:

| Field | Type | Notes |
|---|---|---|
| `IsPlayer` | `bool` | use to find player's row |
| `Position`, `PositionInClass`, `LivePosition` | `int` | |
| `CarClass`, `CarClassID` | `string` | parse to int if needed |
| `CurrentLap` | `int?` | nullable |
| `TrackPositionPercent` | `double?` | nullable; 0.0–1.0 |
| `LastLapTime`, `BestLapTime` | `TimeSpan` | |
| `GaptoPlayer`, `GaptoLeader` | `double?` (seconds) | **not TimeSpan** — wrap with `TimeSpan.FromSeconds(x ?? 0)` |
| `IsCarInPit`, `IsCarInPitLane` | `bool` | |
| `LapsToPlayer`, `LapsToLeader` | `int?` | for lap-down detection |

**Things that are NOT in `SimHub.Plugins.dll`**:

- `SimHub.Logging.Current.Error/Warn/Info` — the `Logging` class isn't in this DLL on this version. Likely lives in `WoteverCommon.dll` (which is referenced via `using WoteverCommon;` inside `PluginManager`). For now the plugin swallows exceptions silently. **TODO**: pull `WoteverCommon.dll` as a 3rd vendored ref and wire logging back.
- `data.NewData.GetRawDataObject()` — exists per docs but the iRacing-specific types (`DataSampleEx`, `Telemetry.SessionFlags`, `CarIdxLapDistPct[]`, etc.) require knowing which iRSDK wrapper SimHub ships internally. Use `dynamic` for runtime access (the `Microsoft.CSharp` reference is already added to the plugin csproj).

### Toolchain quirks discovered (don't re-learn these)

1. **PowerShell on `windows-latest`** doesn't accept C#-style digit separators. `50_000_000` → parser error. Use `50000000`.
2. **The SimHub release zip is NOT a portable layout** — it contains a single `SimHubSetup_<version>.exe` (Inno Setup 6.4.3 installer). `Expand-Archive` and `7z x` both extract just the EXE. The actual SDK DLLs only appear after running the installer with `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /NOICONS`. The `windows-latest` runner has admin rights, so the install succeeds at `C:\Program Files (x86)\SimHub\`.
3. **GitHub API anonymous rate limit (60/hr) is shared across the Azure GitHub-runner IP pool** — anonymous `Invoke-RestMethod` to `api.github.com` will get rate-limited fast on a repo that runs CI a lot. Always pass `${{ secrets.GITHUB_TOKEN }}` as an `Authorization: Bearer …` header (1,000/hr per repo).
4. **`Microsoft.CSharp` reference is required for the C# `dynamic` keyword on net48** — SDK-style csprojs don't include it by default like .NET Core does. Add `<Reference Include="Microsoft.CSharp" />`.
5. **`brew install innoextract` ships v1.9** which only supports Inno Setup ≤ 6.0.5; SimHub uses 6.4.3. Don't try to extract on macOS — use the CI-uploaded `simhub-sdk-dlls` artifact for local API inspection.
6. **Whole project is now Windows-only at build time** (net48 + WPF). macOS/Linux can `dotnet restore` to validate the package graph; both csprojs set `<NoBuild>true</NoBuild>` on non-Windows so `dotnet build` of the solution is a no-op there.

### Known TODOs (Phase 3 + cleanup)

- **Tire wear telemetry** (`LFwearL/M/R` etc.) requires raw `DataSampleEx` access. Currently `TireWearSnapshot.Unknown` is hardcoded in the mapper.
- **FCY detection** — Phase 2 treats `Flag_Yellow > 0` as FCY heuristically. Real FCY needs the raw iRacing `SessionFlags` bitmask (`0x4000` for caution-waving). Read it via `data.NewData.GetRawDataObject() as dynamic`.
- **`PitCount` / `CompletedPitStops`** — the SimHub-normalized layer doesn't expose this. Track it in the plugin by detecting `IsInPit` 0→1 transitions across ticks.
- **Pit-lane length** — currently a 350 m default with `OverridePitLaneLength` setting. Should read `WeekendInfo.TrackPitLaneTotalLength` from the iRacing SessionInfo YAML.
- **Pit-lane speed limit** — currently hardcoded 60 km/h. Should read `WeekendInfo.TrackPitSpeedLimit`.
- **`SimHub.Logging`** — find which DLL exposes it (likely `WoteverCommon.dll`), vendor that DLL alongside the SDK fetch, and wire `Logging.Current.Info(...)` into the plugin's catch blocks.
- **Phase 3** — Monte Carlo simulator (`src/PitStrategy.Plugin/Simulation/`), endurance refinements (driver-swap detection, multi-class rivals, weather windows), MC-augmented `Confidence` on the headline `PitDecision`.

### How to pick this up in a fresh session

1. Read this `## Status` section first — it has the verified facts that prevent re-guessing.
2. Read `git log --oneline` to see recent commits.
3. Check `gh run list --repo johntzan/AIPitStrategy --limit 5` to see the last few CI results.
4. If you need to verify a SimHub or iRacing API before changing plugin code, decompile the actual DLLs:
   - Pull the latest CI's SDK artifact: `gh run download <latest-run-id> -n simhub-sdk-dlls -D scratch/simhub-sdk --repo johntzan/AIPitStrategy`
   - Decompile what you need: `ilspycmd scratch/simhub-sdk/SimHub.Plugins.dll --type SimHub.Plugins.<X>`
5. The Plugin has 38 tests; they run on `windows-latest` in CI (or on a real Windows machine). macOS dev is restore-only.
6. Don't `git filter-branch` or force-push to rewrite history — make new commits forward-only.

### CI iteration log

Sequence of CI iterations during initial bring-up — kept as a record of what was tried and why each fix landed:

| # | What was tried | What broke | Lesson |
|---|---|---|---|
| 1 | Initial scaffold + plan-driven Phase 1+2 implementation. CI gated plugin build on vendored SDK DLLs (which weren't committed). | Plugin step skipped — no SDK DLLs in repo. | Ship plugin DLL; CI must fetch the SDK itself. |
| 2 | Auto-fetch SimHub release via GitHub API + cache; `Expand-Archive` to unpack the zip; recursive find for `*.dll`. | "SimHub.Plugins.dll not found" — `Expand-Archive` extracted nothing visible from the zip in 0.5 s. | The zip isn't a portable layout. |
| 3 | Diagnostic logging: zip magic bytes, top-level listing, `*.dll` and `*.exe` recursive counts. | API rate-limit (anonymous 60/hr exhausted on shared runner IP). | Authenticate the GitHub API call with `secrets.GITHUB_TOKEN`. |
| 4 | Add `Authorization: Bearer ${{ secrets.GITHUB_TOKEN }}` to the `Invoke-RestMethod` call. | PowerShell parser error on `50_000_000` digit-separator literal. | Drop digit separators in pwsh. |
| 5 | `50_000_000` → `50000000`. | `Expand-Archive` succeeded "silently" — extracted a nonexistent file. | Switch to 7z (preinstalled on `windows-latest`). |
| 6 | `7z x simhub.zip`. | Same silent-success / nothing-extracted. Logged 7z's own list output. | The zip contains exactly one entry: `SimHubSetup_<v>.exe` (217 MB Inno Setup installer). |
| 7 | Run the installer with `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /NOICONS` + `Start-Process -Wait`, copy `SimHub.Plugins.dll` and `GameReaderCommon.dll` from `C:\Program Files (x86)\SimHub\` to `lib/simhub-sdk/`. | Installer worked! Plugin code itself failed to compile with **14 errors**: missing `Microsoft.CSharp` for `dynamic`; `byte[]` → `Bitmap` for `ToIcon`; `Action<PM, string>` not `Action<PM, object>`; `SimHub.Logging` namespace missing; `IsInPit`/`IsInPitLane` are `int`; `StatusDataBase` doesn't have `PositionInClass`/`PitCount`. | Stop guessing plugin API — verify against the actual DLL. |
| 8 | Add a step that uploads `lib/simhub-sdk/` as a 1-day artifact regardless of overall job status (`if: always()`). | (used to ground next fix) | Now the SDK DLLs can be decompiled locally. |
| 9 | Pull SDK DLLs locally via `gh run download`, install `ilspycmd`, decompile both DLLs. Rewrite `PitStrategyPlugin.cs` and `IRacingMapper.cs` with verified types. Add `<Reference Include="Microsoft.CSharp" />`. | ✅ All steps green. | Reading the actual binaries beats reading docs / examples. |

---

## TL;DR

- **Headline output**: a single in-the-moment **"PIT NOW" / "STAY OUT"** notification with a confidence value and a one-line reason. That is the only thing the driver should need to glance at. Everything else on the dashboard is diagnostic / for testing.
- **Decision factors** (priority order): fuel feasibility → total pit-loss time (decomposed: pit-lane travel + fuel + tires + entry/exit) → traffic vs. clean air on rejoin → undercut/overcut → FCY opportunity.
- **Race-format priority**: most iRacing races are 1-stop sprints, so the engine's first-class case is *"there is one mandatory or near-mandatory pit window — when do I take it?"*. Multi-stop is supported as diagnostic comparison data and as a Phase 2/3 Monte Carlo sweep, not as the headline.
- **Engine kind**: rule-based deterministic core + Monte Carlo race simulator (in-process C#, no LLM, no external service).
- **Bootstrap**: official SimHub `PluginSdkDemo` template (in `C:\Program Files (x86)\SimHub\PluginSdk\`).
- **Dev environment**: Windows-only for the plugin DLL build. Visual Studio Community 2022 + .NET Framework 4.8 Dev Pack + .NET 8 SDK (for the Core library and tests). Core library + tests build on any .NET-supporting platform.
- **Novelty**: there are no existing SimHub plugins doing rejoin/clean-air projection or Monte Carlo strategy. Closest reference is `KLPlugins.RaceEngineer` (deterministic fuel + tire-pressure only).

---

## 1. Solution structure

```
PitStrategy/
├── PitStrategy.sln
├── Directory.Build.props
├── src/
│   └── PitStrategy.Plugin/                  # net48 — single SimHub DLL
│       ├── Inputs/                          # RaceState, RaceConfig, RivalState, TireWearSnapshot, WeatherSnapshot
│       ├── Outputs/                         # PitDecision, PitLossBreakdown, TrafficProjection, ComparedStrategy, UndercutAlert, FcyOpportunity
│       ├── Fuel/FuelTracker.cs              # rolling 5-lap avg from FuelLevel deltas
│       ├── Pit/PitLossModel.cs              # first-class total-pit-loss decomposition
│       ├── Traffic/RejoinPredictor.cs       # where you'll be after pit; clean air vs traffic
│       ├── PitWindow/PitWindowCalculator.cs # earliest/latest legal lap given fuel
│       ├── Strategy/StrategyEngine.cs       # orchestrator, returns PitDecision
│       ├── Simulation/                      # MonteCarloSimulator (Phase 3)
│       ├── Analysis/                        # UndercutDetector, FcyAnalyzer
│       ├── Util/                            # RollingWindow<T>, seedable IRandomSource (PCG/xoshiro)
│       ├── PitStrategyPlugin.cs             # IPlugin, IDataPlugin, IWPFSettingsV2
│       ├── IRacingMapper.cs                 # DataSampleEx + GameData → RaceState
│       ├── PropertyPublisher.cs             # owns AddProperty/SetPropertyValue
│       └── Settings/{PluginSettings.cs, SettingsView.xaml(.cs)}
│
├── tests/
│   └── PitStrategy.Plugin.Tests/            # xUnit on net48 — Windows-only
│       ├── Fuel/, Strategy/, Pit/, Traffic/
│       └── Fixtures/                        # JSON race scenarios (golden tests; not yet populated)
│
├── dashboards/
│   └── PitStrategy.simhubdash               # JSON dashboard exported from Dash Studio (template only)
│
├── lib/simhub-sdk/                          # vendored SimHub.Plugins.dll, GameReaderCommon.dll
├── .github/workflows/ci.yml                 # windows-latest, build + test + package
└── docs/{ARCHITECTURE.md, INSTALL.md, IRACING-FIELDS.md, PLAN.md}
```

History: pre-v0.1.1 split the strategy logic into a separate `PitStrategy.Core` (`netstandard2.0`) project so unit tests could run cross-platform on `net8.0`. That made the install a multi-DLL drop, which broke at SimHub load time when only `PitStrategy.dll` was redistributed. v0.1.1 folded everything into the Plugin assembly to ship as a single file. Tests now target `net48` and run on Windows only, in CI. Pure-logic types still live under `PitStrategy.Core.*` namespaces.

---

## 2. Engine API — primary output is a `PitDecision`

```csharp
// ───── Headline output ─────
public enum PitDecisionKind {
    Unknown,         // insufficient data (e.g. no green laps yet)
    PitNow,          // pit this lap, end of current lap
    PitNextLap,      // wait one lap (clean-air or undercut window opens)
    StayOut,         // do not pit
    SaveFuel,        // lift-and-coast; pit window not optimal yet
    PitForFuelOnly,  // forced — running out of fuel before any other window
}

public sealed record PitDecision(
    PitDecisionKind Kind,
    double Confidence,                     // 0.0–1.0
    int? RecommendedPitLap,                // null if Kind == StayOut
    PitLossBreakdown LossBreakdown,        // total + components in seconds
    TrafficProjection TrafficAfterPit,     // who's near you on rejoin
    UndercutAlert? Undercut,
    FcyOpportunity? Fcy,
    string PrimaryReason,                  // one-line, dashboard-friendly
    IReadOnlyList<string> SecondaryFactors,
    TimeSpan ComputeTime);

public sealed record PitLossBreakdown(
    double PitLaneTravelSeconds,           // pitLaneLength / pitLaneSpeedLimit
    double FuelServiceSeconds,             // 0 if no refuel armed
    double TireServiceSeconds,             // 0 if no tire change armed
    double EntryExitDeltaSeconds,          // slow-zone deltas at pit-in / pit-exit
    double TotalSeconds)                   // travel + max(fuel, tire) + entryExit  (services run in parallel)
{
    public double ServicesParallelSeconds => Math.Max(FuelServiceSeconds, TireServiceSeconds);
}

public sealed record TrafficProjection(
    int RejoinPosition,                    // overall position when exiting pit
    int RejoinClassPosition,
    double GapToCarAheadSeconds,           // closest rival ahead on rejoin
    double GapToCarBehindSeconds,          // closest rival behind on rejoin
    bool IsCleanAir,                       // both gaps ≥ Heuristics.CleanAirThresholdSeconds
    IReadOnlyList<RivalRejoinState> NearbyRivals,  // top 4 within ±5s
    int CleanAirLapIfWait);                // earliest future lap where pitting yields clean air

public sealed record RivalRejoinState(int CarIdx, int Position, double GapSeconds, bool IsAhead, bool WillBePitting);

// ───── Inputs ─────
public sealed record RaceState( /* current lap, fuel, lap times, rivals, weather, FCY, etc. */ );
public sealed record RaceConfig(
    RaceLength Length,
    double TankCapacityLiters, double FuelSafetyMarginLaps,
    PitLossConfig PitLoss,
    bool TireChangesAllowed, TireDegradationModel TireModel,
    StrategyHeuristics Heuristics);

public sealed record PitLossConfig(
    double PitLaneLengthMeters,            // from WeekendInfo.TrackPitLaneTotalLength when available
    double PitLaneSpeedLimitKph,           // from WeekendInfo.TrackPitSpeedLimit
    double FuelFlowLitersPerSecond,        // car-class specific
    double TireChangeServiceSeconds,       // car-class specific (typically 12–16s)
    double PitEntryDeltaSeconds,           // sustained slow-down before pit lane
    double PitExitDeltaSeconds);

// ───── Engine ─────
public sealed class StrategyEngine {
    PitDecision Decide(RaceState state, RaceConfig config);                                    // fast, deterministic, 1 Hz safe
    Task<PitDecision> DecideWithSimulationAsync(                                               // heavy MC-augmented, on-demand
        RaceState state, RaceConfig config, IProgress<SimProgress>? p, CancellationToken ct);
}
```

**Decision tree (Phase 1 + 2)** — the engine evaluates conditions in this order and returns the first match:

1. **Forced fuel pit**: `LapsToEmpty - SafetyMargin <= 1` → `PitForFuelOnly` (confidence 1.0).
2. **Pit window not yet open**: `LapsToEmpty > LapsRemaining + 1` and no other reason to pit → `StayOut` (confidence high).
3. **FCY opportunity**: `IsUnderFcy && PitsOpen && fuel allows shorter stint` → `PitNow` (confidence ~0.85, reason: "Free pit under FCY saves ~Xs").
4. **Undercut window**: rival 1 ahead is within `UndercutThresholdSeconds` and ≥3 laps from their pit window → `PitNow` (confidence ~0.7, reason: "Undercut #14 — gain ~Ys").
5. **Clean-air optimization**: if pitting *this* lap rejoins in traffic but waiting K laps rejoins in clean air, return `PitNextLap` or `StayOut` (confidence ~0.6, reason: "Wait 2 laps — rejoin in clean air").
6. **Default optimal**: pit at `RecommendedPitLap` from `PitWindowCalculator` (centre of the legal window).

`FuelTracker` uses **`FuelLevel` deltas at lap boundaries**, never iRacing's `FuelUsePerHour` (too noisy). Drops yellow-flag laps from the rolling average; rejects outliers by Z-score.

`PitLossModel.Compute(PitLossConfig, ServiceFlags)` returns the `PitLossBreakdown` — fully unit-testable.

`RejoinPredictor.Project(state, config, candidatePitLap)` returns `TrafficProjection`:
- For each rival, project `lapDistPct` forward by `(playerPitDelta - 0)` worth of time at their pace.
- After your pit-out, your `lapDistPct` = end-of-pit-lane mark; your accumulated time = base + `TotalPitLossSeconds`.
- Sort rivals by projected gap; closest-ahead and closest-behind define traffic.
- `IsCleanAir = min(|gapAhead|, |gapBehind|) >= CleanAirThresholdSeconds` (default 3 s).
- `CleanAirLapIfWait` scans the next N laps (3–5) to find the earliest lap where projection produces clean air.

Edge cases the API handles: zero green laps yet (Kind = Unknown), mid-pit-stop sample (suppressed), driver swap (`FuelTracker.Reset()` exposed), lap- vs. timed-plus-one-lap, FCY mid-lap, replay/time-acceleration (use `data.NewData.SimulationTime` deltas), pit lane length missing from `WeekendInfo` (fall back to setting override).

---

## 3. SimHub plugin (the Windows DLL)

`PitStrategyPlugin` implements `IPlugin`, `IDataPlugin`, `IWPFSettingsV2`.

`DataUpdate` flow at 60 Hz:
1. Bail if game isn't iRacing.
2. Cast `data.NewData?.GetRawDataObject() as DataSampleEx`; bail if null.
3. `IRacingMapper.Map(data, raw, sessionInfo) → RaceState`.
4. `FuelTracker.OnTick(state)` (detects lap boundaries internally).
5. Publish *fast* properties (current fuel, last lap time, on-pit-road) every tick.
6. **Throttle**: run `StrategyEngine.Decide` once per second; publish *decision* properties.
7. Monte Carlo runs only via the `RunSimulation` action, never in `DataUpdate`.

**Plugin properties exposed** (for dashboard NCalc binding), naming convention `PitStrategy.<Group>.<Field>`:

| Group | Properties |
|---|---|
| **`Decision.*` (headline)** | `Kind` (string), `Confidence` (double), `PrimaryReason` (string), `RecommendedPitLap` (int) |
| **`PitLoss.*`** | `Total`, `PitLaneTravel`, `FuelService`, `TireService`, `EntryExitDelta` (all double seconds) |
| **`Traffic.*`** | `IsCleanAir` (bool), `RejoinPosition` (int), `GapAhead` (double), `GapBehind` (double), `CleanAirLapIfWait` (int) |
| `Fuel.*` | `Level`, `PerLapAverage`, `LapsToEmpty`, `LapsToEmptyAtMargin`, `MinFuelToAdd`, `HasEnoughData` |
| `Pit.*` | `RecommendedLap`, `LapsUntilPit`, `StintsRemaining` |
| `Strategy.*` (diagnostic) | `OneStop.{FinishTime, StopLap}`, `TwoStop.{FinishTime, StopLap1, StopLap2}`, `BestName` |
| `Undercut.*` | `IsActive`, `RivalCarIdx`, `ExpectedGain` |
| `Fcy.*` | `IsOpportunity`, `TimeSavings`, `Active` |
| `Sim.*` | `LastRunUtc`, `IsRunning`, `WinProbability` |

Actions: `PitStrategy.Action.RunSimulation`, `ResetFuelTracker`, `ToggleFuelSaving`, `DumpFrame`.

Settings (WPF page): tank-capacity override, fuel-per-lap manual override, safety margin laps, **PitLossConfig overrides** (pit-lane length, speed limit, fuel-flow rate, tire-change time per car class), clean-air threshold (default 3 s), MC trial count, parallelism toggle, seed, log level, "Dump last frame to JSON" debug button.

See `docs/IRACING-FIELDS.md` for the full list of telemetry fields read by `IRacingMapper`.

---

## 4. Dashboard (SimHub Dash Studio)

Two-region layout. **Region A is the only thing the driver glances at; Region B is for testing/diagnostics.**

```
┌─────────────────────────────────────────────────────────────────────┐
│                                                                     │
│                          ┌───────────────┐                          │
│                          │   PIT NOW     │   ← BIG, color-coded     │
│                          │               │     by confidence        │
│                          │ Undercut #14  │   ← PrimaryReason        │
│                          │   (87%)       │   ← Confidence           │
│                          └───────────────┘                          │
│                                                                     │
│  Pit loss: 26.4s  ── travel 5.2s ── fuel 14.0s ── tires 11.5s       │
│                                                                     │
│  Traffic on rejoin: P7  ▼ 1.2s (#22)  ▲ 4.5s (#9)   IN TRAFFIC      │
│                                                                     │
├─────────────────────────────────────────────────────────────────────┤
│  ── DIAGNOSTICS (test panel) ─────────────────────────────────────  │
│  Laps to empty: 12.3   Fuel to add: 34.7L   Window: laps 24–30      │
│  1-stop  ████████████  +0.0s  (BEST)                                │
│  2-stop  █████████████ +24.3s                                       │
│  Last MC: 2,000 trials, win-prob 41%, computed 18s ago              │
└─────────────────────────────────────────────────────────────────────┘
```

Example NCalc bindings:
- Decision card text: `[PitStrategy.Decision.Kind]`
- Decision card color (high-confidence pit = green, low-confidence = amber, stay-out = neutral): `if([PitStrategy.Decision.Kind] = 'PitNow' and [PitStrategy.Decision.Confidence] > 0.7, '#34C759', if([PitStrategy.Decision.Kind] = 'PitNow', '#FFCC00', if([PitStrategy.Decision.Kind] = 'StayOut', '#1E2329', '#FF9500')))`
- Confidence subtext: `format([PitStrategy.Decision.Confidence] * 100, '0') + '%'`
- Pit-loss decomposition bar widths proportional to `[PitStrategy.PitLoss.PitLaneTravel] / [PitStrategy.PitLoss.Total]`, etc.
- Traffic banner color: `if([PitStrategy.Traffic.IsCleanAir] = true, '#34C759', '#FF3B30')`
- Traffic text: `'P' + [PitStrategy.Traffic.RejoinPosition] + '  ▼ ' + format([PitStrategy.Traffic.GapBehind], '0.0') + 's   ▲ ' + format([PitStrategy.Traffic.GapAhead], '0.0') + 's'`

Ship as `dashboards/PitStrategy.simhubdash` (JSON file, no build step required).

---

## 5. Build & test workflow ✅ implemented

### Local — build + test (Windows)

```powershell
dotnet build PitStrategy.sln -c Release
dotnet test  tests\PitStrategy.Plugin.Tests\PitStrategy.Plugin.Tests.csproj -c Release
```

```bash
# macOS / Linux — restore only
dotnet restore PitStrategy.sln
```

The whole project (plugin + tests) is `net48` + WPF, so building and testing both require Windows. Both csprojs set `<NoBuild>true</NoBuild>` on non-Windows so `dotnet build` of the solution is a no-op there.

### CI (single workflow, `windows-latest`) — `.github/workflows/ci.yml`

Triggers: push to `main`, tags `v*`, PRs to `main`, `workflow_dispatch` (manual run).

Pipeline:

1. `actions/checkout@v4`
2. `actions/setup-dotnet@v4` with `dotnet-version: 8.0.x`.
3. **Resolve latest SimHub release** — authenticated `Invoke-RestMethod` to `api.github.com/repos/SHWotever/SimHub/releases/latest` using `Bearer ${{ secrets.GITHUB_TOKEN }}`. Records the zip's `browser_download_url` and the SimHub tag (e.g. `9.11.11`) as step outputs.
4. **Cache by SimHub tag** — `actions/cache@v4` keyed `simhub-sdk-${{ steps.simhub.outputs.tag }}` over `lib/simhub-sdk/`. Cache hit skips step 5 entirely.
5. **Download + silent install** — `Invoke-WebRequest` the 217 MB zip → `7z x` to get `SimHubSetup_<v>.exe` → `Start-Process` it with `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /NOICONS -Wait` → copy `SimHub.Plugins.dll` and `GameReaderCommon.dll` from `C:\Program Files (x86)\SimHub\` into `lib/simhub-sdk/`.
6. **Upload `simhub-sdk-dlls` artifact** — `if: always()`, retention 1 day. Used to grab the SDK locally for `ilspycmd` decompilation.
7. `dotnet restore PitStrategy.sln`
8. `dotnet build PitStrategy.sln -c Release --no-restore`
9. `dotnet test tests\PitStrategy.Plugin.Tests\PitStrategy.Plugin.Tests.csproj -c Release --no-build --logger "trx;LogFileName=plugin.trx"`
10. Upload `test-results` artifact (`if: always()`).
12. **Compute version label**: `dev-<7-char-SHA>` on push/PR/manual; `<tag>` on tag push.
13. **Stage release contents** into `package-stage/`: `PitStrategy.dll` + `PitStrategy.simhubdash.template.json` + `INSTALL.md` + `README.md`.
14. **Upload two artifacts** (always):
    - `PitStrategy-<label>-dll` — bare DLL
    - `PitStrategy-<label>-package` — staged dir
15. **Tag-only**: zip `package-stage/*` into `PitStrategy-<tag>-windows.zip` and create a GitHub Release with auto-generated notes via `softprops/action-gh-release@v2`.

Cold-cache run is ~4 min (the silent install dominates). Warm-cache run is ~2 min.

### Test categories (today)

1. **Unit** — `FuelTracker`, `PitLossModel`, `RejoinPredictor`, `PitWindowCalculator`. ~30 tests, ~50 ms total. ✅
2. **Decision-tree golden tests** — `StrategyEngineTests` and `StrategyEnginePhase2Tests` in code (not JSON fixtures yet). 8 scenarios covering forced fuel pit, no-pit-needed, FCY, undercut, clean-air timing, default optimal. ✅
3. **MC determinism** — Phase 3, not yet written.
4. **MC perf smoke** — Phase 3, not yet written.

### Replay-driven debugging (planned)

The plugin has a `PitStrategy.Action.DumpFrame` action that writes the latest `RaceState` + `PitDecision` to `%TEMP%\PitStrategy\frame-*.json`. The `tools/PitStrategy.ReplayHarness` console app to replay those JSONs into the engine on Mac is **not yet implemented**.

---

## 6. Risks & unknowns (status)

| # | Risk | Status |
|---|---|---|
| 1 | Traffic-projection accuracy hinges on rival pace stability | **Live** — `RecentLapTimeStdDevSeconds` field on `RivalState` is in place but not yet damping `Confidence` (engine has the wiring; needs mapper to populate the stddev). |
| 2 | Pit lane length not always in `WeekendInfo` | **Live** — current code uses 350 m default with a `OverridePitLaneLength` setting. TODO: read `WeekendInfo.TrackPitLaneTotalLength` once raw-frame access is wired. |
| 3 | `PlayerCarPitSvFlags` bitmask is undocumented | **Deferred** — Phase 1+2 don't depend on it; will read `PitSvFuel > 0`, `PitSvLFP > 0` empirically when Phase 3 needs it. |
| 4 | `FuelUsePerHour` is noisy | **Resolved** — never used. `FuelTracker` reads `FuelLevel` deltas at lap boundaries. |
| 5 | iRacing field-name drift | **Resolved** — mapper uses SimHub-normalized fields (verified against the actual `StatusDataBase` definition). Raw-frame access is opt-in via `dynamic`. |
| 6 | Tire wear telemetry not always populated | **Live** — currently `TireWearSnapshot.Unknown` is hardcoded. Needs raw-frame access. |
| 7 | Multi-class race rival math | **Partly live** — `RivalState.ClassId` populated and `UndercutDetector` filters by class. `RejoinPredictor` separates `RejoinPosition` vs `RejoinClassPosition`. |
| 8 | SimHub SDK DLL licensing for public CI | **Resolved** — CI auto-fetches SimHub from public GitHub Releases at run time and silent-installs to extract the SDK DLLs. No DLLs ever committed to the repo. |
| 9 | **NEW**: `Logging` class is not in `SimHub.Plugins.dll` (likely in `WoteverCommon.dll` not yet vendored) | **Live** — exception handlers swallow silently. Wire up later by also extracting `WoteverCommon.dll` in CI step 5. |
| 10 | **NEW**: `data.NewData.GetRawDataObject()` exists but the iRacing SDK type isn't in our reference set | **Live** — plugin uses `dynamic` (with `Microsoft.CSharp` reference). Field-name typos won't surface until iRacing replay testing. |
| 11 | **NEW**: GitHub API anonymous rate limit (60/hr) is shared across runner IP pool | **Resolved** — workflow authenticates with `secrets.GITHUB_TOKEN` (1,000/hr per repo). |
| 12 | **NEW**: SimHub's release zip is wrapped Inno Setup, not a portable layout | **Resolved** — CI runs the installer silently to extract DLLs. See §5 step 5. |

---

## 7. Phased delivery

### Phase 1 — MVP: rule-based pit-now/stay-out  ✅ done
The driver can glance at the dashboard and see "PIT NOW" or "STAY OUT" with a reason.
- ✅ Pure-logic types under `PitStrategy.Core.*` namespaces in the Plugin assembly: `RaceState`, `RaceConfig`, `FuelTracker`, `PitLossModel`, `PitWindowCalculator`, rule-based `StrategyEngine.Decide`.
- ✅ `PitStrategy.Plugin`: full `IPlugin/IDataPlugin/IWPFSettingsV2` skeleton, frame-dump action, WPF settings page.
- ✅ 38 tests passing (target was 50+; the existing surface is well-covered, more can be added incrementally).
- ✅ Dashboard JSON template with example NCalc bindings.
- ✅ Properties: `Decision.*`, `PitLoss.*`, `Fuel.*`, `Pit.*`.
- ✅ CI: green build + test + artifacts on every push.
- **Exit criterion not yet validated end-to-end** — needs a Windows tester to load the DLL into SimHub and run a 30-min iRacing practice. Local Mac dev cannot exercise this.

### Phase 2 — Traffic + tactical alerts  ✅ done
The driver gets clean-air and tactical-undercut signals.
- ✅ `src/PitStrategy.Plugin/Traffic/RejoinPredictor.cs` — full implementation, including `CleanAirLapIfWait` lookahead.
- ✅ `src/PitStrategy.Plugin/Analysis/{UndercutDetector, FcyAnalyzer}.cs`.
- ✅ Decision tree includes rules 3–5 (FCY, undercut, clean-air timing).
- ✅ `PitDecisionKind.PitNextLap` reachable.
- ✅ New properties: `Traffic.*`, `Undercut.*`, `Fcy.*`.
- **Dashboard expansion not yet built** — `dashboards/PitStrategy.simhubdash.template.json` lists the bindings but the actual `.simhubdash` file requires SimHub Dash Studio on Windows.
- **Exit criterion**: against a fixture where rival is 1.5 s ahead and 3 laps from pitting, decision flips to `PitNow` with `Undercut` populated; against a fixture where pitting now rejoins in traffic but waiting 2 laps rejoins in clean air, decision flips to `PitNextLap` with reason "Wait 2 laps — clean air".

### Phase 3 — Monte Carlo + endurance polish  ⏸ deferred
- `src/PitStrategy.Plugin/Simulation/`: `MonteCarloSimulator`, `RaceLapSimulator`, `Distributions`.
- `RecommendWithSimulationAsync` runs MC across candidate strategies; updates `Confidence` on the headline `PitDecision` and populates `Strategy.*` diagnostic properties (the API surface and the `RunSimulation` action are already in place — they're just no-ops today).
- Per-compound tire calibration learning from the driver's own first stint (also unblocks the `LFwearL/M/R` raw-frame access risk #6).
- Driver-swap detection (resets pace baseline, not fuel tracker).
- Multi-class rival filtering refinements (rivals' projected pit windows).
- Basic weather-window prediction (`Precipitation > 0.5` triggers pre-position).
- **Exit criterion**: 5,000-trial MC <500 ms on a 6-core CPU; a Daytona 24h replay completes without mis-recommendations; driver swap and tire-compound change handled correctly.

---

## 8. Reference reading (don't fork — study patterns)

- [`KLPlugins.RaceEngineer`](https://github.com/kaiusl/KLPlugins.RaceEngineer) — closest existing strategy plugin; study `IDataPlugin` and property-exposure patterns.
- [`DahlDesignProperties`](https://github.com/andreasdahl1987/DahlDesignProperties) — clean iRacing field-access patterns via `GetRawDataObject()`.
- [`IRSDKSharper`](https://github.com/mherbold/IRSDKSharper) — for `DataSampleEx` field shape.
- [`TUMFTM/race-simulation`](https://github.com/TUMFTM/race-simulation) — academic Monte Carlo strategy in Python; logic to translate to C#.
- [`rembertdesigns/pit-stop-simulator`](https://github.com/rembertdesigns/pit-stop-simulator) — RL approach to pit timing; useful for shaping the simulator's state representation.
- iRacing telemetry reference: <https://sajax.github.io/irsdkdocs/>
- SimHub plugin SDK wiki: <https://github.com/SHWotever/SimHub/wiki/Plugin-and-extensions-SDKs>

---

## 9. Verification

### Phase 1 + 2 — current status

| Step | Status |
|---|---|
| Unit tests (Core) | ✅ 38 passing |
| Decision-tree code tests | ✅ — 8 scenarios in `StrategyEngineTests` and `StrategyEnginePhase2Tests` cover forced fuel pit, no-pit-needed, FCY, undercut, traffic projection, default optimal. JSON-fixture style not implemented; current tests build `RaceState` directly in code |
| CI builds Plugin DLL on Windows | ✅ green on `windows-latest` |
| Plugin loads in SimHub | ❓ **needs Windows tester** — DLL hasn't actually been deployed and exercised yet |
| Replay session validation | ❓ same |
| Live iRacing 30-min practice | ❓ same |

The path to validating Phase 1+2 end-to-end:
1. Download the latest `PitStrategy-dev-<sha>-package` artifact from a CI run.
2. Extract on a Windows machine; copy `PitStrategy.dll` into `C:\Program Files (x86)\SimHub\`.
3. Restart SimHub, enable the plugin in *Settings → Additional plugins*.
4. Open SimHub → *Available Properties* and confirm the `PitStrategy.*` namespace shows up.
5. Open a known iRacing replay or join a 30-min practice; observe `PitStrategy.Decision.Kind` transitions.
6. Use the `PitStrategy.Action.DumpFrame` action (button-bindable) to capture problem frames as JSON in `%TEMP%\PitStrategy\` for replay-driven debugging.

### Phase 3 (deferred) verification targets
- MC determinism: same seed twice → identical `Confidence` value to 6 decimal places.
- MC perf: 5,000 trials × 200 laps in <2 s on the CI `windows-latest` runner.
- Daytona 24h replay end-to-end: driver swap correctly resets pace baseline, fuel tracker continues; tire-compound change updates wear baseline; weather forecast trigger pre-positions pit window.

---

## 10. Conventions

- **Research before guessing.** When changing plugin code that touches the SimHub or iRacing API, decompile the actual DLLs (download the `simhub-sdk-dlls` CI artifact into `scratch/simhub-sdk/`) with `ilspycmd` rather than guessing from examples.
- **Don't rewrite published history.** Make new commits forward-only. Force-pushing `main` invalidates downstream clones, including CI-built artifacts.
- **Commit messages**: concise subject; the body explains *why* if non-obvious. End with the standard footer:
  ```
  Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
  ```
- **Plugin testability**: the SimHub-touching code is intentionally thin. Pure logic lives under `PitStrategy.Core.*` namespaces and is exercised by `tests/PitStrategy.Plugin.Tests/` — keep it free of `SimHub.Plugins`/`GameReaderCommon` references so it stays trivially testable. New decisions go through `StrategyEngine`; new properties go through `PropertyPublisher`; new SimHub-specific quirks go in `IRacingMapper`.
