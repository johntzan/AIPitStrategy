namespace PitStrategy.Core.PitWindow
{
    /// <summary>
    /// Computed feasible pit-stop window for a single mandatory or strategic stop.
    ///
    /// <para><see cref="EarliestLap"/> is the earliest lap where pitting allows a full
    /// post-pit tank to reach the finish (with safety margin). <see cref="LatestLap"/> is
    /// the latest lap where the pre-pit fuel still suffices (with safety margin).</para>
    /// </summary>
    public sealed record PitWindow(
        int? EarliestLap,
        int? LatestLap,
        int? RecommendedLap,
        int MinStops,
        double LapsRemainingEstimated,
        double LapsToEmpty,
        double LapsToEmptyAtSafetyMargin,
        bool RequiresPit,
        bool HasEnoughData,
        bool IsForcedPitLap)              // true when LatestLap is in the past or this lap
    {
        public static readonly PitWindow Unknown = new(
            EarliestLap: null,
            LatestLap: null,
            RecommendedLap: null,
            MinStops: 0,
            LapsRemainingEstimated: double.NaN,
            LapsToEmpty: double.NaN,
            LapsToEmptyAtSafetyMargin: double.NaN,
            RequiresPit: false,
            HasEnoughData: false,
            IsForcedPitLap: false);
    }
}
