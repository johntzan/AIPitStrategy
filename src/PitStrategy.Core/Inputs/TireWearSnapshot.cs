using System;

namespace PitStrategy.Core.Inputs
{
    /// <summary>
    /// Per-corner remaining tread fraction (0.0 = bald, 1.0 = brand-new). iRacing exposes
    /// three values per corner (inner / middle / outer); this snapshot averages them.
    /// </summary>
    public sealed record TireWearSnapshot(
        double FrontLeftPct,
        double FrontRightPct,
        double RearLeftPct,
        double RearRightPct,
        bool IsAvailable)
    {
        /// <summary>Default snapshot used when the car doesn't expose tire wear telemetry.</summary>
        public static readonly TireWearSnapshot Unknown = new(1.0, 1.0, 1.0, 1.0, IsAvailable: false);

        public double Min =>
            Math.Min(Math.Min(FrontLeftPct, FrontRightPct), Math.Min(RearLeftPct, RearRightPct));

        public double Mean =>
            (FrontLeftPct + FrontRightPct + RearLeftPct + RearRightPct) / 4.0;
    }
}
