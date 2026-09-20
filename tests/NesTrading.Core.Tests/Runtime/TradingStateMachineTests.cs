using System;
using NUnit.Framework;
using NesTrading.Core.Runtime;

namespace NesTrading.Core.Tests.Runtime
{
    [TestFixture]
    public class TradingStateMachineTests
    {
        private static readonly DateTime Now = new DateTime(2026, 9, 20, 14, 0, 0, DateTimeKind.Utc);

        [Test]
        public void HappyPath_TransitionsThroughReadyTradingAndFlat()
        {
            var sm = new TradingStateMachine();
            Assert.That(sm.CurrentState, Is.EqualTo(TradingSystemState.SystemOff));

            Assert.That(sm.Transition(SystemEvent.Boot(Now)).Accepted, Is.True);
            Assert.That(sm.CurrentState, Is.EqualTo(TradingSystemState.Initializing));

            Assert.That(sm.Transition(SystemEvent.InitializationComplete(Now)).Accepted, Is.True);
            Assert.That(sm.CurrentState, Is.EqualTo(TradingSystemState.DataValidation));

            Assert.That(sm.Transition(SystemEvent.DataValidated(Now)).Accepted, Is.True);
            Assert.That(sm.CurrentState, Is.EqualTo(TradingSystemState.Ready));

            Assert.That(sm.Transition(SystemEvent.EnableTrading(Now)).Accepted, Is.True);
            Assert.That(sm.CurrentState, Is.EqualTo(TradingSystemState.TradingEnabled));
            Assert.That(sm.CanSubmitNewOrder(Now), Is.True);

            Assert.That(sm.Transition(SystemEvent.PositionOpened(Now)).Accepted, Is.True);
            Assert.That(sm.CurrentState, Is.EqualTo(TradingSystemState.PositionOpen));
            Assert.That(sm.CanSubmitNewOrder(Now), Is.False);

            Assert.That(sm.Transition(SystemEvent.BeginManaging(Now)).Accepted, Is.True);
            Assert.That(sm.CurrentState, Is.EqualTo(TradingSystemState.ManagingPosition));

            Assert.That(sm.Transition(SystemEvent.PositionClosed(Now)).Accepted, Is.True);
            Assert.That(sm.CurrentState, Is.EqualTo(TradingSystemState.Flat));
        }

        [TestCase(TradingSystemState.NewsLock)]
        [TestCase(TradingSystemState.RiskLock)]
        [TestCase(TradingSystemState.DataStale)]
        [TestCase(TradingSystemState.BrokerDisconnected)]
        [TestCase(TradingSystemState.DailyLossLock)]
        [TestCase(TradingSystemState.EmergencyStop)]
        public void ProtectiveLockStates_NeverAllowNewOrders(TradingSystemState lockState)
        {
            var sm = TradingStateMachine.Restore(lockState);
            Assert.That(sm.CanSubmitNewOrder(Now), Is.False);
        }

        [Test]
        public void EmergencyStop_CannotJumpDirectlyBackToTradingEnabled()
        {
            var sm = TradingStateMachine.Restore(TradingSystemState.EmergencyStop);
            var result = sm.Transition(SystemEvent.EnableTrading(Now));

            Assert.That(result.Accepted, Is.False);
            Assert.That(result.ReasonCode, Is.EqualTo("INVALID_TRANSITION"));
            Assert.That(sm.CurrentState, Is.EqualTo(TradingSystemState.EmergencyStop));
        }

        [Test]
        public void UnknownBrokerPosition_ForcesRiskLock()
        {
            var sm = TradingStateMachine.Restore(TradingSystemState.TradingEnabled);
            var result = sm.Transition(SystemEvent.UnknownBrokerPosition(Now));

            Assert.That(result.Accepted, Is.True);
            Assert.That(sm.CurrentState, Is.EqualTo(TradingSystemState.RiskLock));
            Assert.That(sm.CanSubmitNewOrder(Now), Is.False);
        }

        [Test]
        public void MissingMandatoryState_ForcesDataStaleLock()
        {
            var sm = TradingStateMachine.Restore(TradingSystemState.TradingEnabled);
            var result = sm.Transition(SystemEvent.MandatoryStateMissing(Now));

            Assert.That(result.Accepted, Is.True);
            Assert.That(sm.CurrentState, Is.EqualTo(TradingSystemState.DataStale));
            Assert.That(sm.CanSubmitNewOrder(Now), Is.False);
        }

        [Test]
        public void InvalidTransition_IsRejectedWithoutMutatingCurrentState()
        {
            var sm = new TradingStateMachine();
            var result = sm.Transition(SystemEvent.PositionOpened(Now));

            Assert.That(result.Accepted, Is.False);
            Assert.That(result.ReasonCode, Is.EqualTo("INVALID_TRANSITION"));
            Assert.That(sm.CurrentState, Is.EqualTo(TradingSystemState.SystemOff));
        }

        [Test]
        public void Replay_ReconstructsSameStateAfterRestart()
        {
            var events = new[]
            {
                SystemEvent.Boot(Now),
                SystemEvent.InitializationComplete(Now.AddSeconds(1)),
                SystemEvent.DataValidated(Now.AddSeconds(2)),
                SystemEvent.EnableTrading(Now.AddSeconds(3)),
                SystemEvent.PositionOpened(Now.AddSeconds(4)),
                SystemEvent.BeginManaging(Now.AddSeconds(5)),
                SystemEvent.PositionClosed(Now.AddSeconds(6))
            };

            var original = new TradingStateMachine();
            foreach (var evt in events)
                Assert.That(original.Transition(evt).Accepted, Is.True);

            var restored = TradingStateMachine.Replay(events);

            Assert.That(restored.CurrentState, Is.EqualTo(original.CurrentState));
            Assert.That(restored.CurrentState, Is.EqualTo(TradingSystemState.Flat));
        }

        [Test]
        public void EventTimestamp_MustBeUtc()
        {
            var local = DateTime.SpecifyKind(Now, DateTimeKind.Local);
            Assert.Throws<ArgumentException>(() => SystemEvent.Boot(local));
        }
    }
}
