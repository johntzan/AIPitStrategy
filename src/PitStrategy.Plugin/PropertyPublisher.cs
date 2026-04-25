using System;
using GameReaderCommon;
using PitStrategy.Core.Outputs;
using SimHub.Plugins;

namespace PitStrategy.Plugin
{
    /// <summary>
    /// Owns the <c>pluginManager.AddProperty</c> + <c>SetPropertyValue</c> wiring. Properties
    /// are namespaced under <c>PitStrategy.&lt;Group&gt;.&lt;Field&gt;</c> so dashboards can
    /// bind via NCalc expressions like <c>[PitStrategy.Decision.Kind]</c>.
    /// </summary>
    public sealed class PropertyPublisher
    {
        private readonly PluginManager _pm;
        private readonly object _owner;

        public PropertyPublisher(PluginManager pm, object owner)
        {
            _pm = pm;
            _owner = owner;
        }

        public void RegisterAll()
        {
            // Headline decision
            _pm.AddProperty("PitStrategy.Decision.Kind", _owner.GetType(), typeof(string));
            _pm.AddProperty("PitStrategy.Decision.Confidence", _owner.GetType(), typeof(double));
            _pm.AddProperty("PitStrategy.Decision.PrimaryReason", _owner.GetType(), typeof(string));
            _pm.AddProperty("PitStrategy.Decision.RecommendedPitLap", _owner.GetType(), typeof(int));

            // Pit-loss decomposition
            _pm.AddProperty("PitStrategy.PitLoss.Total", _owner.GetType(), typeof(double));
            _pm.AddProperty("PitStrategy.PitLoss.PitLaneTravel", _owner.GetType(), typeof(double));
            _pm.AddProperty("PitStrategy.PitLoss.FuelService", _owner.GetType(), typeof(double));
            _pm.AddProperty("PitStrategy.PitLoss.TireService", _owner.GetType(), typeof(double));
            _pm.AddProperty("PitStrategy.PitLoss.EntryExitDelta", _owner.GetType(), typeof(double));

            // Traffic projection
            _pm.AddProperty("PitStrategy.Traffic.IsCleanAir", _owner.GetType(), typeof(bool));
            _pm.AddProperty("PitStrategy.Traffic.RejoinPosition", _owner.GetType(), typeof(int));
            _pm.AddProperty("PitStrategy.Traffic.GapAhead", _owner.GetType(), typeof(double));
            _pm.AddProperty("PitStrategy.Traffic.GapBehind", _owner.GetType(), typeof(double));
            _pm.AddProperty("PitStrategy.Traffic.CleanAirLapIfWait", _owner.GetType(), typeof(int));

            // Fuel diagnostics
            _pm.AddProperty("PitStrategy.Fuel.Level", _owner.GetType(), typeof(double));
            _pm.AddProperty("PitStrategy.Fuel.PerLapAverage", _owner.GetType(), typeof(double));
            _pm.AddProperty("PitStrategy.Fuel.LapsToEmpty", _owner.GetType(), typeof(double));
            _pm.AddProperty("PitStrategy.Fuel.LapsToEmptyAtMargin", _owner.GetType(), typeof(double));
            _pm.AddProperty("PitStrategy.Fuel.MinFuelToAdd", _owner.GetType(), typeof(double));
            _pm.AddProperty("PitStrategy.Fuel.HasEnoughData", _owner.GetType(), typeof(bool));

            // Pit-window diagnostics
            _pm.AddProperty("PitStrategy.Pit.RecommendedLap", _owner.GetType(), typeof(int));
            _pm.AddProperty("PitStrategy.Pit.LapsUntilPit", _owner.GetType(), typeof(int));
            _pm.AddProperty("PitStrategy.Pit.StintsRemaining", _owner.GetType(), typeof(int));

            // Tactical alerts
            _pm.AddProperty("PitStrategy.Undercut.IsActive", _owner.GetType(), typeof(bool));
            _pm.AddProperty("PitStrategy.Undercut.RivalCarIdx", _owner.GetType(), typeof(int));
            _pm.AddProperty("PitStrategy.Undercut.ExpectedGain", _owner.GetType(), typeof(double));

            _pm.AddProperty("PitStrategy.Fcy.IsOpportunity", _owner.GetType(), typeof(bool));
            _pm.AddProperty("PitStrategy.Fcy.TimeSavings", _owner.GetType(), typeof(double));
            _pm.AddProperty("PitStrategy.Fcy.Active", _owner.GetType(), typeof(bool));

            // Sim diagnostics (Phase 3 — placeholder values for now)
            _pm.AddProperty("PitStrategy.Sim.LastRunUtc", _owner.GetType(), typeof(string));
            _pm.AddProperty("PitStrategy.Sim.IsRunning", _owner.GetType(), typeof(bool));
            _pm.AddProperty("PitStrategy.Sim.WinProbability", _owner.GetType(), typeof(double));
        }

