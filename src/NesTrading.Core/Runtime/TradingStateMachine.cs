using System;
using System.Collections.Generic;

namespace NesTrading.Core.Runtime
{
    public enum TradingSystemState
    {
        SystemOff,
        Initializing,
        DataValidation,
        Ready,
        TradingEnabled,
        PositionOpen,
        ManagingPosition,
        Flat,
        SessionComplete,
        NewsLock,
        RiskLock,
        DataStale,
        BrokerDisconnected,
        DailyLossLock,
        EmergencyStop
    }

    public enum SystemEventKind
    {
        Boot,
        InitializationComplete,
        DataValidated,
        EnableTrading,
        PositionOpened,
        BeginManaging,
        PositionClosed,
        SessionEnded,
        NewsBlocked,
        RiskViolation,
        MandatoryStateMissing,
        BrokerLost,
        DailyLossReached,
        EmergencyTriggered,
        UnknownBrokerPosition,
        ResetToOff
    }

    public sealed class SystemEvent
    {
        private SystemEvent(SystemEventKind kind, DateTime occurredAtUtc)
        {
            if (occurredAtUtc.Kind != DateTimeKind.Utc)
                throw new ArgumentException("System event timestamps must be UTC.", nameof(occurredAtUtc));

            Kind = kind;
            OccurredAtUtc = occurredAtUtc;
        }

        public SystemEventKind Kind { get; }
        public DateTime OccurredAtUtc { get; }

        public static SystemEvent Boot(DateTime utc) => new SystemEvent(SystemEventKind.Boot, utc);
        public static SystemEvent InitializationComplete(DateTime utc) => new SystemEvent(SystemEventKind.InitializationComplete, utc);
        public static SystemEvent DataValidated(DateTime utc) => new SystemEvent(SystemEventKind.DataValidated, utc);
        public static SystemEvent EnableTrading(DateTime utc) => new SystemEvent(SystemEventKind.EnableTrading, utc);
        public static SystemEvent PositionOpened(DateTime utc) => new SystemEvent(SystemEventKind.PositionOpened, utc);
        public static SystemEvent BeginManaging(DateTime utc) => new SystemEvent(SystemEventKind.BeginManaging, utc);
        public static SystemEvent PositionClosed(DateTime utc) => new SystemEvent(SystemEventKind.PositionClosed, utc);
        public static SystemEvent SessionEnded(DateTime utc) => new SystemEvent(SystemEventKind.SessionEnded, utc);
        public static SystemEvent NewsBlocked(DateTime utc) => new SystemEvent(SystemEventKind.NewsBlocked, utc);
        public static SystemEvent RiskViolation(DateTime utc) => new SystemEvent(SystemEventKind.RiskViolation, utc);
        public static SystemEvent MandatoryStateMissing(DateTime utc) => new SystemEvent(SystemEventKind.MandatoryStateMissing, utc);
        public static SystemEvent BrokerLost(DateTime utc) => new SystemEvent(SystemEventKind.BrokerLost, utc);
        public static SystemEvent DailyLossReached(DateTime utc) => new SystemEvent(SystemEventKind.DailyLossReached, utc);
        public static SystemEvent EmergencyTriggered(DateTime utc) => new SystemEvent(SystemEventKind.EmergencyTriggered, utc);
        public static SystemEvent UnknownBrokerPosition(DateTime utc) => new SystemEvent(SystemEventKind.UnknownBrokerPosition, utc);
        public static SystemEvent ResetToOff(DateTime utc) => new SystemEvent(SystemEventKind.ResetToOff, utc);
    }

    public sealed class TransitionResult
    {
        private TransitionResult(bool accepted, TradingSystemState state, string reasonCode)
        {
            Accepted = accepted;
            State = state;
            ReasonCode = reasonCode;
        }

        public bool Accepted { get; }
        public TradingSystemState State { get; }
        public string ReasonCode { get; }

        public static TransitionResult AcceptedResult(TradingSystemState state) =>
            new TransitionResult(true, state, "ACCEPTED");

        public static TransitionResult Rejected(TradingSystemState state, string reasonCode) =>
            new TransitionResult(false, state, reasonCode);
    }

