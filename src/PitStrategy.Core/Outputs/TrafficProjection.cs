using System;
using System.Collections.Generic;

namespace PitStrategy.Core.Outputs
{
    /// <summary>
    /// Projected traffic situation at the moment the player exits the pit lane on a
    /// candidate pit lap. Populated by <c>RejoinPredictor</c> in Phase 2.
    /// </summary>
    public sealed record TrafficProjection(
        int RejoinPosition,
        int RejoinClassPosition,
        double GapToCarAheadSeconds,
        double GapToCarBehindSeconds,
        bool IsCleanAir,
        IReadOnlyList<RivalRejoinState> NearbyRivals,
        int? CleanAirLapIfWait,           // null if no nearby lap yields clean air
        bool IsAvailable)                  // false in Phase 1 / when rivals are absent
    {
        public static readonly TrafficProjection Empty = new(
            RejoinPosition: 0,
            RejoinClassPosition: 0,
            GapToCarAheadSeconds: double.PositiveInfinity,
            GapToCarBehindSeconds: double.PositiveInfinity,
            IsCleanAir: true,
            NearbyRivals: Array.Empty<RivalRejoinState>(),
            CleanAirLapIfWait: null,
            IsAvailable: false);
    }
}
