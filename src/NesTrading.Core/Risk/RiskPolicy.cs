using System;

namespace NesTrading.Core.Risk
{
    public sealed class RiskPolicy
    {
        public RiskPolicy(
            decimal targetRiskPercent,
            decimal hardMaxRiskPercent,
            decimal dailySoftLossPercent,
            decimal dailyHardLossPercent,
            int maxTradesPerDay,
            int maxLosingTradesPerDay,
            TimeSpan cooldown,
            int maxContracts,
            decimal maxSpreadTicks,
            decimal maxExpectedSlippageUsd,
            decimal minNetRewardRisk,
            TimeSpan sessionStartEastern,
            TimeSpan sessionEndEastern)
        {
            if (targetRiskPercent <= 0m) throw new ArgumentOutOfRangeException(nameof(targetRiskPercent));
            if (hardMaxRiskPercent < targetRiskPercent) throw new ArgumentOutOfRangeException(nameof(hardMaxRiskPercent));
            if (dailySoftLossPercent <= 0m) throw new ArgumentOutOfRangeException(nameof(dailySoftLossPercent));
            if (dailyHardLossPercent < dailySoftLossPercent) throw new ArgumentOutOfRangeException(nameof(dailyHardLossPercent));
            if (maxTradesPerDay <= 0) throw new ArgumentOutOfRangeException(nameof(maxTradesPerDay));
            if (maxLosingTradesPerDay <= 0) throw new ArgumentOutOfRangeException(nameof(maxLosingTradesPerDay));
            if (cooldown < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(cooldown));
            if (maxContracts != 1) throw new ArgumentOutOfRangeException(nameof(maxContracts), "V1 requires one contract maximum.");
            if (maxSpreadTicks < 0m) throw new ArgumentOutOfRangeException(nameof(maxSpreadTicks));
            if (maxExpectedSlippageUsd < 0m) throw new ArgumentOutOfRangeException(nameof(maxExpectedSlippageUsd));
            if (minNetRewardRisk <= 0m) throw new ArgumentOutOfRangeException(nameof(minNetRewardRisk));
            if (sessionStartEastern < TimeSpan.Zero || sessionStartEastern >= TimeSpan.FromDays(1)) throw new ArgumentOutOfRangeException(nameof(sessionStartEastern));
            if (sessionEndEastern <= sessionStartEastern || sessionEndEastern >= TimeSpan.FromDays(1)) throw new ArgumentOutOfRangeException(nameof(sessionEndEastern));

            TargetRiskPercent = targetRiskPercent;
            HardMaxRiskPercent = hardMaxRiskPercent;
            DailySoftLossPercent = dailySoftLossPercent;
            DailyHardLossPercent = dailyHardLossPercent;
            MaxTradesPerDay = maxTradesPerDay;
            MaxLosingTradesPerDay = maxLosingTradesPerDay;
            Cooldown = cooldown;
            MaxContracts = maxContracts;
            MaxSpreadTicks = maxSpreadTicks;
            MaxExpectedSlippageUsd = maxExpectedSlippageUsd;
            MinNetRewardRisk = minNetRewardRisk;
            SessionStartEastern = sessionStartEastern;
            SessionEndEastern = sessionEndEastern;
        }

        public decimal TargetRiskPercent { get; }
        public decimal HardMaxRiskPercent { get; }
        public decimal DailySoftLossPercent { get; }
        public decimal DailyHardLossPercent { get; }
        public int MaxTradesPerDay { get; }
        public int MaxLosingTradesPerDay { get; }
        public TimeSpan Cooldown { get; }
        public int MaxContracts { get; }
        public decimal MaxSpreadTicks { get; }
        public decimal MaxExpectedSlippageUsd { get; }
        public decimal MinNetRewardRisk { get; }
        public TimeSpan SessionStartEastern { get; }
        public TimeSpan SessionEndEastern { get; }

        public static RiskPolicy V1Defaults()
        {
            return new RiskPolicy(
                targetRiskPercent: 0.01m,
                hardMaxRiskPercent: 0.02m,
                dailySoftLossPercent: 0.02m,
                dailyHardLossPercent: 0.03m,
                maxTradesPerDay: 2,
                maxLosingTradesPerDay: 2,
                cooldown: TimeSpan.FromMinutes(10),
                maxContracts: 1,
                maxSpreadTicks: 4m,
                maxExpectedSlippageUsd: 1m,
                minNetRewardRisk: 1.25m,
                sessionStartEastern: new TimeSpan(9, 40, 0),
                sessionEndEastern: new TimeSpan(11, 30, 0));
        }
    }
}
