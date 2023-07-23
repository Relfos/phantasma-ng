using System.Numerics;
using Phantasma.Core.Types;
using Phantasma.Core.Types.Structs;

namespace Phantasma.Core.Domain.Contract.Stake;

public struct EnergyClaim
{
    public BigInteger stakeAmount;
    public Timestamp claimDate;
    public bool isNew;
}
