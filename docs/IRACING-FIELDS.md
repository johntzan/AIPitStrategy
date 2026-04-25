# iRacing telemetry fields used by PitStrategy

This is the load-bearing list of fields the plugin reads each tick. If iRacing renames any of these (it has happened — `SessionLapsRemain` was deprecated in favor of `SessionLapsRemainEx`), update both the mapper and this doc.

Reference: <https://sajax.github.io/irsdkdocs/>

## SimHub-normalized (`data.NewData`, type `StatusDataBase`)

| Field | Type | Mapped to |
| --- | --- | --- |
| `CurrentLap` | int | `RaceState.CurrentLap` |
| `TrackPositionPercent` | double | `RaceState.LapDistPct` |
| `Fuel` | double L | `RaceState.FuelLevelLiters` |
| `MaxFuel` | double L | `RaceState.TankCapacityLiters` (override available in settings) |
| `RemainingLaps` | int | `RaceState.LapsRemaining` |
| `SessionTimeLeft` | TimeSpan | `RaceState.SessionTimeRemaining` |
| `LastLapTime` | TimeSpan | `RaceState.LastLapTime` |
| `BestLapTime` | TimeSpan | `RaceState.BestLapTime` |
| `IsInPitLane` | bool | `RaceState.IsOnPitRoad` |
| `IsInPit` | bool | `RaceState.IsInPitStall` |
| `Position` | int | `RaceState.PlayerPosition` |
| `PositionInClass` | int | `RaceState.PlayerClassPosition` |
| `PitCount` | int | `RaceState.CompletedPitStops` |
| `Opponents[]` | List | `RaceState.Rivals` (each maps to a `RivalState`) |

Per-opponent fields (vary slightly by SimHub version):

- `CarIndex` → `RivalState.CarIdx`
- `Position` / `PositionInClass`
- `CarClass` (string or int)
- `CurrentLap` / `LapsCompleted`
- `TrackPositionPercent`
- `GaptoPlayer` (TimeSpan, signed)
- `LastLapTime` (TimeSpan)
- `PitCount`
- `IsCarInPit` (bool)
- `IsPlayer` (bool — used to skip ourselves)

If the normalized opponent path returns nothing, the mapper falls back to raw iRacing arrays (see below).

## Raw iRacing (`data.NewData.GetRawDataObject() as DataSampleEx`)

Used for fields SimHub doesn't normalize. Accessed via `dynamic` — verify each one against your iRacing build.

### Telemetry (60 Hz)

| Field | Type | Used for |
| --- | --- | --- |
| `Telemetry.SessionFlags` | uint bitmask | green / yellow / FCY / red / white / checkered |
| `Telemetry.PitsOpen` | bool | engine refuses to recommend `PitNow` if pits are closed |
| `Telemetry.FuelLevel` | float L | preferred over normalized `Fuel` for higher precision |
| `Telemetry.OnPitRoad` | bool | redundant with `IsInPitLane`, used as fallback |
| `Telemetry.LapCompleted` | int | tire-age tracker |
| `Telemetry.CarIdxPosition[]` | int[64] | rival positions (raw fallback) |
| `Telemetry.CarIdxClassPosition[]` | int[64] | rival class positions |
| `Telemetry.CarIdxLapDistPct[]` | float[64] | rival progress around the track |
| `Telemetry.CarIdxLap[]` | int[64] | rival laps |
| `Telemetry.CarIdxLastLapTime[]` | float[64] | rival pace (in seconds; -1 means no valid lap yet) |
| `Telemetry.CarIdxOnPitRoad[]` | bool[64] | rival pit state |
| `Telemetry.CarIdxClassID[]` | int[64] | for multi-class rival filtering |
| `Telemetry.LFwearL` / `LFwearM` / `LFwearR` etc. | float | tire wear per corner section (0.0–1.0) |
| `Telemetry.TrackTempCrew` | float °C | weather snapshot |
| `Telemetry.AirTemp` | float °C | weather snapshot |
| `Telemetry.RelativeHumidity` | float | weather snapshot |
| `Telemetry.Skies` | int (0–3) | clear / partly / overcast / raining |
| `Telemetry.Precipitation` | float | rain intensity (0.0–1.0) |
| `Telemetry.WeatherDeclaredWet` | bool | track flagged wet by race control |

### Session info (YAML, parsed once per session)

| Path | Mapped to |
| --- | --- |
| `SessionInfo.WeekendInfo.TrackPitSpeedLimit` | `PitLossConfig.PitLaneSpeedLimitKph` (TODO — Phase 1+2 uses settings override) |
| `SessionInfo.WeekendInfo.TrackPitLaneTotalLength` | `PitLossConfig.PitLaneLengthMeters` |
| `SessionInfo.DriverInfo.DriverCarIdx` | `RaceState.PlayerCarIdx` |
| `SessionInfo.DriverInfo.Drivers[i].CarClassID` | `RaceState.PlayerClassId` |
| `SessionInfo.DriverInfo.DriverCarFuelMaxLtr` | `RaceState.TankCapacityLiters` (override-able) |

### `SessionFlags` bit values (from `irsdk_supplied/irsdk_defines.h`)

The mapper recognises these bits today:

| Bit | Hex | Meaning |
| --- | --- | --- |
| `Yellow` | `0x0010` | a yellow flag is out somewhere |
| `Red` | `0x0008` | session red-flagged |
| `White` | `0x00040000` | white flag (last lap) |
| `Checkered` | `0x00020000` | session over |
| `CautionWaving` (FCY) | `0x4000` | full-course-yellow active |

If a bit you care about (e.g. `OneToGreen`, `StartReady`) needs surfacing, extend `IRacingMapper.MapWeather`/`MapFlags` and update the table above.

## Things we deliberately *do not* read

- **`FuelUsePerHour`** — too noisy in iRacing, varies wildly with revs and corner load. The plugin derives consumption from `FuelLevel` deltas at lap boundaries inside `FuelTracker`.
- **`PlayerCarPitSvFlags`** — the bitmask values are not in public iRacing SDK docs. The plugin reads `PitSvFuel`, `PitSvLFP/RFP/LRP/RRP` for which services are armed.

## Updating this doc

When you fix a field-name mismatch found on a Windows test session, please:

1. Add the corrected field name and the iRacing build it was verified against to the relevant table.
2. If the rename was system-wide (e.g. `SessionLapsRemain` → `SessionLapsRemainEx`), search the codebase for the old name and update everywhere.
