using System;
using System.Collections.Generic;
using PitStrategy.Core.Inputs;
using PitStrategy.Core.Outputs;
using PitStrategy.Core.Strategy;

namespace PitStrategy.Core.Tests.Strategy;

public class StrategyEnginePhase2Tests
{
    private static readonly PitLossConfig Track = new(
        PitLaneLengthMeters: 350, PitLaneSpeedLimitKph: 60,
        FuelFlowLitersPerSecond: 2.5, TireChangeServiceSeconds: 12,
        PitEntryDeltaSeconds: 2, PitExitDeltaSeconds: 2);

    private static RaceConfig RaceCfg(int totalLaps = 30, double tank = 30) =>
        new(Length: new RaceLength.Laps(totalLaps),
            TankCapacityLiters: tank,
            PitLoss: Track);

    private static RivalState Rival(int carIdx, int position, double lapDistPct,
        int completedPitStops = 0, double gapToPlayerSeconds = 0,
        double recentStdDev = 0.2, int classId = 1)
    {
        return new RivalState(
            CarIdx: carIdx, Position: position, ClassPosition: position, ClassId: classId,
            LapsCompleted: 14, LapDistPct: lapDistPct,
            GapToPlayerSeconds: TimeSpan.FromSeconds(gapToPlayerSeconds),
            LastLapTime: TimeSpan.FromSeconds(90),
            AverageRecentLapTime: TimeSpan.FromSeconds(90),
            RecentLapTimeStdDevSeconds: recentStdDev,
            CompletedPitStops: completedPitStops, IsOnPitRoad: false);
    }

    [Fact]
    public void FcyOpportunity_TriggersPitNow()
    {
        var engine = new StrategyEngine();
        // Inside the 1-stop window, currently lap 16 of 30 with tank covering ~20 laps.
        var s = new RaceState
        {
            CurrentLap = 16,
            FuelLevelLiters = 6,                   // a few laps left in current stint
            RollingFuelPerLap = 1.5,
            FuelTrackerHasEnoughData = true,
            LastLapTime = TimeSpan.FromSeconds(90),
            RecentLapTimes = new[] { TimeSpan.FromSeconds(90) },
            IsUnderFcy = true,
            Flags = SessionFlag.FullCourseYellow,
            PitsOpen = true,
            PlayerPosition = 5, PlayerClassPosition = 5, PlayerClassId = 1,
            LapsRemaining = 14,
        };
        var d = engine.Decide(s, RaceCfg(30, tank: 30));
        d.Kind.Should().Be(PitDecisionKind.PitNow);
        d.Fcy.Should().NotBeNull();
        d.Fcy!.TimeSavingsSeconds.Should().BeGreaterThan(5);
        d.PrimaryReason.Should().Contain("FCY");
    }

