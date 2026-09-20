using System;
using Newtonsoft.Json;
using NesTrading.Core.Domain;
using NesTrading.Core.Execution;
using NesTrading.Core.Risk;
using NesTrading.Core.Storage;
using NesTrading.Core.Strategy;

namespace NesTrading.Core.Runtime
{
    public sealed class PaperStepInput
    {
        public PaperStepInput(MarketSnapshot snapshot, NewsState news, bool closeAtTarget, DateTime? outcomeTimeUtc)
        {
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            News = news ?? throw new ArgumentNullException(nameof(news));
            if (outcomeTimeUtc.HasValue && outcomeTimeUtc.Value.Kind != DateTimeKind.Utc)
                throw new ArgumentException("outcomeTimeUtc must be UTC.", nameof(outcomeTimeUtc));

            CloseAtTarget = closeAtTarget;
            OutcomeTimeUtc = outcomeTimeUtc;
        }

        public MarketSnapshot Snapshot { get; }
        public NewsState News { get; }
        public bool CloseAtTarget { get; }
        public DateTime? OutcomeTimeUtc { get; }
    }

    public sealed class PaperStepResult
    {
        public PaperStepResult(
            bool hasCandidate,
            RiskDecision riskDecision,
            ExecutionResult execution,
            TradeResult tradeResult,
            TradingSystemState finalSystemState)
        {
            HasCandidate = hasCandidate;
            RiskDecision = riskDecision;
            Execution = execution;
            TradeResult = tradeResult;
            FinalSystemState = finalSystemState;
        }

        public bool HasCandidate { get; }
        public RiskDecision RiskDecision { get; }
        public ExecutionResult Execution { get; }
        public TradeResult TradeResult { get; }
        public TradingSystemState FinalSystemState { get; }
    }

    public sealed class PaperSessionRunner
    {
        private readonly string _sessionId;
        private readonly TrendPullbackStrategy _strategy;
        private readonly RiskEngine _riskEngine;
        private readonly IExecutionAdapter _execution;
        private readonly IEventStore _eventStore;
        private readonly AccountRiskState _account;
        private readonly CostEstimate _costs;

        private TradingStateMachine _stateMachine;
        private TradingSessionState _sessionState;

        public PaperSessionRunner(
            string sessionId,
            TrendPullbackStrategy strategy,
            RiskEngine riskEngine,
            IExecutionAdapter execution,
            IEventStore eventStore,
            AccountRiskState account,
            CostEstimate costs)
        {
            if (string.IsNullOrWhiteSpace(sessionId)) throw new ArgumentException("sessionId is required.", nameof(sessionId));

            _sessionId = sessionId;
            _strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));
            _riskEngine = riskEngine ?? throw new ArgumentNullException(nameof(riskEngine));
            _execution = execution ?? throw new ArgumentNullException(nameof(execution));
            _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
            _account = account ?? throw new ArgumentNullException(nameof(account));
            _costs = costs ?? throw new ArgumentNullException(nameof(costs));

            var audit = _eventStore.Reconstruct(_sessionId);
            _sessionState = new TradingSessionState(
                audit.TradesToday,
                audit.LosingTradesToday,
                audit.HasOpenPosition,
                audit.DailyPnlUsd,
                audit.LastTradeClosedAtUtc,
                brokerPositionKnown: !audit.HasOpenPosition);

