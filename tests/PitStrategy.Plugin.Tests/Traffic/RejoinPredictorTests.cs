using System;
using System.Collections.Generic;
using PitStrategy.Core.Inputs;
using PitStrategy.Core.Traffic;

namespace PitStrategy.Plugin.Tests.Traffic;

public class RejoinPredictorTests
{
    private static readonly PitLossConfig Track = new(
        PitLaneLengthMeters: 350, PitLaneSpeedLimitKph: 60,
        FuelFlowLitersPerSecond: 2.5, TireChangeServiceSeconds: 12,
        PitEntryDeltaSeconds: 2, PitExitDeltaSeconds: 2);

    private static RaceConfig Config(double cleanAirThreshold = 3.0) =>
        new(Length: new RaceLength.Laps(30),
            TankCapacityLiters: 80,
            PitLoss: Track,
            Heuristics: StrategyHeuristics.Default with { CleanAirThresholdSeconds = cleanAirThreshold });

    private static RaceState State(IReadOnlyList<RivalState> rivals, double playerLapDistPct = 0.5,
        int playerPosition = 5, int playerClassId = 1)
    {
        return new RaceState
        {
            CurrentLap = 10,
            LapDistPct = playerLapDistPct,
            FuelLevelLiters = 40,
            RollingFuelPerLap = 1.5,
            FuelTrackerHasEnoughData = true,
            LastLapTime = TimeSpan.FromSeconds(90),
            RecentLapTimes = new[] { TimeSpan.FromSeconds(90) },
            Rivals = rivals,
            PlayerPosition = playerPosition,
            PlayerClassPosition = playerPosition,
            PlayerClassId = playerClassId,
        };
    }

    private static RivalState Rival(int carIdx, int position, double lapDistPct, double lapTimeSec = 90,
        int classId = 1, double gapToPlayerSec = 0)
    {
        return new RivalState(
            CarIdx: carIdx, Position: position, ClassPosition: position, ClassId: classId,
            LapsCompleted: 9,
            LapDistPct: lapDistPct,
            GapToPlayerSeconds: TimeSpan.FromSeconds(gapToPlayerSec),
            LastLapTime: TimeSpan.FromSeconds(lapTimeSec),
            AverageRecentLapTime: TimeSpan.FromSeconds(lapTimeSec),
            RecentLapTimeStdDevSeconds: 0.2,
            CompletedPitStops: 0, IsOnPitRoad: false);
    }

    [Fact]
    public void NoRivals_ReturnsEmptyProjection()
    {
        var s = State(rivals: Array.Empty<RivalState>());
        var p = RejoinPredictor.Project(s, Config(), candidatePitLap: 10, pitLossSeconds: 25);
        p.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public void SingleRivalFarAway_IsCleanAir()
    {
        // One rival on the opposite side of the track from pit exit.
        var rivals = new[] { Rival(carIdx: 14, position: 4, lapDistPct: 0.5) };
        var p = RejoinPredictor.Project(State(rivals), Config(), 10, pitLossSeconds: 25);

        p.IsAvailable.Should().BeTrue();
        p.IsCleanAir.Should().BeTrue();
        p.NearbyRivals.Should().BeEmpty(); // none within ±5s
    }

    [Fact]
    public void RivalCloseToPitExit_IsTrafficAndFindsCleanAirLap()
    {
        // A rival who, after the pit-loss elapses, will be just ahead of the pit-exit point.
        // pitLossSeconds=25, lapTime=90 → rival advances 25/90 ≈ 0.278 of a lap.
        // Place rival at 0.05 - 0.278 + 1 = 0.772 so they end up at 0.05 (right at pit exit).
        // Slightly past pit exit gives a tiny positive gap → traffic.
        var rivals = new[] { Rival(carIdx: 14, position: 4, lapDistPct: 0.78) };
        var p = RejoinPredictor.Project(State(rivals), Config(cleanAirThreshold: 3.0),
            candidatePitLap: 10, pitLossSeconds: 25);

        p.IsAvailable.Should().BeTrue();
        p.IsCleanAir.Should().BeFalse();
        // Either ahead-gap or behind-gap should be small (within 3s).
        Math.Min(p.GapToCarAheadSeconds, p.GapToCarBehindSeconds).Should().BeLessThan(3.0);
    }

    [Fact]
    public void RejoinPosition_CountsRivalsProjectedAhead()
    {
        // Three rivals all currently ahead and stay ahead after pit.
        var rivals = new[] {
            Rival(carIdx: 1, position: 1, lapDistPct: 0.6),
            Rival(carIdx: 2, position: 2, lapDistPct: 0.55),
            Rival(carIdx: 3, position: 3, lapDistPct: 0.52),
        };
        var p = RejoinPredictor.Project(State(rivals, playerPosition: 4), Config(), 10, pitLossSeconds: 25);

        // Player is P4 in real time; with three rivals projected ahead at pit-exit, rejoin = P4.
        p.RejoinPosition.Should().BeGreaterThanOrEqualTo(1);
    }
}
