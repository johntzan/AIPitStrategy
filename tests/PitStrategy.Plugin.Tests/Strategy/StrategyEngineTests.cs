using System;
using System.Collections.Generic;
using PitStrategy.Core.Inputs;
using PitStrategy.Core.Outputs;
using PitStrategy.Core.Strategy;

namespace PitStrategy.Plugin.Tests.Strategy;

public class StrategyEngineTests
{
    private static readonly PitLossConfig Track = new(
        PitLaneLengthMeters: 350, PitLaneSpeedLimitKph: 60,
        FuelFlowLitersPerSecond: 2.5, TireChangeServiceSeconds: 12,
        PitEntryDeltaSeconds: 2, PitExitDeltaSeconds: 2);

    private static RaceConfig SprintConfig(int totalLaps, double tank = 80) =>
        new(Length: new RaceLength.Laps(totalLaps),
            TankCapacityLiters: tank,
            PitLoss: Track);

    private static RaceState State(
        int currentLap, double fuel, double fuelPerLap,
        IReadOnlyList<RivalState>? rivals = null,
        bool isUnderFcy = false,
        bool isOnPitRoad = false,
        int? lapsRemaining = null)
    {
        return new RaceState
        {
            CurrentLap = currentLap,
            FuelLevelLiters = fuel,
            RollingFuelPerLap = fuelPerLap,
            FuelTrackerHasEnoughData = fuelPerLap > 0,
            LastLapTime = TimeSpan.FromSeconds(90),
            BestLapTime = TimeSpan.FromSeconds(89),
            RecentLapTimes = new[] { TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(90.5) },
            Rivals = rivals ?? Array.Empty<RivalState>(),
            IsUnderFcy = isUnderFcy,
            Flags = isUnderFcy ? SessionFlag.FullCourseYellow : SessionFlag.Green,
            IsOnPitRoad = isOnPitRoad,
            LapsRemaining = lapsRemaining,
            PitsOpen = true,
        };
    }

    [Fact]
    public void Unknown_WhenInsufficientData()
    {
        var engine = new StrategyEngine();
        var s = State(currentLap: 1, fuel: 80, fuelPerLap: 0); // no green-flag fuel sample yet
        var d = engine.Decide(s, SprintConfig(30));
        d.Kind.Should().Be(PitDecisionKind.Unknown);
        d.PrimaryReason.Should().Contain("data");
    }

    [Fact]
    public void StayOut_WhenFuelCoversTheRace()
    {
        var engine = new StrategyEngine();
        // 30-lap race, lap 5, 60L fuel, 1.5L/lap → 40 laps to empty, 25 laps to go. No pit needed.
        var s = State(currentLap: 5, fuel: 60, fuelPerLap: 1.5, lapsRemaining: 25);
        var d = engine.Decide(s, SprintConfig(30, tank: 60));
        d.Kind.Should().Be(PitDecisionKind.StayOut);
        d.PrimaryReason.Should().Contain("no pit");
    }

    [Fact]
    public void PitForFuelOnly_WhenFuelImminentlyOut()
    {
        var engine = new StrategyEngine();
        // 30-lap race, lap 25, 1L fuel, 2L/lap → only 0.5 laps left. Forced.
        var s = State(currentLap: 25, fuel: 1, fuelPerLap: 2, lapsRemaining: 5);
        var d = engine.Decide(s, SprintConfig(30, tank: 60));
        d.Kind.Should().Be(PitDecisionKind.PitForFuelOnly);
        d.Confidence.Should().BeApproximately(1.0, 1e-6);
        d.RecommendedPitLap.Should().Be(25);
    }

    [Fact]
    public void StayOut_BeforePitWindowOpens()
    {
        var engine = new StrategyEngine();
        // 50-lap race, 30L tank (~20 laps coverage), 1.5L/lap. Lap 1 of 50.
        // Window opens around lap 30+, well after lap 1.
        var s = State(currentLap: 1, fuel: 30, fuelPerLap: 1.5, lapsRemaining: 49);
        var d = engine.Decide(s, SprintConfig(50, tank: 30));
        d.Kind.Should().Be(PitDecisionKind.StayOut);
        d.RecommendedPitLap.Should().BeGreaterThan(s.CurrentLap);
    }

    [Fact]
    public void PitNow_WhenInsideOptimalWindow()
    {
        var engine = new StrategyEngine();
        // 30-lap race, tank covers exactly 20 laps. By lap 18 we should be inside the window.
        var s = State(currentLap: 18, fuel: 3, fuelPerLap: 1.5, lapsRemaining: 12);
        var d = engine.Decide(s, SprintConfig(30, tank: 30));
        d.Kind.Should().BeOneOf(PitDecisionKind.PitNow, PitDecisionKind.PitForFuelOnly);
    }

    [Fact]
    public void PitLossBreakdown_IsAlwaysPopulated()
    {
        var engine = new StrategyEngine();
        var s = State(currentLap: 5, fuel: 30, fuelPerLap: 1.5, lapsRemaining: 25);
        var d = engine.Decide(s, SprintConfig(30, tank: 30));
        d.LossBreakdown.TotalSeconds.Should().BeGreaterThan(0);
        d.LossBreakdown.PitLaneTravelSeconds.Should().BeGreaterThan(0);
        d.LossBreakdown.EntryExitDeltaSeconds.Should().Be(4);
    }

    [Fact]
    public void RecommendedPitLap_FallsInsideLegalWindow()
    {
        var engine = new StrategyEngine();
        var s = State(currentLap: 1, fuel: 30, fuelPerLap: 1.5, lapsRemaining: 49);
        var config = SprintConfig(50, tank: 30);
        var d = engine.Decide(s, config);
        d.RecommendedPitLap.Should().NotBeNull();
        d.RecommendedPitLap!.Value.Should().BeGreaterThan(s.CurrentLap);
        d.RecommendedPitLap.Value.Should().BeLessThanOrEqualTo(50);
    }
}
