using System;

namespace NesTrading.Core.Risk
{
    public sealed class CostEstimate
    {
        public CostEstimate(decimal roundTripFeesUsd, decimal expectedSlippageUsd, decimal spreadTicks)
        {
            if (roundTripFeesUsd < 0m) throw new ArgumentOutOfRangeException(nameof(roundTripFeesUsd));
            if (expectedSlippageUsd < 0m) throw new ArgumentOutOfRangeException(nameof(expectedSlippageUsd));
            if (spreadTicks < 0m) throw new ArgumentOutOfRangeException(nameof(spreadTicks));

            RoundTripFeesUsd = roundTripFeesUsd;
            ExpectedSlippageUsd = expectedSlippageUsd;
            SpreadTicks = spreadTicks;
        }

        public decimal RoundTripFeesUsd { get; }
        public decimal ExpectedSlippageUsd { get; }
        public decimal SpreadTicks { get; }
        public decimal TotalFrictionUsd => RoundTripFeesUsd + ExpectedSlippageUsd;
    }

    public sealed class AccountRiskState
    {
        public AccountRiskState(decimal equityUsd)
        {
            EquityUsd = equityUsd;
        }

        public decimal EquityUsd { get; }
    }
}
