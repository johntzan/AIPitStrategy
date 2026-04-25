using System;
using System.Collections.Generic;

namespace PitStrategy.Core.Outputs
{
    /// <summary>
    /// Headline engine output. The dashboard's "PIT NOW / STAY OUT" card renders
    /// <see cref="Kind"/>, <see cref="Confidence"/> and <see cref="PrimaryReason"/>;
    /// everything else feeds the diagnostic panel.
    /// </summary>
    public sealed record PitDecision(
        PitDecisionKind Kind,
        double Confidence,                    // 0.0–1.0
        int? RecommendedPitLap,
        PitLossBreakdown LossBreakdown,
        TrafficProjection TrafficAfterPit,
        UndercutAlert? Undercut,
        FcyOpportunity? Fcy,
        string PrimaryReason,
        IReadOnlyList<string> SecondaryFactors,
        TimeSpan ComputeTime,
        // ── Diagnostic / dashboard-friendly numbers ──
        double LapsToEmpty,
        double LapsToEmptyAtSafetyMargin,
        double MinFuelToAddLiters,
        int StintsRemaining,
        IReadOnlyList<ComparedStrategy> ComparedStrategies)
    {
        public static PitDecision Unknown(string reason, TimeSpan computeTime) =>
            new(
                Kind: PitDecisionKind.Unknown,
                Confidence: 0,
                RecommendedPitLap: null,
                LossBreakdown: PitLossBreakdown.Empty,
                TrafficAfterPit: TrafficProjection.Empty,
                Undercut: null,
                Fcy: null,
                PrimaryReason: reason,
                SecondaryFactors: Array.Empty<string>(),
                ComputeTime: computeTime,
                LapsToEmpty: double.NaN,
                LapsToEmptyAtSafetyMargin: double.NaN,
                MinFuelToAddLiters: 0,
                StintsRemaining: 0,
                ComparedStrategies: Array.Empty<ComparedStrategy>());
    }
}
