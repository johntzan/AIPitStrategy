using System;

namespace PitStrategy.Core.Inputs
{
    /// <summary>
    /// Snapshot of one competitor at the current tick. Built from iRacing's CarIdx* arrays.
    /// </summary>
    public sealed record RivalState(
        int CarIdx,
        int Position,
        int ClassPosition,
        int ClassId,
        int LapsCompleted,
        double LapDistPct,                 // 0.0–1.0
        TimeSpan GapToPlayerSeconds,       // signed: positive = rival is ahead on track
        TimeSpan LastLapTime,
        TimeSpan AverageRecentLapTime,     // rolling avg of last 3 green laps; equals LastLapTime if unknown
        double RecentLapTimeStdDevSeconds, // 0 when unknown — used to dampen confidence
        int CompletedPitStops,
        bool IsOnPitRoad);
}
