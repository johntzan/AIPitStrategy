using System;
using System.Collections.Generic;
using PitStrategy.Core.Inputs;
using PitStrategy.Core.Outputs;

namespace PitStrategy.Core.Traffic
{
    /// <summary>
    /// Projects where the player will rejoin the field after a pit on a given candidate
    /// lap, and which rivals will be near them in seconds.
    ///
    /// Returns <see cref="TrafficProjection.Empty"/> when no rival data is available.
    /// </summary>
    public static class RejoinPredictor
    {
        /// <summary>
        /// Approximate fraction of a lap the pit-exit point sits past the start-finish line.
        /// Tracks vary; 5% is a reasonable generic default and matches the dashboard model.
        /// </summary>
        private const double PitExitLapDistPct = 0.05;

        /// <summary>Window (seconds) around the rejoin point we consider "nearby" for dashboard rendering.</summary>
        private const double NearbyWindowSeconds = 5.0;

        public static TrafficProjection Project(
            RaceState state, RaceConfig config, int candidatePitLap, double pitLossSeconds)
        {
            if (state.Rivals == null || state.Rivals.Count == 0) return TrafficProjection.Empty;

            var heuristics = config.ResolvedHeuristics;
            double playerLapSeconds = ResolvePlayerLapSeconds(state);
            int lapsUntilCandidate = Math.Max(0, candidatePitLap - state.CurrentLap);

            var instantaneous = ProjectAtLap(
                state, lapsUntilCandidate, playerLapSeconds, pitLossSeconds, heuristics);

            int? cleanAirLap = null;
            if (!instantaneous.IsCleanAir)
            {
                for (int delta = 1; delta <= heuristics.CleanAirLookaheadLaps; delta++)
                {
                    var future = ProjectAtLap(
                        state, lapsUntilCandidate + delta, playerLapSeconds, pitLossSeconds, heuristics);
                    if (future.IsCleanAir)
                    {
                        cleanAirLap = state.CurrentLap + lapsUntilCandidate + delta;
                        break;
                    }
                }
            }

            return instantaneous with { CleanAirLapIfWait = cleanAirLap };
        }

        private static TrafficProjection ProjectAtLap(
            RaceState state, int lapsAhead, double playerLapSeconds,
            double pitLossSeconds, StrategyHeuristics heuristics)
        {
            // Total elapsed seconds (in race time) until the player exits the pit lane on
            // the candidate-future pit lap: stay-out laps × player pace + pit loss.
            double totalElapsedSeconds = lapsAhead * playerLapSeconds + pitLossSeconds;

            int aheadCount = 0;
            int aheadClassCount = 0;
            double gapAhead = double.PositiveInfinity;
            double gapBehind = double.PositiveInfinity;
            var nearby = new List<RivalRejoinState>();

            foreach (var r in state.Rivals)
            {
                double rivalLap = r.AverageRecentLapTime > TimeSpan.Zero
                    ? r.AverageRecentLapTime.TotalSeconds
                    : (r.LastLapTime > TimeSpan.Zero ? r.LastLapTime.TotalSeconds : playerLapSeconds);
                if (rivalLap <= 1e-3) rivalLap = playerLapSeconds;

                double advancement = totalElapsedSeconds / rivalLap;
                double rivalProjectedPct = Wrap01(r.LapDistPct + advancement);

                double gapPct = rivalProjectedPct - PitExitLapDistPct;
                if (gapPct > 0.5) gapPct -= 1.0;
                if (gapPct < -0.5) gapPct += 1.0;

                double gapSec = gapPct * rivalLap;
                bool isAhead = gapPct > 0;

                if (isAhead)
                {
                    aheadCount++;
                    if (r.ClassId == state.PlayerClassId) aheadClassCount++;
                    if (gapSec < gapAhead) gapAhead = gapSec;
                }
                else
                {
                    double absGap = Math.Abs(gapSec);
                    if (absGap < gapBehind) gapBehind = absGap;
                }

                if (Math.Abs(gapSec) <= NearbyWindowSeconds)
                {
                    nearby.Add(new RivalRejoinState(
                        CarIdx: r.CarIdx,
                        Position: r.Position,
                        GapSeconds: gapSec,
                        IsAhead: isAhead,
                        WillBePitting: false));
                }
            }

            nearby.Sort((a, b) => a.GapSeconds.CompareTo(b.GapSeconds));

            int rejoinPosition = aheadCount + 1;
            int rejoinClassPosition = aheadClassCount + 1;

            bool isCleanAir = gapAhead >= heuristics.CleanAirThresholdSeconds
                              && gapBehind >= heuristics.CleanAirThresholdSeconds;

            return new TrafficProjection(
                RejoinPosition: rejoinPosition,
                RejoinClassPosition: rejoinClassPosition,
                GapToCarAheadSeconds: gapAhead,
                GapToCarBehindSeconds: gapBehind,
                IsCleanAir: isCleanAir,
                NearbyRivals: nearby,
                CleanAirLapIfWait: null,
                IsAvailable: true);
        }

        private static double ResolvePlayerLapSeconds(RaceState state)
        {
            if (state.LastLapTime > TimeSpan.Zero) return state.LastLapTime.TotalSeconds;
            if (state.RecentLapTimes != null)
            {
                double sum = 0; int n = 0;
                foreach (var t in state.RecentLapTimes)
                {
                    if (t > TimeSpan.Zero) { sum += t.TotalSeconds; n++; }
                }
                if (n > 0) return sum / n;
            }
            if (state.BestLapTime > TimeSpan.Zero) return state.BestLapTime.TotalSeconds;
            return 90.0;
        }

        private static double Wrap01(double x)
        {
            x %= 1.0;
            if (x < 0) x += 1.0;
            return x;
        }
    }
}
