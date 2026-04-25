using PitStrategy.Core.Inputs;
using PitStrategy.Core.Pit;

namespace PitStrategy.Core.Tests.Pit;

public class PitLossModelTests
{
    private static readonly PitLossConfig Spa = new(
        PitLaneLengthMeters: 410,
        PitLaneSpeedLimitKph: 60,
        FuelFlowLitersPerSecond: 2.5,
        TireChangeServiceSeconds: 14.0,
        PitEntryDeltaSeconds: 2.0,
        PitExitDeltaSeconds: 2.5);

    [Fact]
    public void Travel_ComputedFromLengthAndSpeedLimit()
    {
        // 410m at 60 km/h = 410 / (60 * 1000/3600) = 410 / 16.6667 ≈ 24.6 s
        var loss = PitLossModel.Compute(Spa, PitServiceRequest.None);
        loss.PitLaneTravelSeconds.Should().BeApproximately(24.6, 0.1);
        loss.FuelServiceSeconds.Should().Be(0);
        loss.TireServiceSeconds.Should().Be(0);
        loss.EntryExitDeltaSeconds.Should().Be(4.5);
        loss.TotalSeconds.Should().BeApproximately(29.1, 0.1);
    }

    [Fact]
    public void RefuelOnly_AddsFuelTime()
    {
        // 30 L at 2.5 L/s = 12 s of fuel service.
        var req = new PitServiceRequest(RefuelArmed: true, FuelToAddLiters: 30, TireChangeArmed: false);
        var loss = PitLossModel.Compute(Spa, req);
        loss.FuelServiceSeconds.Should().BeApproximately(12.0, 1e-3);
        loss.TireServiceSeconds.Should().Be(0);
        loss.ServicesParallelSeconds.Should().BeApproximately(12.0, 1e-3);
    }

    [Fact]
    public void TiresOnly_AddsTireTime()
    {
        var req = new PitServiceRequest(RefuelArmed: false, FuelToAddLiters: 0, TireChangeArmed: true);
        var loss = PitLossModel.Compute(Spa, req);
        loss.FuelServiceSeconds.Should().Be(0);
        loss.TireServiceSeconds.Should().Be(14);
        loss.ServicesParallelSeconds.Should().Be(14);
    }

    [Fact]
    public void RefuelAndTires_TakeTheLongerOfTheTwo()
    {
        // 5 L of fuel = 2 s; tires = 14 s. Tire service dominates.
        var req = new PitServiceRequest(RefuelArmed: true, FuelToAddLiters: 5, TireChangeArmed: true);
        var loss = PitLossModel.Compute(Spa, req);
        loss.ServicesParallelSeconds.Should().Be(14);
        // 60 L of fuel = 24 s; tires = 14 s. Fuel dominates.
        var req2 = new PitServiceRequest(RefuelArmed: true, FuelToAddLiters: 60, TireChangeArmed: true);
        var loss2 = PitLossModel.Compute(Spa, req2);
        loss2.ServicesParallelSeconds.Should().BeApproximately(24.0, 1e-3);
    }

    [Fact]
    public void Repair_IsAddedSequentially()
    {
        var req = new PitServiceRequest(RefuelArmed: false, FuelToAddLiters: 0,
            TireChangeArmed: false, RepairArmed: true, RepairTimeSeconds: 30);
        var loss = PitLossModel.Compute(Spa, req);
        loss.TotalSeconds.Should().BeApproximately(24.6 + 0 + 4.5 + 30, 0.1);
    }

    [Fact]
    public void GenericFallback_ProducesReasonableValues()
    {
        var loss = PitLossModel.Compute(PitLossConfig.GenericFallback,
            new PitServiceRequest(true, 30, true));
        loss.TotalSeconds.Should().BeGreaterThan(15).And.BeLessThan(40);
    }
}
