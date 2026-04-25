using System;
using System.Collections.Generic;

namespace PitStrategy.Core.Inputs
{
    /// <summary>
    /// Per-tick snapshot of everything the engine needs to decide whether to pit. Built by
    /// the SimHub plugin shim from iRacing's <c>DataSampleEx</c> + SessionInfo YAML.
    /// All fields are at the moment of the most-recent telemetry frame.
    /// </summary>
    public sealed record RaceState
    {
        // ── Identity / progress ──────────────────────────────────────────────────────────
        public int CurrentLap { get; init; }
        public double LapDistPct { get; init; }                     // 0.0–1.0
        public TimeSpan SessionTimeElapsed { get; init; }
        public TimeSpan? SessionTimeRemaining { get; init; }        // null in lap-counted races
        public int? LapsRemaining { get; init; }                    // null in pure timed races

        // ── Fuel ─────────────────────────────────────────────────────────────────────────
        public double FuelLevelLiters { get; init; }
        public double FuelUsedLastLapLiters { get; init; }
        public double RollingFuelPerLap { get; init; }              // populated by FuelTracker
        public bool FuelTrackerHasEnoughData { get; init; }
        public double TankCapacityLiters { get; init; }

        // ── Pace ─────────────────────────────────────────────────────────────────────────
        public TimeSpan LastLapTime { get; init; }
        public TimeSpan BestLapTime { get; init; }
        public IReadOnlyList<TimeSpan> RecentLapTimes { get; init; } = Array.Empty<TimeSpan>();
        public TimeSpan? OptimalLapTime { get; init; }

        // ── Tires ────────────────────────────────────────────────────────────────────────
        public TireWearSnapshot Tires { get; init; } = TireWearSnapshot.Unknown;
        public int LapsOnCurrentTires { get; init; }

        // ── Field ────────────────────────────────────────────────────────────────────────
        public IReadOnlyList<RivalState> Rivals { get; init; } = Array.Empty<RivalState>();
        public int PlayerCarIdx { get; init; }
        public int PlayerPosition { get; init; }
        public int PlayerClassPosition { get; init; }
        public int PlayerClassId { get; init; }

        // ── Conditions ───────────────────────────────────────────────────────────────────
        public SessionFlag Flags { get; init; }
        public bool IsUnderFcy { get; init; }
        public TimeSpan? FcyElapsed { get; init; }
        public WeatherSnapshot Weather { get; init; } = WeatherSnapshot.Dry;

        // ── Pit context ──────────────────────────────────────────────────────────────────
        public bool IsOnPitRoad { get; init; }
        public bool IsInPitStall { get; init; }
        public bool PitsOpen { get; init; } = true;
        public int CompletedPitStops { get; init; }

        /// <summary>True when no green laps have completed yet, so fuel and pace stats are unreliable.</summary>
        public bool HasInsufficientData => !FuelTrackerHasEnoughData || RollingFuelPerLap <= 0;
    }
}
