using System;
using NesTrading.Core.Risk;
using NesTrading.Core.Runtime;
using NesTrading.Core.Strategy;

namespace NesTrading.Core.Agents
{
    public sealed class AgentCycleInput
    {
        public AgentCycleInput(
            MarketSnapshot marketSnapshot,
            string newsSourceContext,
            AccountRiskState account,
            TradingSessionState session,
            bool systemAllowsTrading,
            DateTime utcNow)
        {
            MarketSnapshot = marketSnapshot ?? throw new ArgumentNullException(nameof(marketSnapshot));
            NewsSourceContext = newsSourceContext ?? throw new ArgumentNullException(nameof(newsSourceContext));
            Account = account ?? throw new ArgumentNullException(nameof(account));
            Session = session ?? throw new ArgumentNullException(nameof(session));
            if (utcNow.Kind != DateTimeKind.Utc) throw new ArgumentException("utcNow must be UTC.", nameof(utcNow));

            SystemAllowsTrading = systemAllowsTrading;
            UtcNow = utcNow;
        }

        public MarketSnapshot MarketSnapshot { get; }
        public string NewsSourceContext { get; }
        public AccountRiskState Account { get; }
        public TradingSessionState Session { get; }
        public bool SystemAllowsTrading { get; }
        public DateTime UtcNow { get; }
    }
}
