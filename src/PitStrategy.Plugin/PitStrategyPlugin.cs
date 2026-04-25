using System;
using System.IO;
using System.Windows.Controls;
using System.Windows.Media;
using GameReaderCommon;
using Newtonsoft.Json;
using PitStrategy.Core.Fuel;
using PitStrategy.Core.Inputs;
using PitStrategy.Core.Outputs;
using PitStrategy.Core.Strategy;
using PitStrategy.Plugin.Settings;
using SimHub.Plugins;

namespace PitStrategy.Plugin
{
    [PluginDescription("AI-driven pit-strategy engine for iRacing. Recommends PIT NOW vs STAY OUT with confidence and reason.")]
    [PluginAuthor("PitStrategy contributors")]
    [PluginName("Pit Strategy")]
    public sealed class PitStrategyPlugin : IPlugin, IDataPlugin, IWPFSettingsV2
    {
        private const string SettingsKey = "PitStrategy.Settings";

        private PluginSettings _settings = new();
        private FuelTracker _fuel = new();
        private StrategyEngine _engine = new();
        private IRacingMapper? _mapper;
        private PropertyPublisher? _publisher;
        private DateTime _lastEngineRunUtc = DateTime.MinValue;
        private PitDecision? _latest;
        private RaceConfig _raceConfig = DefaultRaceConfig();

        public PluginManager PluginManager { get; set; } = null!;

        // ── IWPFSettingsV2 ───────────────────────────────────────────────────────────────
        // Icon is optional; returning null keeps SimHub's default plugin icon.
        public ImageSource PictureIcon => null!;
        public string LeftMenuTitle => "Pit Strategy";

        // ── IPlugin ──────────────────────────────────────────────────────────────────────
        public void Init(PluginManager pluginManager)
        {
            PluginManager = pluginManager;
            _settings = this.ReadCommonSettings(SettingsKey, () => new PluginSettings());

            _fuel = new FuelTracker(_settings.FuelRollingWindowLaps, _settings.FuelOutlierZScore);
            _mapper = new IRacingMapper(_settings);
            _publisher = new PropertyPublisher(pluginManager, this);
            _publisher.RegisterAll();

            // Bindable actions: AddAction expects Action<PluginManager, string>.
            // Phase 3 will hook RunSimulation up to the Monte Carlo simulator.
            pluginManager.AddAction("PitStrategy.Action.RunSimulation", GetType(),
                new Action<PluginManager, string>((pm, name) => { /* no-op */ }));
            pluginManager.AddAction("PitStrategy.Action.ResetFuelTracker", GetType(),
                new Action<PluginManager, string>((pm, name) => _fuel.Reset()));
            pluginManager.AddAction("PitStrategy.Action.DumpFrame", GetType(),
                new Action<PluginManager, string>((pm, name) => DumpLatestFrame()));
        }

        public void End(PluginManager pluginManager)
        {
            this.SaveCommonSettings(SettingsKey, _settings);
        }

        public Control GetWPFSettingsControl(PluginManager pluginManager) =>
            new SettingsView(_settings);

