using System;
using PitStrategy.Core.Inputs;
using PitStrategy.Core.Outputs;

namespace PitStrategy.Core.Analysis
{
    /// <summary>
    /// Detects when pitting now would let the player pass the rival immediately ahead via
    /// fresh-tire pace before that rival pits. Returns null when no qualifying rival exists.
    /// </summary>
    public static class UndercutDetector
    {
        /// <summary>Assumed lap-time advantage on fresh tires vs a rival on older rubber.</summary>
        private const double FreshTirePaceAdvantageSeconds = 0.5;

        /// <summary>How many laps the rival is assumed to stay out before they pit too.</summary>
        private const int RivalStayOutLaps = 3;

        public static UndercutAlert? Evaluate(RaceState state, RaceConfig config, PitLossBreakdown loss)
        {
            if (state.Rivals == null || state.Rivals.Count == 0) return null;
            if (state.PlayerPosition <= 1) return null;

            var heuristics = config.ResolvedHeuristics;
            int targetPosition = state.PlayerPosition - 1;

            RivalState? target = null;
            foreach (var r in state.Rivals)
            {
                // Same class only — undercutting a faster class is meaningless for class wins.
                if (r.ClassId != 0 && r.ClassId != state.PlayerClassId) continue;
                if (r.Position == targetPosition) { target = r; break; }
            }
            if (target == null) return null;

            double gap = Math.Abs(target.GapToPlayerSeconds.TotalSeconds);

            // Already pitted more recently than us — no undercut possible.
            if (target.CompletedPitStops > state.CompletedPitStops) return null;

            // Too far ahead to undercut.
            if (gap > heuristics.UndercutThresholdSeconds) return null;

            double expectedGain = RivalStayOutLaps * FreshTirePaceAdvantageSeconds - gap;
            if (expectedGain <= 0) return null;

            return new UndercutAlert(
                RivalCarIdx: target.CarIdx,
                RivalPosition: target.Position,
                ExpectedGainSeconds: expectedGain,
                OptimalUndercutLap: state.CurrentLap,
                Rationale: $"Gap {gap:0.0}s; assume rival stays out {RivalStayOutLaps} laps; " +
                           $"fresh-tire advantage ~{FreshTirePaceAdvantageSeconds:0.0}s/lap.");
        }
    }
}
