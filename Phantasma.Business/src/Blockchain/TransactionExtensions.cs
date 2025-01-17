﻿using Phantasma.Core;

namespace Phantasma.Business
{
    public static class TransactionExtensions
    {
        public static bool IsValid(this Transaction tx, IChain chain)
        {
            return (chain.Name == tx.ChainName && chain.Nexus.Name == tx.NexusName);
        }
    }
}
