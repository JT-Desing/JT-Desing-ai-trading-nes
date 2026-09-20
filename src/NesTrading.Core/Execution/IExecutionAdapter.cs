using System;
using NesTrading.Core.Domain;

namespace NesTrading.Core.Execution
{
    public interface IExecutionAdapter
    {
        ExecutionResult SubmitBracket(RiskDecision approvedDecision);
        BrokerSnapshot GetSnapshot();
        ReconciliationResult Reconcile(string signalId, BrokerOrderPresence presence);
        CloseResult CloseAtTarget(string tradeId, DateTime closedAtUtc);
    }
}
