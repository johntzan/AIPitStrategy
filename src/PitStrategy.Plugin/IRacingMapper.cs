using System;
using System.Collections.Generic;
using GameReaderCommon;
using PitStrategy.Core.Inputs;

namespace PitStrategy.Plugin
{
    /// <summary>
    /// Maps SimHub's normalized <see cref="GameData"/> + iRacing's raw <c>DataSampleEx</c>
    /// into our <see cref="RaceState"/>. Uses <c>dynamic</c> for raw iRacing data so the plugin
    /// compiles regardless of which IRSDK wrapper SimHub ships in a given version — the field
    /// names below match the public iRacing telemetry contract.
    ///
    /// IMPORTANT: This file references iRacing field shape that has been verified through
    /// public docs (https://sajax.github.io/irsdkdocs/) but not run-time tested in this repo
    /// yet. Field naming for <c>data.NewData</c> matches SimHub's normalized opponent model.
    /// On the first Windows build, expect to fix one or two of these names if SimHub renamed
    /// something between versions — log the corrections in <c>docs/IRACING-FIELDS.md</c>.
    /// </summary>
    public sealed class IRacingMapper
    {
        private readonly Settings.PluginSettings _settings;

        public IRacingMapper(Settings.PluginSettings settings) => _settings = settings;

        public RaceState? Map(GameData data, double rollingFuelPerLap, bool fuelHasEnoughData)
        {
            if (data?.NewData == null) return null;

            // Raw iRacing data sample. Cast to dynamic so we don't depend on a specific
            // IRSDK wrapper namespace at compile time.
            dynamic? raw = null;
            try { raw = data.NewData.GetRawDataObject(); } catch { /* swallow */ }

            var nd = data.NewData;

            // ── Identity / progress ────────────────────────────────────────────────────
            int currentLap = (int)Math.Max(0, nd.CurrentLap);
            double lapDistPct = SafeDouble(() => (double)nd.TrackPositionPercent);
            TimeSpan sessionTimeElapsed = nd.SessionTimeLeft is TimeSpan ts ? TimeSpan.Zero - TimeSpan.Zero : TimeSpan.Zero;
            // SessionTimeElapsed isn't directly normalized; derive it from raw if needed below.

            // Time remaining + laps remaining
            TimeSpan? sessionTimeRemaining = null;
            int? lapsRemaining = null;
            try
            {
                sessionTimeRemaining = nd.SessionTimeLeft;
                lapsRemaining = nd.RemainingLaps > 0 ? (int?)nd.RemainingLaps : null;
            }
            catch { /* not all sims expose these */ }

            // ── Fuel ────────────────────────────────────────────────────────────────────
            double fuel = SafeDouble(() => (double)nd.Fuel);
            double tank = _settings.OverrideTankCapacity
                ? _settings.TankCapacityLitersOverride
                : SafeDouble(() => (double)nd.MaxFuel, fallback: 60);

            double fuelPerLap = _settings.OverrideFuelPerLap
                ? _settings.FuelPerLapOverride
                : rollingFuelPerLap;

            // ── Pace ────────────────────────────────────────────────────────────────────
            TimeSpan lastLap = SafeTimeSpan(() => nd.LastLapTime);
            TimeSpan bestLap = SafeTimeSpan(() => nd.BestLapTime);

            // ── Player pit context ──────────────────────────────────────────────────────
            bool onPitRoad = SafeBool(() => nd.IsInPitLane);
            bool inPitStall = SafeBool(() => nd.IsInPit);

            // ── Session flags / FCY (raw iRacing telemetry) ────────────────────────────
            SessionFlag flags = SessionFlag.Green;
            bool isUnderFcy = false;
            bool pitsOpen = true;
            int playerCarIdx = 0;
            int playerClassId = 0;

            if (raw != null)
            {
                try
                {
                    uint sf = (uint)raw.Telemetry.SessionFlags;
                    if ((sf & 0x0010) != 0) flags |= SessionFlag.Yellow;        // yellow
                    if ((sf & 0x4000) != 0) { flags |= SessionFlag.FullCourseYellow; isUnderFcy = true; } // caution waving
                    if ((sf & 0x0008) != 0) flags |= SessionFlag.Red;
                    if ((sf & 0x00040000) != 0) flags |= SessionFlag.White;
                    if ((sf & 0x00020000) != 0) flags |= SessionFlag.Checkered;
                }
                catch { }

                try { pitsOpen = (bool)raw.Telemetry.PitsOpen; } catch { }
                try { playerCarIdx = (int)raw.SessionInfo.DriverInfo.DriverCarIdx; } catch { }
                try { playerClassId = (int)raw.SessionInfo.DriverInfo.Drivers[playerCarIdx].CarClassID; } catch { }

                // Rolling fuel sanity: prefer raw FuelLevel over normalized Fuel when available
                try { fuel = (double)raw.Telemetry.FuelLevel; } catch { }
            }

            // ── Rivals (from SimHub's normalized OpponentData if available, else raw) ───
            var rivals = MapRivals(nd, raw, playerCarIdx, playerClassId);

            // ── Tires (raw iRacing) ─────────────────────────────────────────────────────
            var tires = MapTires(raw);
            int lapsOnTires = 0;
            try { if (raw != null) lapsOnTires = (int)raw.Telemetry.LapCompleted; } catch { }

            // ── Weather ─────────────────────────────────────────────────────────────────
            var weather = MapWeather(raw);

            // ── Player position ─────────────────────────────────────────────────────────
            int position = SafeInt(() => (int)nd.Position, fallback: 1);
            int classPosition = SafeInt(() => (int)nd.PositionInClass, fallback: 1);

            return new RaceState
            {
                CurrentLap = currentLap,
                LapDistPct = lapDistPct,
                SessionTimeElapsed = sessionTimeElapsed,
                SessionTimeRemaining = sessionTimeRemaining,
                LapsRemaining = lapsRemaining,

                FuelLevelLiters = fuel,
                FuelUsedLastLapLiters = 0, // FuelTracker tracks this internally
                RollingFuelPerLap = fuelPerLap,
                FuelTrackerHasEnoughData = fuelHasEnoughData,
                TankCapacityLiters = tank,

                LastLapTime = lastLap,
                BestLapTime = bestLap,
                RecentLapTimes = Array.Empty<TimeSpan>(), // populated by plugin's own rolling buffer if needed

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
                PitsOpen = pitsOpen,
                CompletedPitStops = SafeInt(() => (int)nd.PitCount),
            };
        }

