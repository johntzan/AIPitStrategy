using System;
using System.Collections.Generic;
using System.Linq;
using PitStrategy.Core.Util;

namespace PitStrategy.Core.Fuel
{
    /// <summary>
    /// Rolling-window fuel-per-lap tracker. Drives the engine's "laps to empty" / "fuel
    /// to add" calculations.
    ///
    /// Why this design: iRacing's <c>FuelUsePerHour</c> is too noisy to use directly, so we
    /// derive consumption from <c>FuelLevel</c> deltas at lap boundaries. We drop yellow-flag
    /// laps (consumption is wildly different behind a pace car) and reject outliers by Z-score.
    /// Pit stops are detected automatically (positive fuel delta) and don't pollute the average.
    /// </summary>
    public sealed class FuelTracker
    {
        private readonly int _windowLaps;
        private readonly double _outlierZScore;
        private readonly RollingWindow<LapSample> _samples;

        private int _lastLapNumber = -1;
        private double _fuelAtLastLapBoundary;
        private double _fuelAtTickStart;
        private bool _hasFuelAtTickStart;
        private bool _hasLastLapBoundary;
        private bool _yellowSeenThisLap;
        private int _outliersRejected;

        public FuelTracker(int windowLaps = 5, double outlierZScore = 2.5)
        {
            if (windowLaps < 1) throw new ArgumentOutOfRangeException(nameof(windowLaps));
            _windowLaps = windowLaps;
            _outlierZScore = outlierZScore;
            _samples = new RollingWindow<LapSample>(windowLaps);
        }

        public int SamplesInWindow => _samples.Count;
        public int OutliersRejected => _outliersRejected;
        public double LastLapFuelUsed { get; private set; }

        /// <summary>
        /// Simple arithmetic mean of valid samples in the rolling window. Returns NaN
        /// before the first valid sample is observed.
        /// </summary>
        public double RollingAverageFuelPerLap =>
            _samples.Count == 0 ? double.NaN : _samples.Average(s => s.FuelUsedLiters);

        /// <summary>
        /// Weighted mean with linear weights — most recent sample weighted N, oldest weighted 1.
        /// More responsive to a step-change like fuel-saving mode being toggled on.
        /// </summary>
        public double WeightedAverageFuelPerLap
        {
            get
            {
                if (_samples.Count == 0) return double.NaN;
                var snapshot = _samples.Snapshot();
                double num = 0, den = 0;
                for (int i = 0; i < snapshot.Count; i++)
                {
                    double weight = i + 1; // oldest=1, newest=N
                    num += snapshot[i].FuelUsedLiters * weight;
                    den += weight;
                }
                return num / den;
            }
        }

        /// <summary>
        /// True once we have at least one valid (green-flag, non-pit-out) sample.
        /// The engine refuses to predict before this is true.
        /// </summary>
        public bool HasEnoughDataForPrediction => _samples.Count >= 1;

        /// <summary>
        /// Drop all samples and lap-boundary state. Use on driver swap or a session restart.
        /// </summary>
        public void Reset()
        {
            _samples.Clear();
            _lastLapNumber = -1;
            _fuelAtLastLapBoundary = 0;
            _hasLastLapBoundary = false;
            _hasFuelAtTickStart = false;
            _yellowSeenThisLap = false;
            _outliersRejected = 0;
            LastLapFuelUsed = 0;
        }

        /// <summary>
        /// Direct lap-boundary entry point. Use this from tests or any code that already
        /// knows when a lap completed.
        /// </summary>
        public void OnLapCompleted(int lap, double fuelAtLapStart, double fuelAtLapEnd,
                                   TimeSpan lapTime, bool wasUnderYellow)
        {
            // Pit stop detected: fuel went up. Skip this lap from the average.
            if (fuelAtLapEnd > fuelAtLapStart)
            {
                LastLapFuelUsed = 0;
                return;
            }

            double used = fuelAtLapStart - fuelAtLapEnd;
            LastLapFuelUsed = used;

            if (used < 0) return;        // sanity: ignore negative
            if (wasUnderYellow) return;  // yellow-flag laps don't reflect race-pace burn
            if (used < 0.01) return;     // numerical noise / paused car

            if (IsOutlier(used))
            {
                _outliersRejected++;
                return;
            }

            _samples.Add(new LapSample(lap, used, lapTime));
        }

