using System;
using PitStrategy.Core.Inputs;
using PitStrategy.Core.PitWindow;

namespace PitStrategy.Core.Tests.PitWindow;

public class PitWindowCalculatorTests
{
    private static readonly PitLossConfig Track = PitLossConfig.GenericFallback;

    private static RaceConfig SprintConfig(int totalLaps, double tank = 80) =>
        new(Length: new RaceLength.Laps(totalLaps),
            TankCapacityLiters: tank,
            PitLoss: Track,
            FuelSafetyMarginLaps: 0.5);

    private static RaceState State(int currentLap, double fuel, double rollingFuelPerLap,
        TimeSpan lastLap = default, int? lapsRemaining = null)
    {
        return new RaceState
        {
            CurrentLap = currentLap,
            FuelLevelLiters = fuel,
            RollingFuelPerLap = rollingFuelPerLap,
            FuelTrackerHasEnoughData = true,
            LastLapTime = lastLap == default ? TimeSpan.FromSeconds(90) : lastLap,
            LapsRemaining = lapsRemaining,
        };
    }

    [Fact]
    public void NoPitNeeded_WhenFuelCoversRace()
    {
        // 30-lap race, 60L tank, 1.5L/lap means 45L needed total. We have 50L on lap 5 (25 to go = 37.5L).
        var s = State(currentLap: 5, fuel: 50, rollingFuelPerLap: 1.5);
        var w = PitWindowCalculator.Compute(s, SprintConfig(30, tank: 60));
        w.RequiresPit.Should().BeFalse();
        w.MinStops.Should().Be(0);
        w.LapsToEmpty.Should().BeApproximately(50 / 1.5, 1e-6);
    }

    [Fact]
    public void SinglePitWindow_IsCentredAroundMidRace()
    {
        // 30-lap race, 30L tank (~20 laps coverage), 1.5L/lap → 1 stop needed.
        // Earliest: 30 - 19 + 0.5 ≈ lap 12. Latest: 1 + 19.5 ≈ lap 20.
        // Recommended ≈ lap 16.
        var s = State(currentLap: 1, fuel: 30, rollingFuelPerLap: 1.5);
        var w = PitWindowCalculator.Compute(s, SprintConfig(30, tank: 30));
        w.RequiresPit.Should().BeTrue();
        w.MinStops.Should().BeGreaterThanOrEqualTo(1);
        w.EarliestLap.Should().NotBeNull();
        w.LatestLap.Should().NotBeNull();
        w.LatestLap.Should().BeGreaterThanOrEqualTo(w.EarliestLap!.Value);
        w.RecommendedLap.Should().BeInRange(w.EarliestLap.Value, w.LatestLap!.Value);
    }

    [Fact]
    public void ForcedPitLap_FlagsWhenLatestLapIsInPast()
    {
        // 30-lap race, lap 25, only 1L of fuel @ 2L/lap → 0.5 laps to empty. Forced pit.
        var s = State(currentLap: 25, fuel: 1, rollingFuelPerLap: 2);
        var w = PitWindowCalculator.Compute(s, SprintConfig(30));
        w.RequiresPit.Should().BeTrue();
        w.IsForcedPitLap.Should().BeTrue();
    }

    [Fact]
    public void Unknown_WhenInsufficientData()
    {
        var s = new RaceState
        {
            CurrentLap = 1,
            FuelLevelLiters = 80,
            RollingFuelPerLap = 0,
            FuelTrackerHasEnoughData = false,
        };
        var w = PitWindowCalculator.Compute(s, SprintConfig(30));
        w.HasEnoughData.Should().BeFalse();
    }

    [Fact]
    public void TimedRace_DerivesLapsRemainingFromTime()
    {
        // 1-hour timed race, lap 5, used 7.5 minutes, 90s laps → 35 laps remaining.
        var config = new RaceConfig(
            Length: new RaceLength.Timed(TimeSpan.FromHours(1)),
            TankCapacityLiters: 60,
            PitLoss: Track);
        var s = new RaceState
        {
            CurrentLap = 5,
            FuelLevelLiters = 30,
            RollingFuelPerLap = 1.5,
            FuelTrackerHasEnoughData = true,
            SessionTimeElapsed = TimeSpan.FromMinutes(7.5),
            SessionTimeRemaining = TimeSpan.FromMinutes(52.5),
            LastLapTime = TimeSpan.FromSeconds(90),
            RecentLapTimes = new[] { TimeSpan.FromSeconds(90) },
        };
        var w = PitWindowCalculator.Compute(s, config);
        // 52.5 minutes / 90s = 35 laps. Total estimated laps remaining ~35.
        w.LapsRemainingEstimated.Should().BeApproximately(35.0, 1.0);
    }
}
