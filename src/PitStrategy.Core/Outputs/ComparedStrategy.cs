using System;
using System.Collections.Generic;

namespace PitStrategy.Core.Outputs
{
    /// <summary>
    /// One candidate strategy in the diagnostic 1-stop / 2-stop / 3-stop comparison panel.
    /// Populated only when Monte Carlo simulation has run (Phase 3). Phase 1 and 2 may
    /// emit a 1-stop entry derived deterministically from the pit window.
    /// </summary>
    public sealed record ComparedStrategy(
        string Name,
        int StopCount,
        IReadOnlyList<int> StopLaps,
        TimeSpan PredictedFinishTime,
        TimeSpan DeltaToBest,
        double FinishPositionMean,
        double WinProbability)
    {
        public static readonly ComparedStrategy Empty = new(
            Name: "(none)",
            StopCount: 0,
            StopLaps: Array.Empty<int>(),
            PredictedFinishTime: TimeSpan.Zero,
            DeltaToBest: TimeSpan.Zero,
            FinishPositionMean: 0,
            WinProbability: 0);
    }
}