        /// <summary>
        /// Tick-driven entry point used by the SimHub plugin. Detects lap boundaries
        /// internally by watching <paramref name="currentLap"/> increment.
        /// </summary>
        public void OnTick(int currentLap, double currentFuelLiters, bool isOnPitRoad,
                           bool isUnderYellow, TimeSpan lastLapTime)
        {
            // Track yellow inside the lap so a brief FCY taints the whole lap.
            if (isUnderYellow) _yellowSeenThisLap = true;

            // Skip sampling while the player is on pit road — refuel inflates the level.
            if (isOnPitRoad)
            {
                _hasFuelAtTickStart = false;
                _fuelAtTickStart = 0;
                // We still need to update the boundary fuel once the player rejoins,
                // so don't overwrite _fuelAtLastLapBoundary here.
                _hasLastLapBoundary = false;
                return;
            }

            if (!_hasFuelAtTickStart)
            {
                _fuelAtTickStart = currentFuelLiters;
                _hasFuelAtTickStart = true;
            }

            // First call: prime lap tracking, no sample yet.
            if (_lastLapNumber < 0)
            {
                _lastLapNumber = currentLap;
                _fuelAtLastLapBoundary = currentFuelLiters;
                _hasLastLapBoundary = true;
                _yellowSeenThisLap = false;
                return;
            }

            // Lap boundary crossed.
            if (currentLap > _lastLapNumber)
            {
                if (_hasLastLapBoundary)
                {
                    OnLapCompleted(
                        lap: currentLap - 1,
                        fuelAtLapStart: _fuelAtLastLapBoundary,
                        fuelAtLapEnd: currentFuelLiters,
                        lapTime: lastLapTime,
                        wasUnderYellow: _yellowSeenThisLap);
                }
                _lastLapNumber = currentLap;
                _fuelAtLastLapBoundary = currentFuelLiters;
                _hasLastLapBoundary = true;
                _yellowSeenThisLap = isUnderYellow; // start the new lap clean
            }
        }

        /// <summary>Explicit notification (optional) that a refuel happened.</summary>
        public void OnPitStop(double fuelAddedLiters)
        {
            // Same effect as the auto-detection above, but lets the caller signal it
            // when the boundary detection might miss it (e.g. multi-pit-stop in one tick batch).
            _hasLastLapBoundary = false;
            _hasFuelAtTickStart = false;
            _yellowSeenThisLap = false;
        }

        /// <summary>
        /// Estimated laps until the tank is empty. NaN before the tracker has data.
        /// </summary>
        public double LapsToEmpty(double currentFuelLiters)
        {
            double avg = RollingAverageFuelPerLap;
            if (double.IsNaN(avg) || avg <= 1e-6) return double.NaN;
            return currentFuelLiters / avg;
        }

        private bool IsOutlier(double candidate)
        {
            if (_samples.Count < 3) return false; // not enough history to judge
            var values = _samples.Select(s => s.FuelUsedLiters).ToArray();
            return Math.Abs(Statistics.ZScore(candidate, values)) > _outlierZScore;
        }

        private readonly struct LapSample
        {
            public LapSample(int lap, double fuelUsedLiters, TimeSpan lapTime)
            {
                Lap = lap; FuelUsedLiters = fuelUsedLiters; LapTime = lapTime;
            }
            public int Lap { get; }
            public double FuelUsedLiters { get; }
            public TimeSpan LapTime { get; }
        }
    }
}
