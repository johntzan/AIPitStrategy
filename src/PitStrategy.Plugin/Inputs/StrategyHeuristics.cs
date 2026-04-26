namespace PitStrategy.Core.Inputs
{
    /// <summary>
    /// Tunable thresholds and parameters for the rule-based decision tree. Defaults are
    /// chosen for iRacing road racing in the GT3 / LMP class range.
    /// </summary>
    public sealed record StrategyHeuristics(
        double UndercutThresholdSeconds = 1.5,
        double CleanAirThresholdSeconds = 3.0,
        int MaxStopsToConsider = 3,
        int MonteCarloTrials = 2000,
        double FcyPitTimeSavingsFraction = 0.55,
        int FuelRollingWindowLaps = 5,
        double FuelOutlierZScore = 2.5,
        int CleanAirLookaheadLaps = 5,
        double LowConfidenceRivalPaceStdDevSeconds = 1.0)
    {
        public static readonly StrategyHeuristics Default = new();
    }
}
