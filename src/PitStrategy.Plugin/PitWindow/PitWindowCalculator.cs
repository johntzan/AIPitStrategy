using System;
using PitStrategy.Core.Inputs;

namespace PitStrategy.Core.PitWindow
{
    /// <summary>
    /// Computes the feasible single-pit-stop window given a race state and config.
    /// Multi-stop strategies (Phase 3 Monte Carlo) are computed separately; this calculator
    /// is the workhorse for the headline 1-stop case.
    ///
    /// Math, in plain English:
    ///   • <c>lapsRemaining</c> comes from <c>SessionLapsRemainEx</c> if the race is lap-counted,
    ///     otherwise from <c>SessionTimeRemain / avgLapTime</c> (+1 for "timed plus one lap").
    ///   • <c>lapsToEmpty</c> = <c>fuelLevel / fuelPerLap</c>. Apply the safety margin.
    ///   • If <c>lapsToEmpty - safetyMargin &gt;= lapsRemaining</c>: no pit needed.
    ///   • Otherwise compute earliest/latest legal pit laps:
    ///       latest  = currentLap + (lapsToEmpty - safetyMargin)
    ///       earliest = totalLapsAtFinish - (tankCapacity / fuelPerLap - safetyMargin)
    ///                  but never before the current lap.
    ///   • <c>minStops</c> = <c>ceil(totalFuelNeeded / (tankCapacity * 0.95))</c>. The 0.95 leaves
    ///     a small ullage allowance (you can never actually use the full last drop of the tank).
    /// </summary>
    public static class PitWindowCalculator
    {
        public static PitWindow Compute(RaceState state, RaceConfig config, TimeSpan? averageLapTime = null)
        {
            if (state.HasInsufficientData) return PitWindow.Unknown;

            double fuelPerLap = state.RollingFuelPerLap;
            if (fuelPerLap <= 1e-6) return PitWindow.Unknown;

            double avgLapSeconds = ResolveAvgLapSeconds(state, averageLapTime);
            double lapsRemaining = ResolveLapsRemaining(state, config, avgLapSeconds);
            if (double.IsNaN(lapsRemaining) || lapsRemaining < 0) return PitWindow.Unknown;

            double safetyMargin = config.FuelSafetyMarginLaps;
            double lapsToEmpty = state.FuelLevelLiters / fuelPerLap;
            double lapsToEmptySafe = lapsToEmpty - safetyMargin;

            // Total fuel still needed to finish (independent of pit stops).
            double totalFuelNeeded = lapsRemaining * fuelPerLap + safetyMargin * fuelPerLap;
            double effectiveTank = config.TankCapacityLiters * 0.95;
            int minStops = effectiveTank <= 1e-6
                ? int.MaxValue
                : (int)Math.Max(0, Math.Ceiling(
                      Math.Max(0, totalFuelNeeded - state.FuelLevelLiters) / effectiveTank));

            // No pit needed: current fuel covers the rest of the race with margin.
            if (lapsToEmptySafe >= lapsRemaining)
            {
                return new PitWindow(
                    EarliestLap: null,
                    LatestLap: null,
                    RecommendedLap: null,
                    MinStops: 0,
                    LapsRemainingEstimated: lapsRemaining,
                    LapsToEmpty: lapsToEmpty,
                    LapsToEmptyAtSafetyMargin: lapsToEmptySafe,
                    RequiresPit: false,
                    HasEnoughData: true,
                    IsForcedPitLap: false);
            }

            // Single pit stop window.
            double finishLap = state.CurrentLap + lapsRemaining;
            double tankCoverageLaps = effectiveTank / fuelPerLap;

            double latestF = state.CurrentLap + lapsToEmptySafe;
            double earliestF = finishLap - tankCoverageLaps + safetyMargin;

            // Clamp earliest to the current lap (you can't pit in the past).
            if (earliestF < state.CurrentLap) earliestF = state.CurrentLap;
            // Clamp latest to the finish (no point pitting on the last lap).
            if (latestF > finishLap - 1) latestF = Math.Max(state.CurrentLap, finishLap - 1);

            int earliest = (int)Math.Ceiling(earliestF);
            int latest = (int)Math.Floor(latestF);
            bool forced = latest <= state.CurrentLap;

            int recommended = Math.Max(earliest, Math.Min(latest, (earliest + latest) / 2));

            return new PitWindow(
                EarliestLap: earliest,
                LatestLap: latest,
                RecommendedLap: recommended,
                MinStops: Math.Max(1, minStops),
                LapsRemainingEstimated: lapsRemaining,
                LapsToEmpty: lapsToEmpty,
                LapsToEmptyAtSafetyMargin: lapsToEmptySafe,
                RequiresPit: true,
                HasEnoughData: true,
                IsForcedPitLap: forced);
        }

        private static double ResolveAvgLapSeconds(RaceState state, TimeSpan? averageLapTime)
        {
            if (averageLapTime.HasValue && averageLapTime.Value > TimeSpan.Zero)
                return averageLapTime.Value.TotalSeconds;
            if (state.RecentLapTimes != null && state.RecentLapTimes.Count > 0)
            {
                double sum = 0; int count = 0;
                foreach (var t in state.RecentLapTimes)
                {
                    if (t > TimeSpan.Zero) { sum += t.TotalSeconds; count++; }
                }
                if (count > 0) return sum / count;
            }
            if (state.LastLapTime > TimeSpan.Zero) return state.LastLapTime.TotalSeconds;
            if (state.BestLapTime > TimeSpan.Zero) return state.BestLapTime.TotalSeconds;
            return 90.0; // fallback to a generic 1:30 if we truly have nothing
        }

        private static double ResolveLapsRemaining(RaceState state, RaceConfig config, double avgLapSeconds)
        {
            // Prefer iRacing's authoritative count when present.
            if (state.LapsRemaining.HasValue) return state.LapsRemaining.Value;

            switch (config.Length)
            {
                case RaceLength.Laps laps:
                    return Math.Max(0, laps.TotalLaps - state.CurrentLap);

                case RaceLength.Timed timed:
                {
                    double remainingSeconds = state.SessionTimeRemaining?.TotalSeconds
                                              ?? Math.Max(0, timed.Duration.TotalSeconds - state.SessionTimeElapsed.TotalSeconds);
                    return remainingSeconds / Math.Max(avgLapSeconds, 1e-6);
                }

                case RaceLength.TimedPlusOneLap timedPlus:
                {
                    double remainingSeconds = state.SessionTimeRemaining?.TotalSeconds
                                              ?? Math.Max(0, timedPlus.Duration.TotalSeconds - state.SessionTimeElapsed.TotalSeconds);
                    // +1 lap once the timer expires.
                    return Math.Ceiling(remainingSeconds / Math.Max(avgLapSeconds, 1e-6)) + 1;
                }

                default:
                    return double.NaN;
            }
        }
    }
}