        private static IReadOnlyList<RivalState> MapRivals(StatusDataBase nd, dynamic? raw, int playerCarIdx, int playerClassId)
        {
            var list = new List<RivalState>();

            // SimHub normalizes opponents into Opponents / OpponentsHandled — name varies by version.
            // Try the most common surfaces.
            try
            {
                var opponents = nd.Opponents ?? null;
                if (opponents != null)
                {
                    foreach (var op in (System.Collections.IEnumerable)opponents)
                    {
                        dynamic o = op!;
                        if ((bool?)o.IsPlayer == true) continue;
                        list.Add(new RivalState(
                            CarIdx: SafeInt(() => (int)o.CarIndex),
                            Position: SafeInt(() => (int)o.Position),
                            ClassPosition: SafeInt(() => (int)o.PositionInClass),
                            ClassId: SafeInt(() => (int)o.CarClass),
                            LapsCompleted: SafeInt(() => (int)o.CurrentLap) - 1,
                            LapDistPct: SafeDouble(() => (double)o.TrackPositionPercent),
                            GapToPlayerSeconds: SafeTimeSpan(() => o.GaptoPlayer),
                            LastLapTime: SafeTimeSpan(() => o.LastLapTime),
                            AverageRecentLapTime: SafeTimeSpan(() => o.LastLapTime),
                            RecentLapTimeStdDevSeconds: 0.3,
                            CompletedPitStops: SafeInt(() => (int)o.PitCount),
                            IsOnPitRoad: SafeBool(() => (bool)o.IsCarInPit)));
                    }
                }
            }
            catch { /* OpponentData shape varies; raw fallback below */ }

            // Raw iRacing CarIdx* arrays as a fallback. Only build this list if the normalized
            // path produced nothing.
            if (list.Count == 0 && raw != null)
            {
                try
                {
                    int[] positions = (int[])raw.Telemetry.CarIdxPosition;
                    int[] classPositions = (int[])raw.Telemetry.CarIdxClassPosition;
                    float[] distPct = (float[])raw.Telemetry.CarIdxLapDistPct;
                    int[] laps = (int[])raw.Telemetry.CarIdxLap;
                    float[] lastLapTimes = (float[])raw.Telemetry.CarIdxLastLapTime;
                    bool[] inPit = (bool[])raw.Telemetry.CarIdxOnPitRoad;
                    int[] classIds = (int[])raw.Telemetry.CarIdxClassID;

                    for (int i = 0; i < positions.Length; i++)
                    {
                        if (i == playerCarIdx) continue;
                        if (positions[i] <= 0) continue; // not a valid car idx

                        list.Add(new RivalState(
                            CarIdx: i,
                            Position: positions[i],
                            ClassPosition: classPositions[i],
                            ClassId: classIds[i],
                            LapsCompleted: laps[i],
                            LapDistPct: distPct[i],
                            GapToPlayerSeconds: TimeSpan.Zero, // not in raw arrays; computed elsewhere if needed
                            LastLapTime: TimeSpan.FromSeconds(Math.Max(0, lastLapTimes[i])),
                            AverageRecentLapTime: TimeSpan.FromSeconds(Math.Max(0, lastLapTimes[i])),
                            RecentLapTimeStdDevSeconds: 0.3,
                            CompletedPitStops: 0,
                            IsOnPitRoad: inPit[i]));
                    }
                }
                catch { }
            }

            return list;
        }

