namespace NesTrading.Core.Domain
{
    public enum NewsGate { Allow, Caution, Block }
    public enum MarketRegime { TrendUp, TrendDown, Range, HighVolatility, InvalidData }
    public enum TradeDirection { Long, Short, None }
    public enum TradeAction { Long, Short, Wait, ExitRecommendation }
    public enum VolatilityState { Low, Normal, High }
    public enum RiskDecisionType { Approved, Rejected }
}
