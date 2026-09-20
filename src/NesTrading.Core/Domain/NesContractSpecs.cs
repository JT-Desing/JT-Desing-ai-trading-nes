using System;

namespace NesTrading.Core.Domain
{
    /// <summary>
    /// Exchange contract constants for CME E-nano S&P 500 futures (NES).
    /// V1 treats these values as invariant contract metadata.
    /// </summary>
    public static class NesContractSpecs
    {
        public const decimal UsdPerPoint = 0.50m;
        public const decimal TickSizePoints = 0.50m;
        public const decimal UsdPerTick = 0.25m;

        public static bool IsTickAligned(decimal price)
        {
            if (price <= 0m) return false;
            return price % TickSizePoints == 0m;
        }

        public static void RequireTickAligned(decimal price, string parameterName)
        {
            if (!IsTickAligned(price))
                throw new ArgumentException(
                    "NES executable prices must align to 0.50 index-point ticks.",
                    parameterName);
        }
    }
}
