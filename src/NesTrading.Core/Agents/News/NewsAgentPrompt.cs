using System;

namespace NesTrading.Core.Agents.News
{
    public static class NewsAgentPrompt
    {
        public const string Version = "news.prompt.v1";

        public static string Build(string sourceContext)
        {
            if (sourceContext == null) throw new ArgumentNullException(nameof(sourceContext));

            return
                "You are the NES News Risk Agent. NES is the E-nano S&P 500 futures contract. " +
                "Classify only risk context relevant to U.S. equity-index intraday trading. " +
                "Allowed state values are ALLOW, CAUTION, or BLOCK. " +
                "Do not output BUY or SELL. Do not choose position size. " +
                "Do not choose entry, stop, target, leverage, or contracts. " +
                "Scheduled high-impact macro events and material systemic shocks may justify CAUTION or BLOCK. " +
                "Return only the required JSON schema.\n\nSOURCE CONTEXT:\n" +
                sourceContext;
        }
    }
}
