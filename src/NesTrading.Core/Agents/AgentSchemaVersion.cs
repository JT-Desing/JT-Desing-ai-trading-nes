namespace NesTrading.Core.Agents
{
    public static class AgentSchemaVersion
    {
        public const string NewsV1 = "news.v1";
        public const string TradeV1 = "trade.v1";
        public const string MarketV1 = "market.v1";
    }

    public enum ProviderCompletionStatus
    {
        Completed,
        Refused,
        Incomplete,
        TimedOut,
        Failed,
        Cancelled,
        BudgetExhausted
    }
}
