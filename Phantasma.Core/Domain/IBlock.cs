using System.Numerics;
using Phantasma.Shared.Types;

namespace Phantasma.Core
{
    public interface IOracleEntry
    {
        string URL { get; }
        byte[] Content { get; }
    }

    public interface IBlock
    {
        Address ChainAddress { get; }
        BigInteger Height { get; }
        Timestamp Timestamp { get; }
        Hash PreviousHash { get; }
        uint Protocol { get; }
        Hash Hash { get; }
        Hash[] TransactionHashes { get; }
        IOracleEntry[] OracleData { get; }
    }
}
