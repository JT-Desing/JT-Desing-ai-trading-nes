namespace NesTrading.Core.Agents
{
    public sealed class AgentRefreshResult<T>
    {
        private AgentRefreshResult(
            bool success,
            bool published,
            string reasonCode,
            AgentEnvelope<T> state,
            ProviderCompletion completion,
            object usageRecord)
        {
            Success = success;
            Published = published;
            ReasonCode = reasonCode;
            State = state;
            Completion = completion;
            UsageRecord = usageRecord;
        }

        public bool Success { get; }
        public bool Published { get; }
        public string ReasonCode { get; }
        public AgentEnvelope<T> State { get; }
        public ProviderCompletion Completion { get; }
        public object UsageRecord { get; }

        public static AgentRefreshResult<T> PublishedState(
            AgentEnvelope<T> state,
            ProviderCompletion completion,
            object usageRecord) =>
            new AgentRefreshResult<T>(true, true, "PUBLISHED", state, completion, usageRecord);

        public static AgentRefreshResult<T> ValidatedState(
            AgentEnvelope<T> state,
            ProviderCompletion completion,
            object usageRecord) =>
            new AgentRefreshResult<T>(true, false, "VALIDATED", state, completion, usageRecord);

        public static AgentRefreshResult<T> Failure(
            string reasonCode,
            ProviderCompletion completion = null,
            object usageRecord = null) =>
            new AgentRefreshResult<T>(false, false, reasonCode, null, completion, usageRecord);

        public static AgentRefreshResult<T> Skipped(string reasonCode) =>
            new AgentRefreshResult<T>(false, false, reasonCode, null, null, null);
    }
}