        // ── IDataPlugin: 60 Hz tick ──────────────────────────────────────────────────────
        public void DataUpdate(PluginManager pluginManager, ref GameData data)
        {
            if (!data.GameRunning) return;
            // Restrict to iRacing — the plugin is iRacing-specific by design.
            if (!string.Equals(data.GameName, "IRacing", StringComparison.OrdinalIgnoreCase))
                return;
            if (data.NewData == null) return;

            // Drive FuelTracker every tick (cheap; lap boundary detected internally).
            // Note: StatusDataBase.IsInPitLane is int (0/1), not bool.
            try
            {
                _fuel.OnTick(
                    currentLap: data.NewData.CurrentLap,
                    currentFuelLiters: data.NewData.Fuel,
                    isOnPitRoad: data.NewData.IsInPitLane > 0,
                    isUnderYellow: data.NewData.Flag_Yellow > 0,
                    lastLapTime: data.NewData.LastLapTime);
            }
            catch { /* defensive: ignore one-off field-shape errors */ }

            // Map telemetry to our RaceState.
            var state = _mapper?.Map(data, _fuel.RollingAverageFuelPerLap, _fuel.HasEnoughDataForPrediction);
            if (state == null) return;

            // Refresh RaceConfig from session state every tick (cheap; fields rarely change).
            _raceConfig = BuildRaceConfig(state);

            // Always publish fast properties.
            _publisher?.PublishFastFrame(
                state.FuelLevelLiters, _fuel.RollingAverageFuelPerLap,
                _fuel.HasEnoughDataForPrediction);

            // Throttle the engine to ~1 Hz (no need to rebuild a recommendation every frame).
            var now = DateTime.UtcNow;
            if (now - _lastEngineRunUtc < _settings.EngineThrottle) return;
            _lastEngineRunUtc = now;

            try
            {
                _latest = _engine.Decide(state, _raceConfig);
                _publisher?.Publish(_latest, state.CurrentLap);
                if (_settings.EnableFrameDump) DumpFrame(state, _latest);
            }
            catch
            {
                // Swallow engine exceptions; the plugin should never bring SimHub down.
                // TODO: route to a SimHub logger once we identify which DLL ships it
                // (Logging class isn't in SimHub.Plugins.dll on this version).
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────────────────
        private RaceConfig BuildRaceConfig(RaceState state)
        {
            // Race length: prefer iRacing's lap count if known; otherwise timed.
            RaceLength length;
            if (state.LapsRemaining.HasValue && state.LapsRemaining.Value > 0)
            {
                length = new RaceLength.Laps(state.CurrentLap + state.LapsRemaining.Value);
            }
            else if (state.SessionTimeRemaining.HasValue)
            {
                length = new RaceLength.TimedPlusOneLap(state.SessionTimeRemaining.Value + state.SessionTimeElapsed);
            }
            else
            {
                length = new RaceLength.Timed(TimeSpan.FromHours(1));
            }

            var pitLoss = new PitLossConfig(
                PitLaneLengthMeters: _settings.OverridePitLaneLength
                    ? _settings.PitLaneLengthMetersOverride
                    : 350,                                        // TODO read from WeekendInfo when available
                PitLaneSpeedLimitKph: 60,                         // TODO read from WeekendInfo
                FuelFlowLitersPerSecond: _settings.FuelFlowLitersPerSecond,
                TireChangeServiceSeconds: _settings.TireChangeServiceSeconds,
                PitEntryDeltaSeconds: _settings.PitEntryDeltaSeconds,
                PitExitDeltaSeconds: _settings.PitExitDeltaSeconds,
                UsingDefaultPitLaneLength: !_settings.OverridePitLaneLength);

            var heuristics = StrategyHeuristics.Default with
            {
                UndercutThresholdSeconds = _settings.UndercutThresholdSeconds,
                CleanAirThresholdSeconds = _settings.CleanAirThresholdSeconds,
                CleanAirLookaheadLaps = _settings.CleanAirLookaheadLaps,
                FcyPitTimeSavingsFraction = _settings.FcyPitTimeSavingsFraction,
                MonteCarloTrials = _settings.MonteCarloTrials,
                FuelRollingWindowLaps = _settings.FuelRollingWindowLaps,
                FuelOutlierZScore = _settings.FuelOutlierZScore,
            };

            return new RaceConfig(
                Length: length,
                TankCapacityLiters: state.TankCapacityLiters,
                PitLoss: pitLoss,
                FuelSafetyMarginLaps: _settings.FuelSafetyMarginLaps,
                Heuristics: heuristics);
        }

        private static RaceConfig DefaultRaceConfig() =>
            new(Length: new RaceLength.Timed(TimeSpan.FromHours(1)),
                TankCapacityLiters: 60,
                PitLoss: PitLossConfig.GenericFallback);

        private void DumpLatestFrame()
        {
            if (_latest == null) return;
            DumpFrame(state: null, decision: _latest);
        }

        private void DumpFrame(RaceState? state, PitDecision decision)
        {
            try
            {
                string dir = Path.Combine(Path.GetTempPath(), "PitStrategy");
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, $"frame-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.json");
                var payload = new { state, decision };
                File.WriteAllText(file, JsonConvert.SerializeObject(payload, Formatting.Indented));
                _settings.LastFrameDumpPath = file;
            }
            catch
            {
                // best-effort frame dump
            }
        }
    }
}
