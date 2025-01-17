﻿using System;
using Serilog;

namespace Phantasma.Node.Oracles
{
    public static class Pricer
    {
        public static decimal GetCoinRate(string baseSymbol, string quoteSymbol, string cryptoCompApiKey, bool cgEnabled, PricerSupportedToken[] supportedTokens)
        {
            try
            {
                Decimal cGeckoPrice = 0;
                Decimal cComparePrice = 0;

                if (string.IsNullOrEmpty(cryptoCompApiKey))
                {
                    cComparePrice = CryptoCompareUtils.GetCoinRate(baseSymbol, quoteSymbol, cryptoCompApiKey, supportedTokens);
                }

                if (cgEnabled)
                {
                    cGeckoPrice = CoinGeckoUtils.GetCoinRate(baseSymbol, quoteSymbol, supportedTokens);
                }

                if ((cGeckoPrice > 0) && (cComparePrice > 0))
                {
                    return ((cGeckoPrice + cComparePrice) / 2);
                }
                if((cGeckoPrice > 0) && (cComparePrice <= 0))
                {
                    return (cGeckoPrice);
                }
                if ((cComparePrice > 0) && (cGeckoPrice <= 0))
                {
                    return (cComparePrice);
                }
                return 0;

            }
            catch (Exception ex)
            {
                var errorMsg = ex.ToString();
                Log.Error($"Pricer error: {errorMsg}");
                return 0;
            }
        }
    }
}
