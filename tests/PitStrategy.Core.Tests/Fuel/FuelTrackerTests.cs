using System;
using PitStrategy.Core.Fuel;

namespace PitStrategy.Core.Tests.Fuel;

public class FuelTrackerTests
{
    [Fact]
    public void NewTracker_HasNoData()
    {
        var t = new FuelTracker();
        t.HasEnoughDataForPrediction.Should().BeFalse();
        double.IsNaN(t.RollingAverageFuelPerLap).Should().BeTrue();
        t.SamplesInWindow.Should().Be(0);
    }

    [Fact]
    public void OnLapCompleted_GreenLap_AddsSampleAndComputesAverage()
    {
        var t = new FuelTracker(windowLaps: 5);
        t.OnLapCompleted(lap: 1, fuelAtLapStart: 100.0, fuelAtLapEnd: 97.5,
            lapTime: TimeSpan.FromSeconds(90), wasUnderYellow: false);

        t.SamplesInWindow.Should().Be(1);
        t.RollingAverageFuelPerLap.Should().BeApproximately(2.5, 1e-6);
        t.LastLapFuelUsed.Should().BeApproximately(2.5, 1e-6);
        t.HasEnoughDataForPrediction.Should().BeTrue();
    }

    [Fact]
    public void OnLapCompleted_YellowLap_DoesNotAffectAverage()
    {
        var t = new FuelTracker();
        t.OnLapCompleted(1, 100, 97.5, TimeSpan.FromSeconds(90), false);
        t.OnLapCompleted(2, 97.5, 96.0, TimeSpan.FromSeconds(180), wasUnderYellow: true); // pace car

        t.SamplesInWindow.Should().Be(1);
        t.RollingAverageFuelPerLap.Should().BeApproximately(2.5, 1e-6);
    }

    [Fact]
    public void OnLapCompleted_PitStopLap_DoesNotAffectAverage()
    {
        var t = new FuelTracker();
        t.OnLapCompleted(1, 100, 97.5, TimeSpan.FromSeconds(90), false);
        // Pit stop: fuel went up.
        t.OnLapCompleted(2, 97.5, 100.0, TimeSpan.FromSeconds(120), false);

        t.SamplesInWindow.Should().Be(1);
        t.LastLapFuelUsed.Should().Be(0); // pit lap reports zero used
    }

    [Fact]
    public void RollingWindow_OnlyKeepsLastN()
    {
        var t = new FuelTracker(windowLaps: 3);
        for (int i = 0; i < 5; i++)
        {
            t.OnLapCompleted(i + 1, fuelAtLapStart: 100 - (i * 2.5),
                fuelAtLapEnd: 100 - ((i + 1) * 2.5),
                lapTime: TimeSpan.FromSeconds(90), wasUnderYellow: false);
        }
        t.SamplesInWindow.Should().Be(3);
    }

    [Fact]
    public void OutlierRejection_DropsExtremeValues()
    {
        var t = new FuelTracker(windowLaps: 10, outlierZScore: 1.5);
        // 5 laps with realistic small variance around 2.5L. Without variance the
        // sample-stddev is zero and Z-score is undefined, so outlier detection is
        // disabled (correct behavior).
        double[] burns = { 2.45, 2.55, 2.48, 2.52, 2.50 };
        double fuel = 100;
        for (int i = 0; i < burns.Length; i++)
        {
            double next = fuel - burns[i];
            t.OnLapCompleted(i + 1, fuel, next, TimeSpan.FromSeconds(90), false);
            fuel = next;
        }

        double avgBefore = t.RollingAverageFuelPerLap;

        // Now an outlier — 8L on one lap (fuel pump glitch / data spike).
        t.OnLapCompleted(burns.Length + 1, fuel, fuel - 8, TimeSpan.FromSeconds(90), false);

        t.OutliersRejected.Should().Be(1);
        t.RollingAverageFuelPerLap.Should().BeApproximately(avgBefore, 1e-6);
    }

    [Fact]
    public void Reset_ClearsAllState()
    {
        var t = new FuelTracker();
        t.OnLapCompleted(1, 100, 97.5, TimeSpan.FromSeconds(90), false);
        t.Reset();
        t.SamplesInWindow.Should().Be(0);
        double.IsNaN(t.RollingAverageFuelPerLap).Should().BeTrue();
        t.HasEnoughDataForPrediction.Should().BeFalse();
    }

    [Fact]
    public void LapsToEmpty_UsesRollingAverage()
    {
        var t = new FuelTracker();
        t.OnLapCompleted(1, 100, 98, TimeSpan.FromSeconds(90), false); // 2.0 L/lap
        t.LapsToEmpty(currentFuelLiters: 50).Should().BeApproximately(25.0, 1e-6);
    }

    [Fact]
    public void OnTick_DetectsLapBoundaryAutomatically()
    {
        var t = new FuelTracker();
        // Tick during lap 1
        t.OnTick(currentLap: 1, currentFuelLiters: 100, isOnPitRoad: false,
            isUnderYellow: false, lastLapTime: TimeSpan.Zero);
        // Tick rolls into lap 2 — fuel went down 2.5L over the lap.
        t.OnTick(currentLap: 2, currentFuelLiters: 97.5, isOnPitRoad: false,
            isUnderYellow: false, lastLapTime: TimeSpan.FromSeconds(90));

        t.SamplesInWindow.Should().Be(1);
        t.RollingAverageFuelPerLap.Should().BeApproximately(2.5, 1e-6);
    }

    [Fact]
    public void OnTick_OnPitRoad_SuppressesSampling()
    {
        var t = new FuelTracker();
        t.OnTick(1, 50, false, false, TimeSpan.Zero);
        // Player enters pits, fuel jumps to 100.
        t.OnTick(1, 100, isOnPitRoad: true, false, TimeSpan.Zero);
        t.OnTick(2, 97.5, false, false, TimeSpan.FromSeconds(90));

        // The pit-out lap shouldn't be sampled (boundary state was reset).
        t.SamplesInWindow.Should().Be(0);
    }

    [Fact]
    public void WeightedAverage_FavorsRecentSamples()
    {
        var t = new FuelTracker(windowLaps: 5);
        // Three laps at 3.0, two laps at 2.0
        t.OnLapCompleted(1, 100, 97, TimeSpan.FromSeconds(90), false);
        t.OnLapCompleted(2, 97, 94, TimeSpan.FromSeconds(90), false);
        t.OnLapCompleted(3, 94, 91, TimeSpan.FromSeconds(90), false);
        t.OnLapCompleted(4, 91, 89, TimeSpan.FromSeconds(90), false);
        t.OnLapCompleted(5, 89, 87, TimeSpan.FromSeconds(90), false);

        // simple average: (3+3+3+2+2)/5 = 2.6
        t.RollingAverageFuelPerLap.Should().BeApproximately(2.6, 1e-6);
        // weighted with linear weights 1..5: (3*1 + 3*2 + 3*3 + 2*4 + 2*5) / 15 = (3+6+9+8+10)/15 = 36/15 = 2.4
        t.WeightedAverageFuelPerLap.Should().BeApproximately(2.4, 1e-6);
    }
}
