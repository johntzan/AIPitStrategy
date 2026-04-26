namespace PitStrategy.Core.Outputs
{
    /// <summary>
    /// The headline output the dashboard renders. Exactly one of these per tick.
    /// </summary>
    public enum PitDecisionKind
    {
        /// <summary>Insufficient data (e.g. no green laps yet) — engine cannot recommend.</summary>
        Unknown,

        /// <summary>Pit at the end of the current lap.</summary>
        PitNow,

        /// <summary>Wait one lap, then pit (clean-air or undercut window opens shortly).</summary>
        PitNextLap,

        /// <summary>Stay out — pit window not yet optimal.</summary>
        StayOut,

        /// <summary>Lift-and-coast / fuel-save mode advised; pit window is too far away to commit.</summary>
        SaveFuel,

        /// <summary>Forced pit — fuel will run out before any other window opens.</summary>
        PitForFuelOnly,
    }
}