    public sealed class TradingStateMachine
    {
        private static readonly IReadOnlyDictionary<Tuple<TradingSystemState, SystemEventKind>, TradingSystemState> AllowedTransitions =
            new Dictionary<Tuple<TradingSystemState, SystemEventKind>, TradingSystemState>
            {
                [Tuple.Create(TradingSystemState.SystemOff, SystemEventKind.Boot)] = TradingSystemState.Initializing,
                [Tuple.Create(TradingSystemState.Initializing, SystemEventKind.InitializationComplete)] = TradingSystemState.DataValidation,
                [Tuple.Create(TradingSystemState.DataValidation, SystemEventKind.DataValidated)] = TradingSystemState.Ready,
                [Tuple.Create(TradingSystemState.Ready, SystemEventKind.EnableTrading)] = TradingSystemState.TradingEnabled,
                [Tuple.Create(TradingSystemState.TradingEnabled, SystemEventKind.PositionOpened)] = TradingSystemState.PositionOpen,
                [Tuple.Create(TradingSystemState.PositionOpen, SystemEventKind.BeginManaging)] = TradingSystemState.ManagingPosition,
                [Tuple.Create(TradingSystemState.ManagingPosition, SystemEventKind.PositionClosed)] = TradingSystemState.Flat,
                [Tuple.Create(TradingSystemState.Flat, SystemEventKind.EnableTrading)] = TradingSystemState.TradingEnabled,
                [Tuple.Create(TradingSystemState.Flat, SystemEventKind.SessionEnded)] = TradingSystemState.SessionComplete,
                [Tuple.Create(TradingSystemState.TradingEnabled, SystemEventKind.SessionEnded)] = TradingSystemState.SessionComplete,

                [Tuple.Create(TradingSystemState.TradingEnabled, SystemEventKind.NewsBlocked)] = TradingSystemState.NewsLock,
                [Tuple.Create(TradingSystemState.TradingEnabled, SystemEventKind.RiskViolation)] = TradingSystemState.RiskLock,
                [Tuple.Create(TradingSystemState.TradingEnabled, SystemEventKind.MandatoryStateMissing)] = TradingSystemState.DataStale,
                [Tuple.Create(TradingSystemState.TradingEnabled, SystemEventKind.BrokerLost)] = TradingSystemState.BrokerDisconnected,
                [Tuple.Create(TradingSystemState.TradingEnabled, SystemEventKind.DailyLossReached)] = TradingSystemState.DailyLossLock,
                [Tuple.Create(TradingSystemState.TradingEnabled, SystemEventKind.EmergencyTriggered)] = TradingSystemState.EmergencyStop,
                [Tuple.Create(TradingSystemState.TradingEnabled, SystemEventKind.UnknownBrokerPosition)] = TradingSystemState.RiskLock,

                [Tuple.Create(TradingSystemState.PositionOpen, SystemEventKind.EmergencyTriggered)] = TradingSystemState.EmergencyStop,
                [Tuple.Create(TradingSystemState.ManagingPosition, SystemEventKind.EmergencyTriggered)] = TradingSystemState.EmergencyStop,

                [Tuple.Create(TradingSystemState.NewsLock, SystemEventKind.ResetToOff)] = TradingSystemState.SystemOff,
                [Tuple.Create(TradingSystemState.RiskLock, SystemEventKind.ResetToOff)] = TradingSystemState.SystemOff,
                [Tuple.Create(TradingSystemState.DataStale, SystemEventKind.ResetToOff)] = TradingSystemState.SystemOff,
                [Tuple.Create(TradingSystemState.BrokerDisconnected, SystemEventKind.ResetToOff)] = TradingSystemState.SystemOff,
                [Tuple.Create(TradingSystemState.DailyLossLock, SystemEventKind.ResetToOff)] = TradingSystemState.SystemOff,
                [Tuple.Create(TradingSystemState.EmergencyStop, SystemEventKind.ResetToOff)] = TradingSystemState.SystemOff,
                [Tuple.Create(TradingSystemState.SessionComplete, SystemEventKind.ResetToOff)] = TradingSystemState.SystemOff
            };

        public TradingStateMachine() : this(TradingSystemState.SystemOff) { }

        private TradingStateMachine(TradingSystemState initialState)
        {
            CurrentState = initialState;
        }

        public TradingSystemState CurrentState { get; private set; }

        public bool CanSubmitNewOrder(DateTime utcNow)
        {
            if (utcNow.Kind != DateTimeKind.Utc)
                throw new ArgumentException("utcNow must be UTC.", nameof(utcNow));

            return CurrentState == TradingSystemState.TradingEnabled;
        }

        public TransitionResult Transition(SystemEvent evt)
        {
            if (evt == null) throw new ArgumentNullException(nameof(evt));

            var key = Tuple.Create(CurrentState, evt.Kind);
            TradingSystemState next;
            if (!AllowedTransitions.TryGetValue(key, out next))
                return TransitionResult.Rejected(CurrentState, "INVALID_TRANSITION");

            CurrentState = next;
            return TransitionResult.AcceptedResult(CurrentState);
        }

        public static TradingStateMachine Restore(TradingSystemState state)
        {
            return new TradingStateMachine(state);
        }

        public static TradingStateMachine Replay(IEnumerable<SystemEvent> events)
        {
            if (events == null) throw new ArgumentNullException(nameof(events));

            var machine = new TradingStateMachine();
            foreach (var evt in events)
            {
                var result = machine.Transition(evt);
                if (!result.Accepted)
                    throw new InvalidOperationException("Cannot replay invalid event sequence: " + result.ReasonCode);
            }

            return machine;
        }
    }
}
