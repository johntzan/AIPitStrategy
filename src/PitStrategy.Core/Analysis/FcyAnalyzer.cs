using PitStrategy.Core.Inputs;
using PitStrategy.Core.Outputs;

namespace PitStrategy.Core.Analysis
{
    /// <summary>
    /// Detects a Full-Course Yellow window when pits are open. Surfaces the time delta vs
    /// pitting under green flag (~45% of green-flag pit cost, by default heuristic).
    /// </summary>
    public static class FcyAnalyzer
    {
        public static FcyOpportunity? Evaluate(RaceState state, RaceConfig config, PitLossBreakdown loss)
        {
            if (!state.IsUnderFcy || !state.PitsOpen) return null;

            var heuristics = config.ResolvedHeuristics;
            double savings = loss.TotalSeconds * heuristics.FcyPitTimeSavingsFraction;
            if (savings < 0.5) return null;

            return new FcyOpportunity(
                ShouldPitNow: true,
                TimeSavingsSeconds: savings,
                FuelSavingsLitersIfShortFill: 0,    // refined when fuel-saver mode is on (Phase 3)
                Rationale: $"FCY pit ≈ {(loss.TotalSeconds - savings):0.0}s vs " +
                           $"green-flag {loss.TotalSeconds:0.0}s — save ~{savings:0.0}s.");
        }
    }
}
