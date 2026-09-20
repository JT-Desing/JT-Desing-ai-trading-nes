using System;
using System.Collections.Generic;
using NesTrading.Core.Domain;
using NesTrading.Core.Runtime;

namespace NesTrading.Core.Risk
{
    public sealed class RiskEngine
    {
        public const decimal NesUsdPerPoint = 0.50m;

        private readonly RiskPolicy _policy;
        private readonly HashSet<string> _evaluatedSignals = new HashSet<string>(StringComparer.Ordinal);

        public RiskEngine(RiskPolicy policy)
        {
            _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        }

        public RiskDecision Evaluate(
            TradeCandidate candidate,
            AccountRiskState account,
            CostEstimate costs,
            TradingSessionState session,
            NewsState news,
            MarketState market,
            TradeDecision trade,
            TradingStateMachine stateMachine,
            DateTime utcNow)
        {
            if (candidate == null) throw new ArgumentNullException(nameof(candidate));
            if (account == null) throw new ArgumentNullException(nameof(account));
            if (costs == null) throw new ArgumentNullException(nameof(costs));
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (news == null) throw new ArgumentNullException(nameof(news));
            if (market == null) throw new ArgumentNullException(nameof(market));
            if (trade == null) throw new ArgumentNullException(nameof(trade));
            if (stateMachine == null) throw new ArgumentNullException(nameof(stateMachine));
            if (utcNow.Kind != DateTimeKind.Utc) throw new ArgumentException("utcNow must be UTC.", nameof(utcNow));

            if (_evaluatedSignals.Contains(candidate.SignalId))
                return Reject(candidate, "DUPLICATE_SIGNAL");

            // A signal is single-use even when rejected. Re-evaluation must use a new signal ID.
            _evaluatedSignals.Add(candidate.SignalId);

            if (account.EquityUsd <= 0m)
                return Reject(candidate, "INVALID_ACCOUNT_EQUITY");

            if (!stateMachine.CanSubmitNewOrder(utcNow))
                return Reject(candidate, "SYSTEM_STATE_LOCKED");

            if (!session.BrokerPositionKnown)
                return Reject(candidate, "BROKER_POSITION_UNKNOWN");

            if (session.HasOpenPosition)
                return Reject(candidate, "POSITION_ALREADY_OPEN");

            if (session.LosingTradesToday >= _policy.MaxLosingTradesPerDay)
                return Reject(candidate, "MAX_LOSSES_REACHED");

            if (session.TradesToday >= _policy.MaxTradesPerDay)
                return Reject(candidate, "MAX_TRADES_REACHED");

            var hardLossUsd = account.EquityUsd * _policy.DailyHardLossPercent;
            if (session.DailyPnlUsd <= -hardLossUsd)
                return Reject(candidate, "DAILY_HARD_LOSS_LOCK");

            var softLossUsd = account.EquityUsd * _policy.DailySoftLossPercent;
            if (session.DailyPnlUsd <= -softLossUsd)
                return Reject(candidate, "DAILY_SOFT_LOSS_LOCK");

            if (session.LastTradeClosedAtUtc.HasValue &&
                utcNow < session.LastTradeClosedAtUtc.Value + _policy.Cooldown)
                return Reject(candidate, "COOLDOWN_ACTIVE");

            if (!IsInsideTradingSession(utcNow))
                return Reject(candidate, "OUTSIDE_SESSION");

            if (!news.IsFreshAt(utcNow))
                return Reject(candidate, "STALE_NEWS_STATE");

            if (!market.IsFreshAt(utcNow))
                return Reject(candidate, "STALE_MARKET_STATE");

            if (!trade.IsFreshAt(utcNow))
                return Reject(candidate, "STALE_TRADE_STATE");

            if (news.State == NewsGate.Block)
                return Reject(candidate, "NEWS_BLOCK");

            if (news.State == NewsGate.Caution && market.SetupScore < 85)
                return Reject(candidate, "NEWS_CAUTION_SCORE_TOO_LOW");

            if (!market.Valid ||
                (market.Regime != MarketRegime.TrendUp && market.Regime != MarketRegime.TrendDown))
                return Reject(candidate, "MARKET_NOT_TRADABLE");

            if (market.Direction != candidate.Direction)
                return Reject(candidate, "MARKET_DIRECTION_MISMATCH");

            if (trade.Action != TradeAction.Long && trade.Action != TradeAction.Short)
                return Reject(candidate, "TRADE_DECISION_NOT_EXECUTABLE");

            if (!string.Equals(trade.SignalId, candidate.SignalId, StringComparison.Ordinal))
                return Reject(candidate, "SIGNAL_ID_MISMATCH");

            var expectedAction = candidate.Direction == TradeDirection.Long ? TradeAction.Long : TradeAction.Short;
            if (trade.Action != expectedAction)
                return Reject(candidate, "DIRECTION_MISMATCH");

            if (candidate.Contracts != _policy.MaxContracts)
                return Reject(candidate, "INVALID_CONTRACT_COUNT");

            if (costs.SpreadTicks > _policy.MaxSpreadTicks)
                return Reject(candidate, "SPREAD_TOO_WIDE");

            if (costs.ExpectedSlippageUsd > _policy.MaxExpectedSlippageUsd)
                return Reject(candidate, "SLIPPAGE_TOO_HIGH");

            var stopDistancePoints = Math.Abs(candidate.EntryPrice - candidate.StructuralStop);
            var rewardDistancePoints = Math.Abs(candidate.TargetPrice - candidate.EntryPrice);
            if (stopDistancePoints <= 0m || rewardDistancePoints <= 0m)
                return Reject(candidate, "INVALID_BRACKET_GEOMETRY");

            if (candidate.Direction == TradeDirection.Long &&
                !(candidate.StructuralStop < candidate.EntryPrice && candidate.TargetPrice > candidate.EntryPrice))
                return Reject(candidate, "INVALID_BRACKET_GEOMETRY");

            if (candidate.Direction == TradeDirection.Short &&
                !(candidate.StructuralStop > candidate.EntryPrice && candidate.TargetPrice < candidate.EntryPrice))
                return Reject(candidate, "INVALID_BRACKET_GEOMETRY");

            var grossRiskUsd = stopDistancePoints * NesUsdPerPoint * candidate.Contracts;
            var totalRiskUsd = grossRiskUsd + costs.TotalFrictionUsd;
            var riskPercent = totalRiskUsd / account.EquityUsd;

            var grossRewardUsd = rewardDistancePoints * NesUsdPerPoint * candidate.Contracts;
            var netRewardUsd = Math.Max(0m, grossRewardUsd - costs.TotalFrictionUsd);
            var netRewardRisk = totalRiskUsd > 0m ? netRewardUsd / totalRiskUsd : 0m;

            // The hard monetary risk cap has diagnostic priority over trade-quality filters.
            // A structurally wide stop must be identified as a risk-cap rejection, never "fixed"
            // by tightening the stop or obscured behind a reward/risk reason.
            if (riskPercent > _policy.HardMaxRiskPercent)
                return Reject(candidate, "RISK_PER_TRADE_HARD_MAX", totalRiskUsd, riskPercent);

            if (netRewardRisk < _policy.MinNetRewardRisk)
                return Reject(candidate, "REWARD_RISK_TOO_LOW", totalRiskUsd, riskPercent);

            return RiskDecision.Approve(candidate, totalRiskUsd, riskPercent);
        }

        private RiskDecision Reject(TradeCandidate candidate, string reason, decimal totalRiskUsd = 0m, decimal riskPercent = 0m)
        {
            return RiskDecision.Reject(candidate.SignalId, reason, candidate, totalRiskUsd, riskPercent);
        }

        private bool IsInsideTradingSession(DateTime utcNow)
        {
            var eastern = TimeZoneInfo.ConvertTimeFromUtc(utcNow, FindEasternTimeZone());
            if (eastern.DayOfWeek == DayOfWeek.Saturday || eastern.DayOfWeek == DayOfWeek.Sunday)
                return false;

            var time = eastern.TimeOfDay;
            return time >= _policy.SessionStartEastern && time <= _policy.SessionEndEastern;
        }

        private static TimeZoneInfo FindEasternTimeZone()
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
            }
            catch (TimeZoneNotFoundException)
            {
                return TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
            }
        }
    }
}
