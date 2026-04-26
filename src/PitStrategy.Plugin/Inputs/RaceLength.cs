using System;

namespace PitStrategy.Core.Inputs
{
    /// <summary>
    /// Discriminated-union-style abstract base for the three iRacing race-length kinds.
    /// Use pattern matching on the derived types to handle each case.
    /// </summary>
    public abstract record RaceLength
    {
        private RaceLength() { }

        public sealed record Laps(int TotalLaps) : RaceLength;
        public sealed record Timed(TimeSpan Duration) : RaceLength;

        /// <summary>iRacing's default race format: race ends when the leader crosses the line on or after the timer expires.</summary>
        public sealed record TimedPlusOneLap(TimeSpan Duration) : RaceLength;
    }
}
