using System;
using System.Collections.Generic;
using System.Diagnostics;
using PitStrategy.Core.Inputs;
using PitStrategy.Core.Outputs;
using PitStrategy.Core.Pit;
using PitStrategy.Core.PitWindow;
using PitStrategy.Core.Traffic;
using PitStrategy.Core.Analysis;

namespace PitStrategy.Core.Strategy
{
    /// <summary>
    /// Rule-based pit-strategy engine. Returns a single <see cref="PitDecision"/> per call;
    /// callers should throttle invocation to ~1 Hz (cheap, but no need to recompute every
    /// 60 Hz telemetry tick).
    ///
    /// Decision tree (evaluated in order — first match wins):
    ///   1. Forced fuel pit  — fuel runs out before any other window opens.
    ///   2. No pit needed     — fuel covers the rest of the race with margin.
    ///   3. FCY opportunity   — pit while the field is bunched (Phase 2).
    ///   4. Undercut window   — fresher tires and a pit-loss gap let us pass a rival (Phase 2).
    ///   5. Clean-air timing  — wait one or more laps for a clean rejoin (Phase 2).
    ///   6. Default optimal   — pit at the centre of the legal window.
    /// </summary>
    public sealed class StrategyEngine
    {
        public PitDecision Decide(RaceState state, RaceConfig config)
        {
            var sw = Stopwatch.StartNew();

            if (state.HasInsufficientData)
            {
                sw.Stop();
                return PitDecision.Unknown("Waiting for green-flag fuel data", sw.Elapsed);
            }

            var window = PitWindowCalculator.Compute(state, config);
            if (!window.HasEnoughData)
            {
                sw.Stop();
                return PitDecision.Unknown("Pit-window calculator has insufficient data", sw.Elapsed);
            }

            // Recommended fuel-to-add: enough to cover the remaining stint after the pit.
            double fuelToAdd = ComputeRecommendedFuelToAdd(state, config, window);

            // Build the service request the engine recommends (refuel always, tires only if allowed
            // and we're past a heuristic stint length).
            var serviceRequest = new PitServiceRequest(
                RefuelArmed: config.RefuelingAllowed && fuelToAdd > 0,
                FuelToAddLiters: Math.Max(0, fuelToAdd),
                TireChangeArmed: config.TireChangesAllowed);

            var loss = PitLossModel.Compute(config.PitLoss, serviceRequest);

            // ── Phase 2 traffic projection ────────────────────────────────────────────────
            // Computed best-effort even at Phase 1 — falls back to TrafficProjection.Empty
            // when no rivals are present.
            var heuristics = config.ResolvedHeuristics;
            var trafficNow = RejoinPredictor.Project(
                state, config, candidatePitLap: state.CurrentLap, pitLossSeconds: loss.TotalSeconds);

            // Tactical alerts (Phase 2; safe to evaluate in Phase 1 — they return null when N/A).
            var fcy = FcyAnalyzer.Evaluate(state, config, loss);
            var undercut = UndercutDetector.Evaluate(state, config, loss);

            int stintsRemaining = Math.Max(1, window.MinStops);
            int recommendedLap = window.RecommendedLap ?? state.CurrentLap;

            // ── Decision tree ─────────────────────────────────────────────────────────────
            // Rule 1: forced fuel pit.
            if (window.IsForcedPitLap)
            {
                sw.Stop();
                return new PitDecision(
                    Kind: PitDecisionKind.PitForFuelOnly,
                    Confidence: 1.0,
                    RecommendedPitLap: state.CurrentLap,
                    LossBreakdown: loss,
                    TrafficAfterPit: trafficNow,
                    Undercut: undercut,
                    Fcy: fcy,
                    PrimaryReason: "Fuel runs out — pit immediately",
                    SecondaryFactors: new[] { $"Laps to empty: {window.LapsToEmpty:0.0}" },
                    ComputeTime: sw.Elapsed,
                    LapsToEmpty: window.LapsToEmpty,
                    LapsToEmptyAtSafetyMargin: window.LapsToEmptyAtSafetyMargin,
                    MinFuelToAddLiters: fuelToAdd,
                    StintsRemaining: stintsRemaining,
                    ComparedStrategies: Array.Empty<ComparedStrategy>());
            }

            // Rule 2: no pit needed.
            if (!window.RequiresPit)
            {
                sw.Stop();
                return new PitDecision(
                    Kind: PitDecisionKind.StayOut,
                    Confidence: 0.95,
                    RecommendedPitLap: null,
                    LossBreakdown: loss,
                    TrafficAfterPit: TrafficProjection.Empty,
                    Undercut: undercut,
                    Fcy: fcy,
                    PrimaryReason: "Fuel covers the race — no pit needed",
                    SecondaryFactors: new[] { $"Laps to empty: {window.LapsToEmpty:0.0}",
                                              $"Laps remaining: {window.LapsRemainingEstimated:0.0}" },
                    ComputeTime: sw.Elapsed,
                    LapsToEmpty: window.LapsToEmpty,
                    LapsToEmptyAtSafetyMargin: window.LapsToEmptyAtSafetyMargin,
                    MinFuelToAddLiters: 0,
                    StintsRemaining: 0,
                    ComparedStrategies: Array.Empty<ComparedStrategy>());
            }

            // Rule 3: FCY opportunity (Phase 2).
            if (fcy != null && fcy.ShouldPitNow)
            {
                sw.Stop();
                return new PitDecision(
                    Kind: PitDecisionKind.PitNow,
                    Confidence: 0.85,
                    RecommendedPitLap: state.CurrentLap,
                    LossBreakdown: loss,
                    TrafficAfterPit: trafficNow,
                    Undercut: undercut,
                    Fcy: fcy,
                    PrimaryReason: $"Free pit under FCY saves {fcy.TimeSavingsSeconds:0.0}s",
                    SecondaryFactors: new[] { fcy.Rationale },
                    ComputeTime: sw.Elapsed,
                    LapsToEmpty: window.LapsToEmpty,
                    LapsToEmptyAtSafetyMargin: window.LapsToEmptyAtSafetyMargin,
                    MinFuelToAddLiters: fuelToAdd,
                    StintsRemaining: stintsRemaining,
                    ComparedStrategies: Array.Empty<ComparedStrategy>());
            }

            // Rule 4: undercut (Phase 2). Only fire when we're inside the legal pit window.
            bool insideWindow = state.CurrentLap >= (window.EarliestLap ?? int.MaxValue)
                                && state.CurrentLap <= (window.LatestLap ?? int.MinValue);

            if (insideWindow && undercut != null && undercut.ExpectedGainSeconds > 0)
            {
                double conf = 0.7;
                // Damp confidence if rival pace is very inconsistent.
                if (state.Rivals != null)
                {
                    foreach (var r in state.Rivals)
                    {
                        if (r.CarIdx == undercut.RivalCarIdx
                            && r.RecentLapTimeStdDevSeconds > heuristics.LowConfidenceRivalPaceStdDevSeconds)
                        {
                            conf -= 0.15;
                        }
                    }
                }

                sw.Stop();
                return new PitDecision(
                    Kind: PitDecisionKind.PitNow,
                    Confidence: Math.Max(0.4, conf),
                    RecommendedPitLap: state.CurrentLap,
                    LossBreakdown: loss,
                    TrafficAfterPit: trafficNow,
                    Undercut: undercut,
                    Fcy: fcy,
                    PrimaryReason: $"Undercut #{undercut.RivalCarIdx} — gain {undercut.ExpectedGainSeconds:0.0}s",
                    SecondaryFactors: new[] { undercut.Rationale },
                    ComputeTime: sw.Elapsed,
                    LapsToEmpty: window.LapsToEmpty,
                    LapsToEmptyAtSafetyMargin: window.LapsToEmptyAtSafetyMargin,
                    MinFuelToAddLiters: fuelToAdd,
                    StintsRemaining: stintsRemaining,
                    ComparedStrategies: Array.Empty<ComparedStrategy>());
            }

            // Rule 5: clean-air timing (Phase 2). If pitting *now* dumps us in traffic but a small
            // lookahead puts us in clean air, advise PitNextLap (or StayOut for >1 lap delay).
            if (insideWindow && trafficNow.IsAvailable && !trafficNow.IsCleanAir
                && trafficNow.CleanAirLapIfWait.HasValue
                && trafficNow.CleanAirLapIfWait.Value > state.CurrentLap)
            {
                int delta = trafficNow.CleanAirLapIfWait.Value - state.CurrentLap;
                var kind = delta == 1 ? PitDecisionKind.PitNextLap : PitDecisionKind.StayOut;
                sw.Stop();
                return new PitDecision(
                    Kind: kind,
                    Confidence: 0.6,
                    RecommendedPitLap: trafficNow.CleanAirLapIfWait,
                    LossBreakdown: loss,
                    TrafficAfterPit: trafficNow,
                    Undercut: undercut,
                    Fcy: fcy,
                    PrimaryReason: delta == 1
                        ? "Wait 1 lap — rejoin in clean air"
                        : $"Wait {delta} laps — rejoin in clean air",
                    SecondaryFactors: new[] {
                        $"Pitting now puts you between cars (gaps {trafficNow.GapToCarBehindSeconds:0.0}s / {trafficNow.GapToCarAheadSeconds:0.0}s)" },
                    ComputeTime: sw.Elapsed,
                    LapsToEmpty: window.LapsToEmpty,
                    LapsToEmptyAtSafetyMargin: window.LapsToEmptyAtSafetyMargin,
                    MinFuelToAddLiters: fuelToAdd,
                    StintsRemaining: stintsRemaining,
                    ComparedStrategies: Array.Empty<ComparedStrategy>());
            }

            // Rule 6: default optimal — pit at recommended lap. Stay out until then; pit when we hit it.
            if (state.CurrentLap >= recommendedLap)
            {
                sw.Stop();
                return new PitDecision(
                    Kind: PitDecisionKind.PitNow,
                    Confidence: 0.75,
                    RecommendedPitLap: state.CurrentLap,
                    LossBreakdown: loss,
                    TrafficAfterPit: trafficNow,
                    Undercut: undercut,
                    Fcy: fcy,
                    PrimaryReason: $"Optimal pit window (laps {window.EarliestLap}–{window.LatestLap})",
                    SecondaryFactors: new[] { $"Add {fuelToAdd:0.0}L" },
                    ComputeTime: sw.Elapsed,
                    LapsToEmpty: window.LapsToEmpty,
                    LapsToEmptyAtSafetyMargin: window.LapsToEmptyAtSafetyMargin,
                    MinFuelToAddLiters: fuelToAdd,
                    StintsRemaining: stintsRemaining,
                    ComparedStrategies: Array.Empty<ComparedStrategy>());
            }

            // Default: stay out, give the lap-count to the window opening.
            int lapsUntilPit = recommendedLap - state.CurrentLap;
            sw.Stop();
            return new PitDecision(
                Kind: PitDecisionKind.StayOut,
                Confidence: 0.9,
                RecommendedPitLap: recommendedLap,
                LossBreakdown: loss,
                TrafficAfterPit: TrafficProjection.Empty,
                Undercut: undercut,
                Fcy: fcy,
                PrimaryReason: lapsUntilPit == 1
                    ? "Pit next lap"
                    : $"Pit in {lapsUntilPit} laps (window opens lap {window.EarliestLap})",
                SecondaryFactors: new[] {
                    $"Recommended pit lap: {recommendedLap}",
                    $"Latest legal pit lap: {window.LatestLap}" },
                ComputeTime: sw.Elapsed,
                LapsToEmpty: window.LapsToEmpty,
                LapsToEmptyAtSafetyMargin: window.LapsToEmptyAtSafetyMargin,
                MinFuelToAddLiters: fuelToAdd,
                StintsRemaining: stintsRemaining,
                ComparedStrategies: Array.Empty<ComparedStrategy>());
        }

        /// <summary>
        /// Recommended litres to add at the next stop: enough to cover the remaining stint
        /// after the pit, capped by the tank capacity.
        /// </summary>
        private static double ComputeRecommendedFuelToAdd(RaceState state, RaceConfig config, PitWindow.PitWindow window)
        {
            if (!window.RequiresPit) return 0;

            double fuelPerLap = state.RollingFuelPerLap;
            int pitLap = window.RecommendedLap ?? state.CurrentLap;
            double lapsAfterPit = window.LapsRemainingEstimated - Math.Max(0, pitLap - state.CurrentLap);
            if (lapsAfterPit <= 0) return 0;
            double need = lapsAfterPit * fuelPerLap + config.FuelSafetyMarginLaps * fuelPerLap;
            return Math.Min(need, config.TankCapacityLiters);
        }
    }
}
