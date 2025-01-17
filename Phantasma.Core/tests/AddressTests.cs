using Phantasma.Core;
using Shouldly;
using System.Text.Json.Nodes;
using System.Collections.Generic;
using Xunit;
using static Phantasma.Core.WalletLink;

namespace Phantasma.Core.Tests
{
    public class AddressTests
    {
        [Fact]
        public void null_address_test()
        {
            var address = Address.Null;
            address.ToByteArray().Length.ShouldBe(Address.LengthInBytes);
            address.ToByteArray().ShouldBe(new byte[Address.LengthInBytes]);
        }
    }
}
