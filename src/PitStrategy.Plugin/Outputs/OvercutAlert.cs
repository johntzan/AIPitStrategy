namespace PitStrategy.Core.Outputs
{
    /// <summary>
    /// Active when staying out (and pitting later) is expected to pass the named rival on
    /// their out-lap. Less useful in iRacing than in F1 due to lower tire-warmup penalties,
    /// but exposed for low-deg tracks (Monaco / Long Beach style).
    /// </summary>
    public sealed record OvercutAlert(
        int RivalCarIdx,
        double ExpectedGainSeconds,
        int OptimalOvercutLap);
}