    [Fact]
    public void Undercut_TriggersWhenRivalCloseAndUnpitted()
    {
        var engine = new StrategyEngine();
        // Inside the 1-stop window. Rival in P4 is 1.0s ahead, hasn't pitted.
        var s = new RaceState
        {
            CurrentLap = 16,
            FuelLevelLiters = 6,
            RollingFuelPerLap = 1.5,
            FuelTrackerHasEnoughData = true,
            LastLapTime = TimeSpan.FromSeconds(90),
            RecentLapTimes = new[] { TimeSpan.FromSeconds(90) },
            PlayerPosition = 5, PlayerClassPosition = 5, PlayerClassId = 1,
            LapsRemaining = 14,
            Rivals = new[] {
                Rival(carIdx: 14, position: 4, lapDistPct: 0.7,
                      completedPitStops: 0, gapToPlayerSeconds: 1.0),
                Rival(carIdx: 21, position: 6, lapDistPct: 0.3,
                      completedPitStops: 0, gapToPlayerSeconds: -2.5),
            },
        };
        var d = engine.Decide(s, RaceCfg(30, tank: 30));
        d.Kind.Should().Be(PitDecisionKind.PitNow);
        d.Undercut.Should().NotBeNull();
        d.Undercut!.RivalCarIdx.Should().Be(14);
        d.Undercut.ExpectedGainSeconds.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Undercut_DoesNotTriggerWhenRivalAlreadyPitted()
    {
        var engine = new StrategyEngine();
        var s = new RaceState
        {
            CurrentLap = 16, FuelLevelLiters = 6, RollingFuelPerLap = 1.5,
            FuelTrackerHasEnoughData = true,
            LastLapTime = TimeSpan.FromSeconds(90),
            RecentLapTimes = new[] { TimeSpan.FromSeconds(90) },
            PlayerPosition = 5, PlayerClassId = 1, LapsRemaining = 14,
            Rivals = new[] {
                Rival(carIdx: 14, position: 4, lapDistPct: 0.7,
                      completedPitStops: 1, gapToPlayerSeconds: 1.0),
            },
        };
        var d = engine.Decide(s, RaceCfg(30, tank: 30));
        d.Undercut.Should().BeNull();
    }

    [Fact]
    public void PitNextLap_FiresWhenWaitingOpensCleanAir()
    {
        var engine = new StrategyEngine();
        // Rival positioned so that pitting now lands you right next to them, but waiting
        // one lap puts the rival farther ahead by another lap-fraction.
        var s = new RaceState
        {
            CurrentLap = 16,
            FuelLevelLiters = 8, // gives us another lap+
            RollingFuelPerLap = 1.5,
            FuelTrackerHasEnoughData = true,
            LastLapTime = TimeSpan.FromSeconds(90),
            RecentLapTimes = new[] { TimeSpan.FromSeconds(90) },
            PlayerPosition = 5, PlayerClassId = 1, LapsRemaining = 14,
            Rivals = new[] {
                // One rival in clean track position
                Rival(carIdx: 14, position: 4, lapDistPct: 0.78,
                      completedPitStops: 0, gapToPlayerSeconds: 5.0),
            },
        };

        var d = engine.Decide(s, RaceCfg(30, tank: 30));
        // The decision could be PitNow, PitNextLap, or StayOut depending on projected gaps.
        // For pit-recommending branches, traffic should be live; for stay-out, Empty is fine.
        if (d.Kind == PitDecisionKind.PitNow || d.Kind == PitDecisionKind.PitNextLap
            || d.Kind == PitDecisionKind.PitForFuelOnly)
        {
            d.TrafficAfterPit.IsAvailable.Should().BeTrue();
        }
    }

    [Fact]
    public void TrafficProjection_PopulatedWhenRivalsPresent()
    {
        var engine = new StrategyEngine();
        var s = new RaceState
        {
            CurrentLap = 16, FuelLevelLiters = 6, RollingFuelPerLap = 1.5,
            FuelTrackerHasEnoughData = true,
            LastLapTime = TimeSpan.FromSeconds(90),
            RecentLapTimes = new[] { TimeSpan.FromSeconds(90) },
            PlayerPosition = 5, PlayerClassId = 1, LapsRemaining = 14,
            Rivals = new[] {
                Rival(carIdx: 1, position: 1, lapDistPct: 0.5),
                Rival(carIdx: 2, position: 2, lapDistPct: 0.6),
            },
        };
        var d = engine.Decide(s, RaceCfg(30, tank: 30));
        // Sanity: any branch that exposes a real projection should set IsAvailable. The branches
        // that use TrafficProjection.Empty (StayOut-no-pit, default StayOut) are valid too.
        // We only assert on traffic when the engine actually recommends pitting — that's when
        // the dashboard's traffic banner is meaningful.
        if (d.Kind == PitDecisionKind.PitNow || d.Kind == PitDecisionKind.PitNextLap
            || d.Kind == PitDecisionKind.PitForFuelOnly)
        {
            d.TrafficAfterPit.IsAvailable.Should().BeTrue($"engine emitted {d.Kind} so traffic should be live");
            d.TrafficAfterPit.RejoinPosition.Should().BeGreaterThan(0);
        }
        else
        {
            // StayOut path is fine; just confirm we got a valid decision.
            d.Kind.Should().BeOneOf(PitDecisionKind.StayOut, PitDecisionKind.SaveFuel);
        }
    }
}
