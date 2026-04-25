# PitStrategy

AI-driven pit strategy plugin for SimHub + iRacing. Outputs a single **PIT NOW / STAY OUT** decision card with confidence and reason; everything else (pit-loss decomposition, traffic projection, undercut/FCY alerts) is exposed as plugin properties for testing and dashboard bindings.

## Status

- Phase 1 (rule-based fuel/pit-window engine): **complete**
- Phase 2 (traffic projection + undercut + FCY tactical alerts): **complete**
- Phase 3 (Monte Carlo simulator + endurance polish): **deferred**

The Core library has 38 passing unit + golden tests. The SimHub plugin DLL builds on Windows.

## Repository layout

```
PitStrategy/
├── src/
│   ├── PitStrategy.Core/        # netstandard2.0 — pure decision logic, fully unit-tested
│   └── PitStrategy.Plugin/      # net48 — SimHub plugin DLL (Windows-only)
├── tests/PitStrategy.Core.Tests/
├── dashboards/                  # SimHub Dash Studio dashboard templates
├── lib/simhub-sdk/              # vendored SimHub SDK DLLs (you provide)
├── .github/workflows/ci.yml     # windows-latest build + test + release
└── docs/                        # INSTALL.md, IRACING-FIELDS.md, ARCHITECTURE.md
```

## Build

### Core library + tests (works anywhere)

```bash
dotnet test tests/PitStrategy.Core.Tests/PitStrategy.Core.Tests.csproj
```

### Plugin DLL (Windows only)

1. Install Visual Studio 2022 with the **.NET desktop development** workload (gives you net48 targeting + WPF designer) and the **.NET 8 SDK**.
2. Copy `SimHub.Plugins.dll` and `GameReaderCommon.dll` from your SimHub install (default `C:\Program Files (x86)\SimHub\`) into `lib/simhub-sdk/`.
3. From a PowerShell prompt:
   ```powershell
   dotnet build src\PitStrategy.Plugin\PitStrategy.Plugin.csproj -c Release
   ```
   The Release post-build target auto-copies `PitStrategy.dll` into `C:\Program Files (x86)\SimHub\`.
4. Restart SimHub. Enable the **Pit Strategy** plugin in Settings → Additional plugins.

See [`docs/INSTALL.md`](docs/INSTALL.md) for screenshots and troubleshooting.

## Plugin properties (for dashboard bindings)

| Property | Type | Description |
| --- | --- | --- |
| `PitStrategy.Decision.Kind` | string | One of `Unknown`, `PitNow`, `PitNextLap`, `StayOut`, `SaveFuel`, `PitForFuelOnly`. |
| `PitStrategy.Decision.Confidence` | double | 0.0–1.0. |
| `PitStrategy.Decision.PrimaryReason` | string | Dashboard-friendly one-liner. |
| `PitStrategy.Decision.RecommendedPitLap` | int | 0 if no pit needed. |
| `PitStrategy.PitLoss.Total` | double | Seconds. |
| `PitStrategy.PitLoss.PitLaneTravel` | double | Seconds. |
| `PitStrategy.PitLoss.FuelService` | double | Seconds. |
| `PitStrategy.PitLoss.TireService` | double | Seconds. |
| `PitStrategy.PitLoss.EntryExitDelta` | double | Seconds. |
| `PitStrategy.Traffic.IsCleanAir` | bool | True when both gaps ≥ threshold. |
| `PitStrategy.Traffic.RejoinPosition` | int | Projected position at pit-exit. |
| `PitStrategy.Traffic.GapAhead` | double | Seconds; 0 when no rival ahead. |
| `PitStrategy.Traffic.GapBehind` | double | Seconds; 0 when no rival behind. |
| `PitStrategy.Traffic.CleanAirLapIfWait` | int | 0 if not waiting helps. |
| `PitStrategy.Fuel.Level` | double | Litres, current. |
| `PitStrategy.Fuel.PerLapAverage` | double | Litres / lap, rolling 5-lap avg. |
| `PitStrategy.Fuel.LapsToEmpty` | double | Float. |
| `PitStrategy.Fuel.LapsToEmptyAtMargin` | double | Float, with safety margin applied. |
| `PitStrategy.Fuel.MinFuelToAdd` | double | Litres. |
| `PitStrategy.Fuel.HasEnoughData` | bool | False before first green-flag lap. |
| `PitStrategy.Pit.RecommendedLap` | int | Lap to pit at. |
| `PitStrategy.Pit.LapsUntilPit` | int | Difference vs current lap. |
| `PitStrategy.Pit.StintsRemaining` | int | |
| `PitStrategy.Undercut.IsActive` | bool | |
| `PitStrategy.Undercut.RivalCarIdx` | int | -1 if not active. |
| `PitStrategy.Undercut.ExpectedGain` | double | Seconds. |
| `PitStrategy.Fcy.IsOpportunity` | bool | |
| `PitStrategy.Fcy.TimeSavings` | double | Seconds vs green-flag pit. |

Sample NCalc bindings are in [`dashboards/PitStrategy.simhubdash.template.json`](dashboards/PitStrategy.simhubdash.template.json).

## Plugin actions (button-bindable)

- `PitStrategy.Action.RunSimulation` — kicks off a Monte Carlo run (Phase 3, currently a no-op).
- `PitStrategy.Action.ResetFuelTracker` — clears the rolling fuel window. Bind to a button used after driver swaps.
- `PitStrategy.Action.DumpFrame` — writes the current state + decision to `%TEMP%\PitStrategy\frame-*.json`. Use for capturing reproducible scenarios.

## Decision tree (Phase 1 + 2)

The engine evaluates rules in order; first match wins.

1. **Forced fuel pit** — fuel runs out before any other window opens.
2. **No pit needed** — fuel covers the rest of the race with margin.
3. **FCY opportunity** — pit while the field is bunched (~45 % of green-flag pit cost).
4. **Undercut window** — rival immediately ahead is within `UndercutThresholdSeconds` and hasn't pitted.
5. **Clean-air timing** — pitting now lands in traffic but waiting K laps yields clean air.
6. **Default optimal** — pit at the centre of the legal window.

## License

Source code: see [LICENSE](LICENSE) (TBD).
SimHub SDK DLLs in `lib/simhub-sdk/` are property of SimHub and excluded from this repository.
