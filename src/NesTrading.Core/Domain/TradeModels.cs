using System;
using System.Collections.Generic;

namespace NesTrading.Core.Domain
{
    public sealed class TradeCandidate
    {
        public TradeCandidate(string signalId, string symbol, TradeDirection direction, decimal entryPrice, decimal structuralStop,
            decimal targetPrice, int setupScore, int contracts, DateTime createdAtUtc)
        {
            SignalId = Guard.Required(signalId, nameof(signalId));
            Symbol = Guard.NesSymbol(symbol);
            if (direction != TradeDirection.Long && direction != TradeDirection.Short)
                throw new ArgumentException("Candidate direction must be Long or Short.", nameof(direction));
            if (entryPrice <= 0m) throw new ArgumentOutOfRangeException(nameof(entryPrice));
            if (structuralStop <= 0m) throw new ArgumentOutOfRangeException(nameof(structuralStop));
            if (targetPrice <= 0m) throw new ArgumentOutOfRangeException(nameof(targetPrice));
            if (setupScore < 0 || setupScore > 100) throw new ArgumentOutOfRangeException(nameof(setupScore));
            if (contracts != 1) throw new ArgumentOutOfRangeException(nameof(contracts), "V1 requires exactly one NES contract.");
            if (createdAtUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("createdAtUtc must be UTC.", nameof(createdAtUtc));

            Direction = direction;
            EntryPrice = entryPrice;
            StructuralStop = structuralStop;
            TargetPrice = targetPrice;
            SetupScore = setupScore;
            Contracts = contracts;
            CreatedAtUtc = createdAtUtc;
        }

        public string SignalId { get; }
        public string Symbol { get; }
        public TradeDirection Direction { get; }
        public decimal EntryPrice { get; }
        public decimal StructuralStop { get; }
        public decimal TargetPrice { get; }
        public int SetupScore { get; }
        public int Contracts { get; }
        public DateTime CreatedAtUtc { get; }
    }

    public sealed class RiskDecision
    {
        private RiskDecision(string signalId, RiskDecisionType type, string reasonCode, TradeCandidate candidate, decimal totalRiskUsd, decimal riskPercent)
        {
            SignalId = Guard.Required(signalId, nameof(signalId));
            Type = type;
            ReasonCode = Guard.Required(reasonCode, nameof(reasonCode));
            Candidate = candidate;
            TotalRiskUsd = totalRiskUsd;
            RiskPercent = riskPercent;
        }

        public string SignalId { get; }
        public RiskDecisionType Type { get; }
        public string ReasonCode { get; }
        public TradeCandidate Candidate { get; }
        public decimal TotalRiskUsd { get; }
        public decimal RiskPercent { get; }
        public bool IsApproved => Type == RiskDecisionType.Approved;

        public static RiskDecision Approve(TradeCandidate candidate, decimal totalRiskUsd, decimal riskPercent)
        {
            if (candidate == null) throw new ArgumentNullException(nameof(candidate));
            return new RiskDecision(candidate.SignalId, RiskDecisionType.Approved, "APPROVED", candidate, totalRiskUsd, riskPercent);
        }

        public static RiskDecision Reject(string signalId, string reasonCode, TradeCandidate candidate = null, decimal totalRiskUsd = 0m, decimal riskPercent = 0m)
        {
            return new RiskDecision(signalId, RiskDecisionType.Rejected, reasonCode, candidate, totalRiskUsd, riskPercent);
        }
    }

    public sealed class TradeResult
    {
        public TradeResult(string tradeId, string signalId, decimal netPnlUsd, decimal pnlR, DateTime closedAtUtc)
        {
            TradeId = Guard.Required(tradeId, nameof(tradeId));
            SignalId = Guard.Required(signalId, nameof(signalId));
            if (closedAtUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("closedAtUtc must be UTC.", nameof(closedAtUtc));
            NetPnlUsd = netPnlUsd;
            PnlR = pnlR;
            ClosedAtUtc = closedAtUtc;
        }

        public string TradeId { get; }
        public string SignalId { get; }
        public decimal NetPnlUsd { get; }
        public decimal PnlR { get; }
        public DateTime ClosedAtUtc { get; }
    }
}
