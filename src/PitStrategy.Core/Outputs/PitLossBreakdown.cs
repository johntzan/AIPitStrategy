using System;

namespace PitStrategy.Core.Outputs
{
    /// <summary>
    /// Decomposed pit-lane time-loss in seconds, for the next stop given the currently
    /// armed services. Pit fuel and tire services run in parallel — the longer of the two
    /// dominates. Travel and entry/exit deltas always apply.
    /// </summary>
    public sealed record PitLossBreakdown(
        double PitLaneTravelSeconds,
        double FuelServiceSeconds,
        double TireServiceSeconds,
        double EntryExitDeltaSeconds,
        double TotalSeconds)
    {
        public double ServicesParallelSeconds => Math.Max(FuelServiceSeconds, TireServiceSeconds);

        public static readonly PitLossBreakdown Empty = new(0, 0, 0, 0, 0);
    }
}
