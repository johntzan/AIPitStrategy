using System;
using PitStrategy.Core.Inputs;
using PitStrategy.Core.Outputs;

namespace PitStrategy.Core.Pit
{
    /// <summary>
    /// Computes total pit-lane time loss and its components for a given track config and
    /// service request. Pure — no race state required.
    ///
    /// Travel time and entry/exit deltas always apply. Refuel and tire changes happen in
    /// parallel inside the box, so the longer of the two dominates that segment. A repair
    /// (if armed) is sequential and added on top.
    /// </summary>
    public static class PitLossModel
    {
        /// <summary>Convert km/h to m/s.</summary>
        private const double KphToMps = 1000.0 / 3600.0;

        public static PitLossBreakdown Compute(PitLossConfig config, PitServiceRequest service)
        {
            double speedMps = Math.Max(config.PitLaneSpeedLimitKph * KphToMps, 1e-6);
            double travel = config.PitLaneLengthMeters / speedMps;

            double fuelTime = service.RefuelArmed && service.FuelToAddLiters > 0
                ? service.FuelToAddLiters / Math.Max(config.FuelFlowLitersPerSecond, 1e-6)
                : 0;

            double tireTime = service.TireChangeArmed
                ? config.TireChangeServiceSeconds
                : 0;

            double parallelService = Math.Max(fuelTime, tireTime);

            double entryExit = config.PitEntryDeltaSeconds + config.PitExitDeltaSeconds;

            double repair = service.RepairArmed ? service.RepairTimeSeconds : 0;

            double total = travel + parallelService + entryExit + repair;

            return new PitLossBreakdown(
                PitLaneTravelSeconds: travel,
                FuelServiceSeconds: fuelTime,
                TireServiceSeconds: tireTime,
                EntryExitDeltaSeconds: entryExit,
                TotalSeconds: total);
        }
    }
}