        public void PublishFastFrame(double fuelLevel, double fuelPerLap, bool hasEnoughData)
        {
            _pm.SetPropertyValue("PitStrategy.Fuel.Level", _owner.GetType(), fuelLevel);
            _pm.SetPropertyValue("PitStrategy.Fuel.PerLapAverage", _owner.GetType(), fuelPerLap);
            _pm.SetPropertyValue("PitStrategy.Fuel.HasEnoughData", _owner.GetType(), hasEnoughData);
        }

        public void Publish(PitDecision d, int currentLap)
        {
            _pm.SetPropertyValue("PitStrategy.Decision.Kind", _owner.GetType(), d.Kind.ToString());
            _pm.SetPropertyValue("PitStrategy.Decision.Confidence", _owner.GetType(), d.Confidence);
            _pm.SetPropertyValue("PitStrategy.Decision.PrimaryReason", _owner.GetType(), d.PrimaryReason ?? string.Empty);
            _pm.SetPropertyValue("PitStrategy.Decision.RecommendedPitLap", _owner.GetType(), d.RecommendedPitLap ?? 0);

            _pm.SetPropertyValue("PitStrategy.PitLoss.Total", _owner.GetType(), d.LossBreakdown.TotalSeconds);
            _pm.SetPropertyValue("PitStrategy.PitLoss.PitLaneTravel", _owner.GetType(), d.LossBreakdown.PitLaneTravelSeconds);
            _pm.SetPropertyValue("PitStrategy.PitLoss.FuelService", _owner.GetType(), d.LossBreakdown.FuelServiceSeconds);
            _pm.SetPropertyValue("PitStrategy.PitLoss.TireService", _owner.GetType(), d.LossBreakdown.TireServiceSeconds);
            _pm.SetPropertyValue("PitStrategy.PitLoss.EntryExitDelta", _owner.GetType(), d.LossBreakdown.EntryExitDeltaSeconds);

            var t = d.TrafficAfterPit;
            _pm.SetPropertyValue("PitStrategy.Traffic.IsCleanAir", _owner.GetType(), t.IsCleanAir);
            _pm.SetPropertyValue("PitStrategy.Traffic.RejoinPosition", _owner.GetType(), t.RejoinPosition);
            _pm.SetPropertyValue("PitStrategy.Traffic.GapAhead", _owner.GetType(),
                double.IsInfinity(t.GapToCarAheadSeconds) ? 0 : t.GapToCarAheadSeconds);
            _pm.SetPropertyValue("PitStrategy.Traffic.GapBehind", _owner.GetType(),
                double.IsInfinity(t.GapToCarBehindSeconds) ? 0 : t.GapToCarBehindSeconds);
            _pm.SetPropertyValue("PitStrategy.Traffic.CleanAirLapIfWait", _owner.GetType(),
                t.CleanAirLapIfWait ?? 0);

            _pm.SetPropertyValue("PitStrategy.Fuel.LapsToEmpty", _owner.GetType(),
                double.IsNaN(d.LapsToEmpty) ? 0 : d.LapsToEmpty);
            _pm.SetPropertyValue("PitStrategy.Fuel.LapsToEmptyAtMargin", _owner.GetType(),
                double.IsNaN(d.LapsToEmptyAtSafetyMargin) ? 0 : d.LapsToEmptyAtSafetyMargin);
            _pm.SetPropertyValue("PitStrategy.Fuel.MinFuelToAdd", _owner.GetType(), d.MinFuelToAddLiters);

            _pm.SetPropertyValue("PitStrategy.Pit.RecommendedLap", _owner.GetType(), d.RecommendedPitLap ?? 0);
            _pm.SetPropertyValue("PitStrategy.Pit.LapsUntilPit", _owner.GetType(),
                d.RecommendedPitLap.HasValue ? Math.Max(0, d.RecommendedPitLap.Value - currentLap) : 0);
            _pm.SetPropertyValue("PitStrategy.Pit.StintsRemaining", _owner.GetType(), d.StintsRemaining);

            _pm.SetPropertyValue("PitStrategy.Undercut.IsActive", _owner.GetType(), d.Undercut != null);
            _pm.SetPropertyValue("PitStrategy.Undercut.RivalCarIdx", _owner.GetType(), d.Undercut?.RivalCarIdx ?? -1);
            _pm.SetPropertyValue("PitStrategy.Undercut.ExpectedGain", _owner.GetType(), d.Undercut?.ExpectedGainSeconds ?? 0);

            _pm.SetPropertyValue("PitStrategy.Fcy.IsOpportunity", _owner.GetType(), d.Fcy?.ShouldPitNow ?? false);
            _pm.SetPropertyValue("PitStrategy.Fcy.TimeSavings", _owner.GetType(), d.Fcy?.TimeSavingsSeconds ?? 0);
            // Active flag is updated by the fast publisher (bound to the SessionFlag bit) when raw data is available.
        }
    }
}
