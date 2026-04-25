namespace PitStrategy.Core.Inputs
{
    /// <summary>
    /// Per-session configuration: race length, vehicle / track parameters, and tunable
    /// strategy heuristics. Stable across the race; populated once at session start.
    /// </summary>
    public sealed record RaceConfig(
        RaceLength Length,
        double TankCapacityLiters,
        PitLossConfig PitLoss,
        bool TireChangesAllowed = true,
        bool RefuelingAllowed = true,
        double FuelSafetyMarginLaps = 0.5,
        TireDegradationModel? TireModel = null,
        StrategyHeuristics? Heuristics = null)
    {
        public TireDegradationModel ResolvedTireModel =>
            TireModel ?? TireDegradationModel.Default;
        public StrategyHeuristics ResolvedHeuristics =>
            Heuristics ?? StrategyHeuristics.Default;
    }
}