            _stateMachine = audit.HasOpenPosition
                ? TradingStateMachine.Restore(TradingSystemState.RiskLock)
                : TradingStateMachine.Restore(TradingSystemState.TradingEnabled);
        }

        public PaperStepResult Process(PaperStepInput input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));

            var now = input.Snapshot.TimestampUtc;

            if (_stateMachine.CurrentState == TradingSystemState.Flat)
                _stateMachine.Transition(SystemEvent.EnableTrading(now));

            var market = _strategy.EvaluateMarket(input.Snapshot, now);
            var candidate = _strategy.BuildTechnicalCandidate(market, input.Snapshot);

            if (candidate == null)
            {
                if (!TryAppend(TradingEvent.Create(
                    EventId(),
                    _sessionId,
                    "NO_TECHNICAL_SETUP",
                    now,
                    JsonConvert.SerializeObject(new
                    {
                        regime = market.Regime.ToString(),
                        score = market.SetupScore,
                        setup = market.Setup
                    }))))
                {
                    ForceSafeLock(false);
                }

                return new PaperStepResult(false, null, null, null, _stateMachine.CurrentState);
            }

            if (!TryAppend(TradingEvent.Create(
                EventId(),
                _sessionId,
                "CANDIDATE_EVALUATED",
                now,
                JsonConvert.SerializeObject(new
                {
                    symbol = candidate.Symbol,
                    direction = candidate.Direction.ToString(),
                    entry = candidate.EntryPrice,
                    stop = candidate.StructuralStop,
                    target = candidate.TargetPrice,
                    score = candidate.SetupScore
                }),
                candidate.SignalId)))
            {
                ForceSafeLock(false);
                return AuditFailure(candidate);
            }

            if (WasSignalPreviouslyEvaluated(candidate.SignalId))
            {
                var persistedDuplicate = RiskDecision.Reject(
                    candidate.SignalId,
                    "DUPLICATE_SIGNAL_PERSISTED",
                    candidate);

                TryAppend(TradingEvent.Create(
                    EventId(),
                    _sessionId,
                    "RISK_REJECTED",
                    now,
                    JsonConvert.SerializeObject(new { reason = persistedDuplicate.ReasonCode }),
                    candidate.SignalId));

                return new PaperStepResult(
                    true,
                    persistedDuplicate,
                    null,
                    null,
                    _stateMachine.CurrentState);
            }

            var tradeAction = candidate.Direction == TradeDirection.Long ? TradeAction.Long : TradeAction.Short;
            var tradeDecision = new TradeDecision(
                "NES",
                tradeAction,
                0.90m,
                candidate.SignalId,
                now,
                now.AddSeconds(15),
                new[] { "PAPER_DETERMINISTIC_DECISION" });

            var risk = _riskEngine.Evaluate(
                candidate,
                _account,
                _costs,
                _sessionState,
                input.News,
                market,
                tradeDecision,
                _stateMachine,
                now);

            var riskEventType = risk.IsApproved ? "RISK_APPROVED" : "RISK_REJECTED";
            if (!TryAppend(TradingEvent.Create(
                EventId(),
                _sessionId,
                riskEventType,
                now,
                JsonConvert.SerializeObject(new
                {
                    reason = risk.ReasonCode,
                    totalRiskUsd = risk.TotalRiskUsd,
                    riskPercent = risk.RiskPercent
                }),
                candidate.SignalId)))
            {
                ForceSafeLock(false);
                return AuditFailure(candidate);
            }

            if (!risk.IsApproved)
                return new PaperStepResult(true, risk, null, null, _stateMachine.CurrentState);

            var execution = _execution.SubmitBracket(risk);

            if (!execution.Accepted)
            {
                if (!TryAppend(TradingEvent.Create(
                    EventId(),
                    _sessionId,
                    "EXECUTION_REJECTED",
                    now,
                    JsonConvert.SerializeObject(new { reason = execution.ReasonCode }),
                    candidate.SignalId,
                    execution.TradeId)))
                {
                    ForceSafeLock(false);
                    return AuditFailure(candidate);
                }

                return new PaperStepResult(true, risk, execution, null, _stateMachine.CurrentState);
            }

            if (!TryAppend(TradingEvent.Create(
                EventId(),
                _sessionId,
                "EXECUTION_ACCEPTED",
                now,
                JsonConvert.SerializeObject(new
                {
                    tradeId = execution.TradeId,
                    entryFillPrice = execution.EntryFillPrice,
                    orderCount = execution.Orders.Count
                }),
                candidate.SignalId,
                execution.TradeId)))
            {
                ForceSafeLock(true);
                return AuditFailure(candidate, execution);
            }

            if (!TryAppend(TradingEvent.Create(
                EventId(),
                _sessionId,
                "TRADE_OPENED",
                now,
                JsonConvert.SerializeObject(new
                {
                    tradeId = execution.TradeId,
                    entryFillPrice = execution.EntryFillPrice,
                    stop = candidate.StructuralStop,
                    target = candidate.TargetPrice
                }),
                candidate.SignalId,
                execution.TradeId)))
            {
                ForceSafeLock(true);
                return AuditFailure(candidate, execution);
            }

            _stateMachine.Transition(SystemEvent.PositionOpened(now));
            _stateMachine.Transition(SystemEvent.BeginManaging(now));
            RefreshSessionState();

            if (!input.CloseAtTarget)
                return new PaperStepResult(true, risk, execution, null, _stateMachine.CurrentState);

            var closeTime = input.OutcomeTimeUtc ?? now.AddMinutes(1);
            var close = _execution.CloseAtTarget(execution.TradeId, closeTime);

            if (!close.Closed)
            {
                ForceSafeLock(true);
                return new PaperStepResult(
                    true,
                    risk,
                    execution,
                    null,
                    _stateMachine.CurrentState);
            }

            var pnlR = risk.TotalRiskUsd == 0m ? 0m : close.NetPnlUsd / risk.TotalRiskUsd;
            var tradeResult = new TradeResult(
                execution.TradeId,
                candidate.SignalId,
                close.NetPnlUsd,
                pnlR,
                closeTime);

            if (!TryAppend(TradingEvent.Create(
                EventId(),
                _sessionId,
                "TRADE_CLOSED",
                closeTime,
                JsonConvert.SerializeObject(new
                {
                    netPnlUsd = tradeResult.NetPnlUsd,
                    pnlR = tradeResult.PnlR,
                    exitFillPrice = close.ExitFillPrice
                }),
                candidate.SignalId,
                execution.TradeId)))
            {
                ForceSafeLock(false);
                return AuditFailure(candidate, execution);
            }

            var transition = _stateMachine.Transition(SystemEvent.PositionClosed(closeTime));
            if (!transition.Accepted)
            {
                ForceSafeLock(false);
                return new PaperStepResult(
                    true,
                    RiskDecision.Reject(candidate.SignalId, "STATE_TRANSITION_FAILURE", candidate),
                    execution,
                    tradeResult,
                    _stateMachine.CurrentState);
            }

            RefreshSessionState();

            if (!TryAppend(TradingEvent.Create(
                EventId(),
                _sessionId,
                "SESSION_METRICS",
                closeTime,
                JsonConvert.SerializeObject(new
                {
                    tradesToday = _sessionState.TradesToday,
                    losingTradesToday = _sessionState.LosingTradesToday,
                    dailyPnlUsd = _sessionState.DailyPnlUsd,
                    hasOpenPosition = _sessionState.HasOpenPosition,
                    lastTradeClosedAtUtc = _sessionState.LastTradeClosedAtUtc
                }),
                candidate.SignalId,
                execution.TradeId)))
            {
                ForceSafeLock(false);
                return new PaperStepResult(
                    true,
                    RiskDecision.Reject(
                        candidate.SignalId,
                        "AUDIT_PERSISTENCE_FAILURE",
                        candidate),
                    execution,
                    tradeResult,
                    _stateMachine.CurrentState);
            }

            return new PaperStepResult(
                true,
                risk,
                execution,
                tradeResult,
                _stateMachine.CurrentState);
        }

        private PaperStepResult AuditFailure(TradeCandidate candidate, ExecutionResult execution = null)
        {
            return new PaperStepResult(
                candidate != null,
                RiskDecision.Reject(
                    candidate == null ? "NO-SIGNAL" : candidate.SignalId,
                    "AUDIT_PERSISTENCE_FAILURE",
                    candidate),
                execution,
                null,
                _stateMachine.CurrentState);
        }

        private void RefreshSessionState()
        {
            var audit = _eventStore.Reconstruct(_sessionId);
            _sessionState = new TradingSessionState(
                audit.TradesToday,
                audit.LosingTradesToday,
                audit.HasOpenPosition,
                audit.DailyPnlUsd,
                audit.LastTradeClosedAtUtc,
                brokerPositionKnown: true);
        }

        private bool WasSignalPreviouslyEvaluated(string signalId)
        {
            try
            {
                foreach (var evt in _eventStore.LoadSession(_sessionId))
                {
                    if (!string.Equals(evt.SignalId, signalId, StringComparison.Ordinal))
                        continue;

                    if (evt.Type == "RISK_APPROVED" ||
                        evt.Type == "RISK_REJECTED" ||
                        evt.Type == "EXECUTION_ACCEPTED" ||
                        evt.Type == "EXECUTION_REJECTED" ||
                        evt.Type == "TRADE_OPENED" ||
                        evt.Type == "TRADE_CLOSED")
                        return true;
                }

                return false;
            }
            catch
            {
                // If historical audit state cannot be read, do not risk evaluating
                // the same signal as if the process had never seen it.
                ForceSafeLock(false);
                return true;
            }
        }

        private bool TryAppend(TradingEvent evt)
        {
            try
            {
                _eventStore.Append(evt);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void ForceSafeLock(bool mayHaveOpenPosition)
        {
            _stateMachine = TradingStateMachine.Restore(
                mayHaveOpenPosition
                    ? TradingSystemState.EmergencyStop
                    : TradingSystemState.RiskLock);
        }

        private static string EventId() => "evt-" + Guid.NewGuid().ToString("N");
    }
}
