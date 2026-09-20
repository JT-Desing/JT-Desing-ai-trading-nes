using System;
using System.Collections.Generic;
using System.Linq;
using NesTrading.Core.Domain;

namespace NesTrading.Core.Execution
{
    public enum OrderRole { Entry, Stop, Target }
    public enum AckState { Accepted, Rejected, Unknown }
    public enum BrokerOrderPresence { Present, Absent }

    public sealed class SimulationExecutionSettings
    {
        public SimulationExecutionSettings(
            int entrySlippageTicks,
            int exitSlippageTicks,
            decimal roundTripFeesUsd,
            bool forceMissedFill,
            AckState nextAckState)
        {
            if (entrySlippageTicks < 0) throw new ArgumentOutOfRangeException(nameof(entrySlippageTicks));
            if (exitSlippageTicks < 0) throw new ArgumentOutOfRangeException(nameof(exitSlippageTicks));
            if (roundTripFeesUsd < 0m) throw new ArgumentOutOfRangeException(nameof(roundTripFeesUsd));

            EntrySlippageTicks = entrySlippageTicks;
            ExitSlippageTicks = exitSlippageTicks;
            RoundTripFeesUsd = roundTripFeesUsd;
            ForceMissedFill = forceMissedFill;
            NextAckState = nextAckState;
        }

        public int EntrySlippageTicks { get; }
        public int ExitSlippageTicks { get; }
        public decimal RoundTripFeesUsd { get; }
        public bool ForceMissedFill { get; }
        public AckState NextAckState { get; }

        public static SimulationExecutionSettings Defaults() =>
            new SimulationExecutionSettings(0, 0, 0m, false, AckState.Accepted);
    }

    public sealed class SimulatedOrder
    {
        public SimulatedOrder(string orderId, string tradeId, string signalId, OrderRole role, decimal price)
        {
            if (string.IsNullOrWhiteSpace(orderId)) throw new ArgumentException("orderId is required.", nameof(orderId));
            if (string.IsNullOrWhiteSpace(tradeId)) throw new ArgumentException("tradeId is required.", nameof(tradeId));
            if (string.IsNullOrWhiteSpace(signalId)) throw new ArgumentException("signalId is required.", nameof(signalId));
            if (price <= 0m) throw new ArgumentOutOfRangeException(nameof(price));

            OrderId = orderId;
            TradeId = tradeId;
            SignalId = signalId;
            Role = role;
            Price = price;
        }

        public string OrderId { get; }
        public string TradeId { get; }
        public string SignalId { get; }
        public OrderRole Role { get; }
        public decimal Price { get; }
    }

    public sealed class ExecutionResult
    {
        private ExecutionResult(bool accepted, string reasonCode, string tradeId, decimal? entryFillPrice, IReadOnlyList<SimulatedOrder> orders)
        {
            Accepted = accepted;
            ReasonCode = reasonCode;
            TradeId = tradeId;
            EntryFillPrice = entryFillPrice;
            Orders = orders ?? Array.Empty<SimulatedOrder>();
        }

        public bool Accepted { get; }
        public string ReasonCode { get; }
        public string TradeId { get; }
        public decimal? EntryFillPrice { get; }
        public IReadOnlyList<SimulatedOrder> Orders { get; }

        public static ExecutionResult Success(string tradeId, decimal entryFillPrice, IReadOnlyList<SimulatedOrder> orders) =>
            new ExecutionResult(true, "ACCEPTED", tradeId, entryFillPrice, orders);

        public static ExecutionResult Reject(string reasonCode, IReadOnlyList<SimulatedOrder> orders = null, string tradeId = null) =>
            new ExecutionResult(false, reasonCode, tradeId, null, orders);
    }

    public sealed class ReconciliationResult
    {
        public ReconciliationResult(bool resolved, bool mayRetry, string reasonCode)
        {
            Resolved = resolved;
            MayRetry = mayRetry;
            ReasonCode = reasonCode;
        }

        public bool Resolved { get; }
        public bool MayRetry { get; }
        public string ReasonCode { get; }
    }

    public sealed class SimulatedOpenTrade
    {
        public SimulatedOpenTrade(string tradeId, string signalId, TradeDirection direction, decimal entryFillPrice, decimal stopPrice, decimal targetPrice)
        {
            TradeId = tradeId;
            SignalId = signalId;
            Direction = direction;
            EntryFillPrice = entryFillPrice;
            StopPrice = stopPrice;
            TargetPrice = targetPrice;
        }

        public string TradeId { get; }
        public string SignalId { get; }
        public TradeDirection Direction { get; }
        public decimal EntryFillPrice { get; }
        public decimal StopPrice { get; }
        public decimal TargetPrice { get; }
    }

    public sealed class BrokerSnapshot
    {
        public BrokerSnapshot(
            IEnumerable<SimulatedOpenTrade> openTrades,
            IEnumerable<string> unresolvedSignals,
            bool externalExposurePresent = false)
        {
            OpenTrades = (openTrades ?? Enumerable.Empty<SimulatedOpenTrade>()).ToArray();
            UnresolvedSignals = (unresolvedSignals ?? Enumerable.Empty<string>()).ToArray();
            ExternalExposurePresent = externalExposurePresent;
        }

        public IReadOnlyList<SimulatedOpenTrade> OpenTrades { get; }
        public IReadOnlyList<string> UnresolvedSignals { get; }
        public bool ExternalExposurePresent { get; }
    }

    public sealed class CloseResult
    {
        public CloseResult(bool closed, string reasonCode, decimal? exitFillPrice, decimal netPnlUsd)
        {
            Closed = closed;
            ReasonCode = reasonCode;
            ExitFillPrice = exitFillPrice;
            NetPnlUsd = netPnlUsd;
        }

        public bool Closed { get; }
        public string ReasonCode { get; }
        public decimal? ExitFillPrice { get; }
        public decimal NetPnlUsd { get; }
    }
}