        private static TireWearSnapshot MapTires(dynamic? raw)
        {
            if (raw == null) return TireWearSnapshot.Unknown;
            try
            {
                double lf = AvgWear(raw, "LFwearL", "LFwearM", "LFwearR");
                double rf = AvgWear(raw, "RFwearL", "RFwearM", "RFwearR");
                double lr = AvgWear(raw, "LRwearL", "LRwearM", "LRwearR");
                double rr = AvgWear(raw, "RRwearL", "RRwearM", "RRwearR");
                bool available = !(lf == 1 && rf == 1 && lr == 1 && rr == 1);
                return new TireWearSnapshot(lf, rf, lr, rr, available);
            }
            catch { return TireWearSnapshot.Unknown; }
        }

        private static double AvgWear(dynamic raw, string a, string b, string c)
        {
            try
            {
                var t = raw.Telemetry;
                double va = (float)t[a]; double vb = (float)t[b]; double vc = (float)t[c];
                return (va + vb + vc) / 3.0;
            }
            catch { return 1.0; }
        }

        private static WeatherSnapshot MapWeather(dynamic? raw)
        {
            if (raw == null) return WeatherSnapshot.Dry;
            try
            {
                double track = (float)raw.Telemetry.TrackTempCrew;
                double air = (float)raw.Telemetry.AirTemp;
                double humidity = (float)raw.Telemetry.RelativeHumidity;
                double precip = SafeDouble(() => (float)raw.Telemetry.Precipitation);
                double skies = SafeDouble(() => (int)raw.Telemetry.Skies) / 3.0;
                bool wet = SafeBool(() => (bool)raw.Telemetry.WeatherDeclaredWet);
                return new WeatherSnapshot(track, air, humidity, skies, precip, precip > 0.05, wet);
            }
            catch { return WeatherSnapshot.Dry; }
        }

        // ── Small helpers that swallow exceptions so a single missing field doesn't kill the tick.
        private static double SafeDouble(Func<double> f, double fallback = 0)
        { try { return f(); } catch { return fallback; } }
        private static int SafeInt(Func<int> f, int fallback = 0)
        { try { return f(); } catch { return fallback; } }
        private static bool SafeBool(Func<bool> f, bool fallback = false)
        { try { return f(); } catch { return fallback; } }
        private static TimeSpan SafeTimeSpan(Func<object?> f)
        { try { var v = f(); return v is TimeSpan ts ? ts : TimeSpan.Zero; } catch { return TimeSpan.Zero; } }
    }
}
