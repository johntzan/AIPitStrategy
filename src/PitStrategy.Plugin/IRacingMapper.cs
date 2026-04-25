using System;
using System.Collections.Generic;
using GameReaderCommon;
using PitStrategy.Core.Inputs;

namespace PitStrategy.Plugin
{
    /// <summary>
    /// Maps SimHub's normalized <see cref="GameData"/> + iRacing's raw telemetry sample
    /// into our <see cref="RaceState"/>.
    ///
    /// Field-name reference: see docs/IRACING-FIELDS.md and the decompiled
    /// SimHub.Plugins / GameReaderCommon DLLs. The normalized layer uses int-valued
    /// flags (e.g. <c>IsInPit</c>, <c>IsInPitLane</c>, <c>Flag_Yellow</c>) — convert
    /// with <c>&gt; 0</c>, not a direct bool cast.
    /// </summary>
    public sealed class IRacingMapper
    {
        private readonly Settings.PluginSettings _settings;

        public IRacingMapper(Settings.PluginSettings settings) => _settings = settings;

        public RaceState? Map(GameData data, double rollingFuelPerLap, bool fuelHasEnoughData)
        {
            if (data?.NewData == null) return null;

            var nd = data.NewData;

            // ── Identity / progress ────────────────────────────────────────────────────
            int currentLap = nd.CurrentLap;
            // GameData doesn't normalize a 0..1 lap-distance fraction directly; we compute
            // it from the player's Opponent entry when available (TrackPositionPercent).
            double lapDistPct = ResolvePlayerLapDistPct(nd);

            TimeSpan sessionTimeElapsed = TimeSpan.Zero;          // not exposed; defaulted
            TimeSpan? sessionTimeRemaining = nd.SessionTimeLeft != TimeSpan.Zero
                ? (TimeSpan?)nd.SessionTimeLeft
                : null;
            int? lapsRemaining = nd.RemainingLaps > 0 ? (int?)nd.RemainingLaps : null;

            // ── Fuel ────────────────────────────────────────────────────────────────────
            double fuel = nd.Fuel;
            double tank = _settings.OverrideTankCapacity
                ? _settings.TankCapacityLitersOverride
                : (nd.MaxFuel > 0 ? nd.MaxFuel : 60.0);

            double fuelPerLap = _settings.OverrideFuelPerLap
                ? _settings.FuelPerLapOverride
                : rollingFuelPerLap;

            // ── Pace ────────────────────────────────────────────────────────────────────
            TimeSpan lastLap = nd.LastLapTime;
            TimeSpan bestLap = nd.BestLapTime;

            // ── Player pit context (StatusDataBase exposes int flags 0/1) ──────────────
            bool onPitRoad = nd.IsInPitLane > 0;
            bool inPitStall = nd.IsInPit > 0;

            // ── Session flags ──────────────────────────────────────────────────────────
            SessionFlag flags = SessionFlag.None;
            if (nd.Flag_Green > 0)     flags |= SessionFlag.Green;
            if (nd.Flag_Yellow > 0)    flags |= SessionFlag.Yellow;
            if (nd.Flag_Blue > 0)      flags |= SessionFlag.Blue;
            if (nd.Flag_White > 0)     flags |= SessionFlag.White;
            if (nd.Flag_Checkered > 0) flags |= SessionFlag.Checkered;
            // FCY: SimHub doesn't normalize a dedicated FCY flag; we treat field-wide
            // yellow + a paced field as FCY heuristically. This will be refined by
            // reading the raw iRacing SessionFlags bitmask in a Phase 3 follow-up.
            bool isUnderFcy = nd.Flag_Yellow > 0;
            if (isUnderFcy) flags |= SessionFlag.FullCourseYellow;

            // ── Player position (Opponent entry) ───────────────────────────────────────
            int position = nd.Position > 0 ? nd.Position : 1;
            int classPosition = ResolvePlayerClassPosition(nd, position);
            int playerClassId = ResolvePlayerClassId(nd);
            int playerCarIdx = 0; // not exposed by the normalized layer; iRSDK CarIdx requires raw access

            // ── Rivals ─────────────────────────────────────────────────────────────────
            var rivals = MapRivals(nd, playerClassId);

            // ── Tires (default Unknown — iRacing tire-wear access requires raw frame) ──
            // TODO: pull LFwearL/M/R etc. from data.NewData.GetRawDataObject() once we
            // verify the iRSDK wrapper type ships with SimHub.
            var tires = TireWearSnapshot.Unknown;
            int lapsOnTires = nd.CompletedLaps;

            // ── Weather (best-effort from normalized fields) ───────────────────────────
            var weather = new WeatherSnapshot(
                TrackTempC: 25,
                AirTempC: nd.AirTemperature,
                Humidity: 0.4,
                SkiesCloudCover: 0.1,
                PrecipChance: 0,
                IsRaining: false,
                TrackDeclaredWet: false);

            return new RaceState
            {
                CurrentLap = currentLap,
                LapDistPct = lapDistPct,
                SessionTimeElapsed = sessionTimeElapsed,
                SessionTimeRemaining = sessionTimeRemaining,
                LapsRemaining = lapsRemaining,

                FuelLevelLiters = fuel,
                FuelUsedLastLapLiters = 0,
                RollingFuelPerLap = fuelPerLap,
                FuelTrackerHasEnoughData = fuelHasEnoughData,
                TankCapacityLiters = tank,

                LastLapTime = lastLap,
                BestLapTime = bestLap,
                RecentLapTimes = Array.Empty<TimeSpan>(),

                Tires = tires,
                LapsOnCurrentTires = lapsOnTires,

                Rivals = rivals,
                PlayerCarIdx = playerCarIdx,
                PlayerPosition = position,
                PlayerClassPosition = classPosition,
                PlayerClassId = playerClassId,

                Flags = flags,
                IsUnderFcy = isUnderFcy,
                Weather = weather,

                IsOnPitRoad = onPitRoad,
                IsInPitStall = inPitStall,
                PitsOpen = true,
                CompletedPitStops = 0, // not exposed by normalized layer; computed in Phase 3
            };
        }

