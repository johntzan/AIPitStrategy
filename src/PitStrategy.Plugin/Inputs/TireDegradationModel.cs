namespace PitStrategy.Core.Inputs
{
    /// <summary>
    /// Heuristic two-phase tire-degradation model: a small linear loss per lap, then a
    /// gaussian-onset "cliff" where loss accelerates. Calibrated per car class / compound.
    /// Purely informational in Phase 1 (we don't penalize the decision tree on tire age yet);
    /// becomes load-bearing in the Monte Carlo simulator (Phase 3).
    /// </summary>
    public sealed record TireDegradationModel(
        double LinearLossPerLapSeconds,    // typically 0.02–0.10 s/lap
        double CliffLapMean,               // gaussian peak of cliff onset (laps on tires)
        double CliffLapStdDev,
        double CliffLossPerLapSeconds)     // typically 0.5–2.0 s/lap once cliff hits
    {
        public static readonly TireDegradationModel Default = new(
            LinearLossPerLapSeconds: 0.04,
            CliffLapMean: 30,
            CliffLapStdDev: 4,
            CliffLossPerLapSeconds: 0.8);

        public static readonly TireDegradationModel LowDeg = new(0.02, 50, 6, 0.4);
        public static readonly TireDegradationModel HighDeg = new(0.08, 18, 3, 1.6);
    }
}
