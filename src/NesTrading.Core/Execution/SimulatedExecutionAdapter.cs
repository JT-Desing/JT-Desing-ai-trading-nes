using System;
using System.Collections.Generic;
using System.Linq;
using NesTrading.Core.Domain;
using NesTrading.Core.Risk;

namespace NesTrading.Core.Execution
{
    public sealed class SimulatedExecutionAdapter : IExecutionAdapter
    {
        public const decimal NesTickSize = 0.25m;

        private readonly SimulationExecutionSettings _settings;
        private readonly Dictionary<string, SimulatedOpenTrade> _openTrades = new Dictionary<string, SimulatedOpenTrade>(StringComparer.Ordinal);
        private readonly HashSet<string> _consumedSignals = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _unresolvedSignals = new HashSet<string>(StringComparer.Ordinal);
        private AckState _nextAckState;

        public SimulatedExecutionAdapter(SimulationExecutionSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _nextAckState = settings.NextAckState;
        }

        public void SetNextAckState(AckState state)
        {
            _nextAckState = state;
        }

        public ExecutionResult SubmitBracket(RiskDecision approvedDecision)
        {
            if (approvedDecision == null) throw new ArgumentNullException(nameof(approvedDecision));

            if (!approvedDecision.IsApproved || approvedDecision.Candidate == null)
                return ExecutionResult.Reject("RISK_NOT_APPROVED");

            var candidate = approvedDecision.Candidate;
            var signalId = candidate.SignalId;

            if (_unresolvedSignals.Contains(signalId))
                return ExecutionResult.Reject("ACK_RECONCILIATION_REQUIRED");

            if (_consumedSignals.Contains(signalId))
                return ExecutionResult.Reject("DUPLICATE_SIGNAL");

            if (_openTrades.Count > 0)
                return ExecutionResult.Reject("POSITION_ALREADY_OPEN");

            if (!HasValidBracketGeometry(candidate))
                return ExecutionResult.Reject("INVALID_BRACKET_GEOMETRY");

            var tradeId = "trade-" + Guid.NewGuid().ToString("N");
            var orders = BuildBracketOrders(tradeId, candidate);

            if (_nextAckState == AckState.Unknown)
            {
                _unresolvedSignals.Add(signalId);
                return ExecutionResult.Reject("ACK_UNKNOWN", orders, tradeId);
            }

            if (_nextAckState == AckState.Rejected)
            {
                _consumedSignals.Add(signalId);
                return ExecutionResult.Reject("BROKER_REJECTED", orders, tradeId);
            }

            _consumedSignals.Add(signalId);

            if (_settings.ForceMissedFill)
                return ExecutionResult.Reject("MISSED_FILL", orders, tradeId);

            var entryFill = ApplyEntrySlippage(candidate);
            var open = new SimulatedOpenTrade(
                tradeId,
                signalId,
                candidate.Direction,
                entryFill,
                candidate.StructuralStop,
                candidate.TargetPrice);

            _openTrades.Add(tradeId, open);
            return ExecutionResult.Success(tradeId, entryFill, orders);
        }

        public BrokerSnapshot GetSnapshot()
        {
            return new BrokerSnapshot(_openTrades.Values.ToArray(), _unresolvedSignals.ToArray());
        }

        public ReconciliationResult Reconcile(string signalId, BrokerOrderPresence presence)
        {
            if (string.IsNullOrWhiteSpace(signalId)) throw new ArgumentException("signalId is required.", nameof(signalId));

            if (!_unresolvedSignals.Contains(signalId))
                return new ReconciliationResult(false, false, "NO_UNRESOLVED_SIGNAL");

            _unresolvedSignals.Remove(signalId);

            if (presence == BrokerOrderPresence.Present)
            {
                _consumedSignals.Add(signalId);
                return new ReconciliationResult(true, false, "BROKER_ORDER_PRESENT");
            }

            return new ReconciliationResult(true, true, "BROKER_ORDER_ABSENT");
        }

        public CloseResult CloseAtTarget(string tradeId, DateTime closedAtUtc)
        {
            if (string.IsNullOrWhiteSpace(tradeId)) throw new ArgumentException("tradeId is required.", nameof(tradeId));
            if (closedAtUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("closedAtUtc must be UTC.", nameof(closedAtUtc));

            SimulatedOpenTrade trade;
            if (!_openTrades.TryGetValue(tradeId, out trade))
                return new CloseResult(false, "TRADE_NOT_OPEN", null, 0m);

            var adverseExitSlippage = _settings.ExitSlippageTicks * NesTickSize;
            decimal exitFill;
            decimal points;

            if (trade.Direction == TradeDirection.Long)
            {
                exitFill = trade.TargetPrice - adverseExitSlippage;
                points = exitFill - trade.EntryFillPrice;
            }
            else
            {
                exitFill = trade.TargetPrice + adverseExitSlippage;
                points = trade.EntryFillPrice - exitFill;
            }

            var grossPnl = points * RiskEngine.NesUsdPerPoint;
            var netPnl = grossPnl - _settings.RoundTripFeesUsd;

            _openTrades.Remove(tradeId);
            return new CloseResult(true, "CLOSED_AT_TARGET", exitFill, netPnl);
        }

        private decimal ApplyEntrySlippage(TradeCandidate candidate)
        {
            var adverse = _settings.EntrySlippageTicks * NesTickSize;
            return candidate.Direction == TradeDirection.Long
                ? candidate.EntryPrice + adverse
                : candidate.EntryPrice - adverse;
        }

        private static bool HasValidBracketGeometry(TradeCandidate candidate)
        {
            if (candidate.Direction == TradeDirection.Long)
                return candidate.StructuralStop < candidate.EntryPrice && candidate.TargetPrice > candidate.EntryPrice;

            if (candidate.Direction == TradeDirection.Short)
                return candidate.StructuralStop > candidate.EntryPrice && candidate.TargetPrice < candidate.EntryPrice;

            return false;
        }

        private static IReadOnlyList<SimulatedOrder> BuildBracketOrders(string tradeId, TradeCandidate candidate)
        {
            return new[]
            {
                new SimulatedOrder("ord-" + Guid.NewGuid().ToString("N"), tradeId, candidate.SignalId, OrderRole.Entry, candidate.EntryPrice),
                new SimulatedOrder("ord-" + Guid.NewGuid().ToString("N"), tradeId, candidate.SignalId, OrderRole.Stop, candidate.StructuralStop),
                new SimulatedOrder("ord-" + Guid.NewGuid().ToString("N"), tradeId, candidate.SignalId, OrderRole.Target, candidate.TargetPrice)
            };
        }
    }
}
