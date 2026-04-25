using System;

namespace PitStrategy.Plugin.Settings
{
    /// <summary>
    /// User-tunable settings persisted by SimHub. Plain POCO; SimHub serializes via
    /// JSON.NET when <see cref="PitStrategyPlugin.End"/> calls SaveCommonSettings.
    /// </summary>
    public sealed class PluginSettings
    {
        // ── Vehicle defaults (overrides for when telemetry is missing or wrong) ───────
        public bool OverrideTankCapacity { get; set; } = false;
        public double TankCapacityLitersOverride { get; set; } = 60.0;

        public bool OverrideFuelPerLap { get; set; } = false;
        public double FuelPerLapOverride { get; set; } = 2.5;

        // ── Strategy ──────────────────────────────────────────────────────────────────
        public double FuelSafetyMarginLaps { get; set; } = 0.5;
        public int FuelRollingWindowLaps { get; set; } = 5;
        public double FuelOutlierZScore { get; set; } = 2.5;
        public double UndercutThresholdSeconds { get; set; } = 1.5;
        public double CleanAirThresholdSeconds { get; set; } = 3.0;
        public int CleanAirLookaheadLaps { get; set; } = 5;
        public double FcyPitTimeSavingsFraction { get; set; } = 0.55;

        // ── Pit-loss config (per-track overrides) ─────────────────────────────────────
        public bool OverridePitLaneLength { get; set; } = false;
        public double PitLaneLengthMetersOverride { get; set; } = 350;
        public double FuelFlowLitersPerSecond { get; set; } = 2.5;
        public double TireChangeServiceSeconds { get; set; } = 14.0;
        public double PitEntryDeltaSeconds { get; set; } = 2.0;
        public double PitExitDeltaSeconds { get; set; } = 2.0;

        // ── Monte Carlo (Phase 3) ─────────────────────────────────────────────────────
        public int MonteCarloTrials { get; set; } = 2000;
        public bool MonteCarloParallel { get; set; } = true;
        public int MonteCarloSeed { get; set; } = 0;

        // ── Diagnostics ───────────────────────────────────────────────────────────────
        public bool EnableFrameDump { get; set; } = false;
        public string LastFrameDumpPath { get; set; } = string.Empty;
        public int LogLevel { get; set; } = 1;            // 0 silent, 1 info, 2 debug, 3 trace

        // ── Engine cadence ────────────────────────────────────────────────────────────
        /// <summary>How often to invoke <c>StrategyEngine.Decide</c>. Default 1 Hz.</summary>
        public TimeSpan EngineThrottle { get; set; } = TimeSpan.FromSeconds(1);
    }
}