        private static double ResolvePlayerLapDistPct(StatusDataBase nd)
        {
            if (nd.Opponents == null) return 0;
            foreach (var op in nd.Opponents)
            {
                if (op.IsPlayer) return op.TrackPositionPercent ?? 0;
            }
            return 0;
        }

        private static int ResolvePlayerClassPosition(StatusDataBase nd, int fallback)
        {
            if (nd.Opponents == null) return fallback;
            foreach (var op in nd.Opponents)
            {
                if (op.IsPlayer) return op.PositionInClass > 0 ? op.PositionInClass : fallback;
            }
            return fallback;
        }

        private static int ResolvePlayerClassId(StatusDataBase nd)
        {
            if (nd.Opponents == null) return 0;
            foreach (var op in nd.Opponents)
            {
                if (op.IsPlayer)
                {
                    return int.TryParse(op.CarClassID, out var id) ? id : op.CarClassID?.GetHashCode() ?? 0;
                }
            }
            return 0;
        }

        private static IReadOnlyList<RivalState> MapRivals(StatusDataBase nd, int playerClassId)
        {
            if (nd.Opponents == null || nd.Opponents.Count == 0) return Array.Empty<RivalState>();

            var list = new List<RivalState>(nd.Opponents.Count);
            int idx = 0;
            foreach (var op in nd.Opponents)
            {
                int carIdx = idx++;
                if (op.IsPlayer) continue;

                int classId = int.TryParse(op.CarClassID, out var c) ? c : op.CarClassID?.GetHashCode() ?? 0;

                list.Add(new RivalState(
                    CarIdx: carIdx,
                    Position: op.Position,
                    ClassPosition: op.PositionInClass,
                    ClassId: classId,
                    LapsCompleted: (op.CurrentLap ?? 0) - 1,
                    LapDistPct: op.TrackPositionPercent ?? 0,
                    GapToPlayerSeconds: TimeSpan.FromSeconds(op.GaptoPlayer ?? 0),
                    LastLapTime: op.LastLapTime,
                    AverageRecentLapTime: op.LastLapTime,
                    RecentLapTimeStdDevSeconds: 0.3,
                    CompletedPitStops: 0, // not exposed
                    IsOnPitRoad: op.IsCarInPitLane));
            }
            return list;
        }
    }
}
