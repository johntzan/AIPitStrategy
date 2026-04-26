namespace PitStrategy.Core.Inputs
{
    /// <summary>
    /// All inputs needed to compute total pit-lane time loss. Populated from iRacing's
    /// SessionInfo YAML where available, otherwise from per-track or user-config defaults.
    /// </summary>
    public sealed record PitLossConfig(
        double PitLaneLengthMeters,
        double PitLaneSpeedLimitKph,
        double FuelFlowLitersPerSecond,         // car-class specific
        double TireChangeServiceSeconds,        // car-class specific (typically 12–16s)
        double PitEntryDeltaSeconds,            // sustained slow-down from race line into pit lane
        double PitExitDeltaSeconds,             // sustained slow-down from pit exit back up to race speed
        bool UsingDefaultPitLaneLength = false) // true when WeekendInfo didn't provide pit-lane length
    {
        /// <summary>Generic fallback values when telemetry doesn't supply them.</summary>
        public static readonly PitLossConfig GenericFallback = new(
            PitLaneLengthMeters: 350,
            PitLaneSpeedLimitKph: 60,
            FuelFlowLitersPerSecond: 2.5,
            TireChangeServiceSeconds: 14.0,
            PitEntryDeltaSeconds: 2.0,
            PitExitDeltaSeconds: 2.0,
            UsingDefaultPitLaneLength: true);
    }
}
