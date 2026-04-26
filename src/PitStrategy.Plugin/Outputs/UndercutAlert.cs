namespace PitStrategy.Core.Outputs
{
    /// <summary>
    /// Active when pitting now is expected to pass the named rival via fresh-tire / cold-fuel pace
    /// before they can pit. <see cref="ExpectedGainSeconds"/> is positive when the player gains.
    /// </summary>
    public sealed record UndercutAlert(
        int RivalCarIdx,
        int RivalPosition,
        double ExpectedGainSeconds,
        int OptimalUndercutLap,
        string Rationale);
}
