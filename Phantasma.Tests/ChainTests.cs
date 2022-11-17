using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Linq;
using System.Numerics;
using System.Collections.Generic;

using Phantasma.Simulator;
using Phantasma.Core.Types;
using Phantasma.Core.Cryptography;
using Phantasma.Core.Domain;
using Phantasma.Business.Blockchain;
using Phantasma.Core.Numerics;
using Phantasma.Business.VM.Utils;
using Phantasma.Business.CodeGen.Assembler;
using Phantasma.Core.Storage.Context;
using Phantasma.Business.Blockchain.Contracts;
using Phantasma.Business.Blockchain.Tokens;
using Phantasma.Business.VM;

namespace Phantasma.LegacyTests
{
    [TestClass]
    public class ChainTests
    {
        [TestMethod]
        public void NullAddress()
        {
            var addr = Address.Null;
            Assert.IsTrue(addr.IsNull);
            Assert.IsTrue(addr.IsSystem);
            Assert.IsFalse(addr.IsUser);
            Assert.IsFalse(addr.IsInterop);

            Assert.IsTrue(Address.IsValidAddress(addr.Text));
        }

        [TestMethod]
        public void Decimals()
        {
            var places = 8;
            decimal d = 93000000;
            BigInteger n = 9300000000000000;

            var tmp1 = UnitConversion.ToBigInteger(UnitConversion.ToDecimal(n, places), places);

            Assert.IsTrue(n == tmp1);
            Assert.IsTrue(d == UnitConversion.ToDecimal(UnitConversion.ToBigInteger(d, places), places));

            Assert.IsTrue(d == UnitConversion.ToDecimal(n, places));
            Assert.IsTrue(n == UnitConversion.ToBigInteger(d, places));

            var tmp2 = UnitConversion.ToBigInteger(0.1m, DomainSettings.FuelTokenDecimals);
            Assert.IsTrue(tmp2 > 0);

            decimal eos = 1006245120;
            var tmp3 = UnitConversion.ToBigInteger(eos, 18);
            var dec = UnitConversion.ToDecimal(tmp3, 18);
            Assert.IsTrue(dec == eos);

            BigInteger small = 60;
            var tmp4 = UnitConversion.ToDecimal(small, 10);
            var dec2 = 0.000000006m;
            Assert.IsTrue(dec2 == tmp4);
        }

        [TestMethod]
        public void GenesisBlock()
        {
            var owner = PhantasmaKeys.Generate();

            var simulator = new NexusSimulator(owner);
            var nexus = simulator.Nexus;

            Assert.IsTrue(nexus.HasGenesis());

            var genesisHash = nexus.GetGenesisHash(nexus.RootStorage);
            Assert.IsTrue(genesisHash != Hash.Null);

            var rootChain = nexus.RootChain;

            Assert.IsTrue(rootChain.Address.IsSystem);
            Assert.IsFalse(rootChain.Address.IsNull);

            var symbol = DomainSettings.FuelTokenSymbol;
            Assert.IsTrue(nexus.TokenExists(nexus.RootStorage, symbol));
            var token = nexus.GetTokenInfo(nexus.RootStorage, symbol);
            Assert.IsTrue(token.MaxSupply == 0);

            var supply = nexus.RootChain.GetTokenSupply(rootChain.Storage, symbol);
            Assert.IsTrue(supply > 0);

            var balance = UnitConversion.ToDecimal(nexus.RootChain.GetTokenBalance(rootChain.Storage, token, owner.Address), DomainSettings.FuelTokenDecimals);
            Assert.IsTrue(balance > 0);

            Assert.IsTrue(rootChain != null);
            Assert.IsTrue(rootChain.Height > 0);

            /*var children = nexus.GetChildChainsByName(nexus.RootStorage, rootChain.Name);
            Assert.IsTrue(children.Any());*/

            Assert.IsTrue(nexus.IsPrimaryValidator(owner.Address));

            var randomKey = PhantasmaKeys.Generate();
            Assert.IsFalse(nexus.IsPrimaryValidator(randomKey.Address));

            /*var txCount = nexus.GetTotalTransactionCount();
            Assert.IsTrue(txCount > 0);*/

            simulator.TransferOwnerAssetsToAddress(randomKey.Address);
        }

        

        [TestMethod]
        public void CreateToken()
        {
            var owner = PhantasmaKeys.Generate();
            var simulator = new NexusSimulator(owner);
            var nexus = simulator.Nexus;

            var accountChain = nexus.GetChainByName("account");
            var symbol = "BLA";

            var tokenAsm = new string[]
            {
                "LOAD r1 42",
                "PUSH r1",
                "RET"
            };

            var tokenScript = AssemblerUtils.BuildScript(tokenAsm);

            var methods = new ContractMethod[]
            {
                new ContractMethod("mycall", VMType.Number, 0, new ContractParameter[0])
            };

            var tokenSupply = UnitConversion.ToBigInteger(10000, 18);
            simulator.BeginBlock();
            simulator.GenerateToken(owner, symbol, "BlaToken", tokenSupply, 18, TokenFlags.Transferable | TokenFlags.Fungible | TokenFlags.Finite | TokenFlags.Divisible, tokenScript, null, methods);
            simulator.MintTokens(owner, owner.Address, symbol, tokenSupply);
            simulator.EndBlock();

            var token = nexus.GetTokenInfo(nexus.RootStorage, symbol);

            var testUser = PhantasmaKeys.Generate();

            var amount = UnitConversion.ToBigInteger(2, token.Decimals);

            var oldBalance = nexus.RootChain.GetTokenBalance(simulator.Nexus.RootStorage, token, owner.Address);

            Assert.IsTrue(oldBalance > amount);

            // Send from Genesis address to test user
            simulator.BeginBlock();
            var tx = simulator.GenerateTransfer(owner, testUser.Address, nexus.RootChain as Chain, symbol, amount);
            simulator.EndBlock();

            // verify test user balance
            var transferBalance = nexus.RootChain.GetTokenBalance(simulator.Nexus.RootStorage, token, testUser.Address);
            Assert.IsTrue(transferBalance == amount);

            var newBalance = nexus.RootChain.GetTokenBalance(simulator.Nexus.RootStorage, token, owner.Address);

            Assert.IsTrue(transferBalance + newBalance == oldBalance);

            Assert.IsTrue(nexus.RootChain.IsContractDeployed(nexus.RootChain.Storage, symbol));

            // try call token contract method
            simulator.BeginBlock();
            tx = simulator.GenerateCustomTransaction(owner, ProofOfWork.None, () =>
            {
                return new ScriptBuilder()
                .AllowGas(owner.Address, Address.Null, simulator.MinimumFee, 999)
                .CallContract(symbol, "mycall")
                .SpendGas(owner.Address)
                .EndScript();
            });
            var block = simulator.EndBlock().First();

            var callResultBytes = block.GetResultForTransaction(tx.Hash);
            var callResult = Serialization.Unserialize<VMObject>(callResultBytes);
            var num = callResult.AsNumber();

            Assert.IsTrue(num == 42);
        }

        [TestMethod]
        public void CreateNonDivisibleToken()
        {
            var owner = PhantasmaKeys.Generate();

            var simulator = new NexusSimulator(owner);
            var nexus = simulator.Nexus;

            var symbol = "BLA";

            var tokenSupply = UnitConversion.ToBigInteger(100000000, 18);
            simulator.BeginBlock();
            simulator.GenerateToken(owner, symbol, "BlaToken", tokenSupply, 0, TokenFlags.Transferable | TokenFlags.Fungible | TokenFlags.Finite);
            simulator.MintTokens(owner, owner.Address, symbol, tokenSupply);
            simulator.EndBlock();

            var token = nexus.GetTokenInfo(nexus.RootStorage, symbol);

            var testUser = PhantasmaKeys.Generate();

            var amount = UnitConversion.ToBigInteger(2, token.Decimals);

            var oldBalance = nexus.RootChain.GetTokenBalance(simulator.Nexus.RootStorage, token, owner.Address);

            Assert.IsTrue(oldBalance > amount);

            // Send from Genesis address to test user
            simulator.BeginBlock();
            var tx = simulator.GenerateTransfer(owner, testUser.Address, nexus.RootChain as Chain, symbol, amount);
            simulator.EndBlock();

            // verify test user balance
            var transferBalance = nexus.RootChain.GetTokenBalance(simulator.Nexus.RootStorage, token, testUser.Address);
            Assert.IsTrue(transferBalance == amount);

            var newBalance = nexus.RootChain.GetTokenBalance(simulator.Nexus.RootStorage, token, owner.Address);

            Assert.IsTrue(transferBalance + newBalance == oldBalance);
        }
        
        [TestMethod]
        public void SimpleTransfer()
        {
            var owner = PhantasmaKeys.Generate();

            var simulator = new NexusSimulator(owner);
            var nexus = simulator.Nexus;

            var testUserA = PhantasmaKeys.Generate();
            var testUserB = PhantasmaKeys.Generate();

            var fuelAmount = UnitConversion.ToBigInteger(10, DomainSettings.FuelTokenDecimals);
            var transferAmount = UnitConversion.ToBigInteger(10, DomainSettings.StakingTokenDecimals);

            simulator.BeginBlock();
            var txA = simulator.GenerateTransfer(owner, testUserA.Address, nexus.RootChain, DomainSettings.FuelTokenSymbol, fuelAmount);
            var txB = simulator.GenerateTransfer(owner, testUserA.Address, nexus.RootChain, DomainSettings.StakingTokenSymbol, transferAmount);
            simulator.EndBlock();

            // Send from user A to user B
            simulator.BeginBlock();
            var txC = simulator.GenerateTransfer(testUserA, testUserB.Address, nexus.RootChain, DomainSettings.StakingTokenSymbol, transferAmount);
            simulator.EndBlock();

            var hashes = simulator.Nexus.RootChain.GetTransactionHashesForAddress(testUserA.Address);
            Assert.IsTrue(hashes.Length == 3);
            Assert.IsTrue(hashes.Any(x => x == txA.Hash));
            Assert.IsTrue(hashes.Any(x => x == txB.Hash));
            Assert.IsTrue(hashes.Any(x => x == txC.Hash));

            var stakeToken = simulator.Nexus.GetTokenInfo(simulator.Nexus.RootStorage, DomainSettings.StakingTokenSymbol);
            var finalBalance = simulator.Nexus.RootChain.GetTokenBalance(simulator.Nexus.RootStorage, stakeToken, testUserB.Address);
            Assert.IsTrue(finalBalance == transferAmount);
        }

        [TestMethod]
        public void GenesisMigration()
        {
            var firstOwner = PhantasmaKeys.Generate();

            var simulator = new NexusSimulator(firstOwner);
            var nexus = simulator.Nexus;

            var secondOwner = PhantasmaKeys.Generate();
            var testUser = PhantasmaKeys.Generate();
            var anotherTestUser = PhantasmaKeys.Generate();

            var fuelAmount = UnitConversion.ToBigInteger(10, DomainSettings.FuelTokenDecimals);
            var transferAmount = UnitConversion.ToBigInteger(10, DomainSettings.StakingTokenDecimals);

            simulator.BeginBlock();
            simulator.GenerateTransfer(firstOwner, secondOwner.Address, nexus.RootChain, DomainSettings.StakingTokenSymbol, transferAmount);
            simulator.GenerateTransfer(firstOwner, testUser.Address, nexus.RootChain, DomainSettings.FuelTokenSymbol, transferAmount);
            simulator.EndBlock();

            var oldToken = nexus.GetTokenInfo(nexus.RootStorage, DomainSettings.RewardTokenSymbol);
            Assert.IsTrue(oldToken.Owner == firstOwner.Address);

            simulator.BeginBlock();
            simulator.GenerateCustomTransaction(firstOwner, ProofOfWork.None, () =>
            {
                return ScriptUtils.BeginScript().
                     AllowGas(firstOwner.Address, Address.Null, simulator.MinimumFee, Transaction.DefaultGasLimit).
                     CallContract("account", "Migrate", firstOwner.Address, secondOwner.Address).
                     SpendGas(firstOwner.Address).
                     EndScript();
            });
            simulator.EndBlock();

            var newToken = nexus.GetTokenInfo(nexus.RootStorage, DomainSettings.RewardTokenSymbol);
            Assert.IsTrue(newToken.Owner == secondOwner.Address);

            simulator.BeginBlock(secondOwner); // here we change the validator keys in simulator
            simulator.GenerateTransfer(secondOwner, testUser.Address, nexus.RootChain, DomainSettings.StakingTokenSymbol, transferAmount);
            simulator.EndBlock();

            // inflation check
            simulator.TimeSkipDays(91);
            simulator.BeginBlock(); 
            simulator.GenerateTransfer(testUser, anotherTestUser.Address, nexus.RootChain, DomainSettings.StakingTokenSymbol, transferAmount);
            simulator.EndBlock();

            var crownBalance = nexus.RootChain.GetTokenBalance(nexus.RootStorage, DomainSettings.RewardTokenSymbol, firstOwner.Address);
            Assert.IsTrue(crownBalance == 0);

            crownBalance = nexus.RootChain.GetTokenBalance(nexus.RootStorage, DomainSettings.RewardTokenSymbol, secondOwner.Address);
            Assert.IsTrue(crownBalance == 1);

            var thirdOwner = PhantasmaKeys.Generate();

            simulator.BeginBlock();
            simulator.GenerateCustomTransaction(secondOwner, ProofOfWork.None, () =>
            {
                return ScriptUtils.BeginScript().
                     AllowGas(secondOwner.Address, Address.Null, simulator.MinimumFee, Transaction.DefaultGasLimit).
                     CallContract("account", "Migrate", secondOwner.Address, thirdOwner.Address).
                     SpendGas(secondOwner.Address).
                     EndScript();
            });
            simulator.EndBlock();

            simulator.BeginBlock(thirdOwner); // here we change the validator keys in simulator
            simulator.GenerateTransfer(thirdOwner, testUser.Address, nexus.RootChain, DomainSettings.StakingTokenSymbol, transferAmount);
            simulator.EndBlock();
        }

        [TestMethod]
        public void SystemAddressTransfer()
        {
            var owner = PhantasmaKeys.Generate();

            var simulator = new NexusSimulator(owner);
            var nexus = simulator.Nexus;

            var testUser = PhantasmaKeys.Generate();

            var fuelAmount = UnitConversion.ToBigInteger(10, DomainSettings.FuelTokenDecimals);
            var transferAmount = UnitConversion.ToBigInteger(10, DomainSettings.StakingTokenDecimals);

            simulator.BeginBlock();
            var txA = simulator.GenerateTransfer(owner, testUser.Address, nexus.RootChain, DomainSettings.FuelTokenSymbol, fuelAmount);
            var txB = simulator.GenerateTransfer(owner, testUser.Address, nexus.RootChain, DomainSettings.StakingTokenSymbol, transferAmount);
            simulator.EndBlock();

            var hashes = simulator.Nexus.RootChain.GetTransactionHashesForAddress(testUser.Address);
            Assert.IsTrue(hashes.Length == 2);
            Assert.IsTrue(hashes.Any(x => x == txA.Hash));
            Assert.IsTrue(hashes.Any(x => x == txB.Hash));

            var stakeToken = simulator.Nexus.GetTokenInfo(simulator.Nexus.RootStorage, DomainSettings.StakingTokenSymbol);
            var finalBalance = simulator.Nexus.RootChain.GetTokenBalance(simulator.Nexus.RootStorage, stakeToken, testUser.Address);
            Assert.IsTrue(finalBalance == transferAmount);

            var fuelToken = simulator.Nexus.GetTokenInfo(simulator.Nexus.RootStorage, DomainSettings.StakingTokenSymbol);
            finalBalance = simulator.Nexus.RootChain.GetTokenBalance(simulator.Nexus.RootStorage, fuelToken, testUser.Address);
            Assert.IsTrue(finalBalance == transferAmount);
        }

        [TestMethod]
        public void CosmicSwap()
        {
            var owner = PhantasmaKeys.Generate();

            var simulator = new NexusSimulator(owner);
            var nexus = simulator.Nexus;

            var testUserA = PhantasmaKeys.Generate();

            var fuelAmount = UnitConversion.ToBigInteger(10, DomainSettings.FuelTokenDecimals);
            var transferAmount = UnitConversion.ToBigInteger(10, DomainSettings.StakingTokenDecimals);

            var symbol = "COOL";

            simulator.BeginBlock();
            simulator.GenerateToken(owner, symbol, "CoolToken", 1000000, 0, TokenFlags.Burnable | TokenFlags.Transferable | TokenFlags.Fungible | TokenFlags.Finite);
            simulator.MintTokens(owner, testUserA.Address, symbol, 100000);
            simulator.EndBlock();

            simulator.BeginBlock();
            simulator.GenerateTransfer(owner, testUserA.Address, nexus.RootChain, DomainSettings.StakingTokenSymbol, transferAmount);
            var blockA = simulator.EndBlock().FirstOrDefault();

            Assert.IsTrue(blockA != null);
            Assert.IsFalse(blockA.OracleData.Any());

            var fuelToken = simulator.Nexus.GetTokenInfo(simulator.Nexus.RootStorage, DomainSettings.FuelTokenSymbol);
            var originalBalance = simulator.Nexus.RootChain.GetTokenBalance(simulator.Nexus.RootStorage, fuelToken, testUserA.Address);

            var swapAmount = UnitConversion.ToBigInteger(0.01m, DomainSettings.StakingTokenDecimals);
            simulator.BeginBlock();
            simulator.GenerateSwap(testUserA, nexus.RootChain, DomainSettings.StakingTokenSymbol, DomainSettings.FuelTokenSymbol, swapAmount);
            var blockB = simulator.EndBlock().FirstOrDefault();

            Assert.IsTrue(blockB != null);
            Assert.IsTrue(blockB.OracleData.Any());

            var bytes = blockB.ToByteArray(true);
            var otherBlock = Block.Unserialize(bytes);
            Assert.IsTrue(otherBlock.Hash == blockB.Hash);

            var finalBalance = simulator.Nexus.RootChain.GetTokenBalance(simulator.Nexus.RootStorage, fuelToken, testUserA.Address);
            Assert.IsTrue(finalBalance > originalBalance);

            /*
            swapAmount = 10;
            simulator.BeginBlock();
            simulator.GenerateCustomTransaction(testUserA, ProofOfWork.None, () =>
            {
               return ScriptUtils.BeginScript().
                    AllowGas(testUserA.Address, Address.Null, simulator.MinimumFee, Transaction.DefaultGasLimit).
                    //CallContract("swap", "SwapFiat", testUserA.Address, symbol, DomainSettings.FuelTokenSymbol, UnitConversion.ToBigInteger(0.1m, DomainSettings.FiatTokenDecimals)).
                    CallContract("swap", "SwapTokens", testUserA.Address, symbol, DomainSettings.FuelTokenSymbol, new BigInteger(1)).
                    SpendGas(testUserA.Address).
                    EndScript();
            });
            simulator.EndBlock();*/
        }

        /*
        [TestMethod]
        public void ChainSwapIn()
        {
            var owner = PhantasmaKeys.Generate();

            var simulator = new NexusSimulator(owner);
            var nexus = simulator.Nexus;

            var neoKeys = NeoKeys.Generate();

            var limit = 800;

            // 1 - at this point a real NEO transaction would be done to the NEO address obtained from getPlatforms in the API
            // here we just use a random hardcoded hash and a fake oracle to simulate it
            var swapSymbol = "GAS";
            var neoTxHash = OracleSimulator.SimulateExternalTransaction("neo", Pay.Chains.NeoWallet.NeoID, neoKeys.PublicKey, neoKeys.Address, swapSymbol, 2);

            var tokenInfo = nexus.GetTokenInfo(nexus.RootStorage, swapSymbol);

            // 2 - transcode the neo address and settle the Neo transaction on Phantasma
            var transcodedAddress = Address.FromKey(neoKeys);

            var testUser = PhantasmaKeys.Generate();

            var platformName = Pay.Chains.NeoWallet.NeoPlatform;
            var platformChain = Pay.Chains.NeoWallet.NeoPlatform;

            var gasPrice = simulator.MinimumFee;

            Func<decimal, byte[]> genScript = (fee) =>
            {
                return new ScriptBuilder()
                .CallContract("interop", "SettleTransaction", transcodedAddress, platformName, platformChain, neoTxHash)
                .CallContract("swap", "SwapFee", transcodedAddress, swapSymbol, UnitConversion.ToBigInteger(fee, DomainSettings.FuelTokenDecimals))
                .TransferBalance(swapSymbol, transcodedAddress, testUser.Address)
                .AllowGas(transcodedAddress, Address.Null, gasPrice, limit)
                .TransferBalance(DomainSettings.FuelTokenSymbol, transcodedAddress, testUser.Address)
                .SpendGas(transcodedAddress).EndScript();
            };

            // note the 0.1m passed here could be anything else. It's just used to calculate the actual fee
            var vm = new GasMachine(genScript(0.1m), 0, null);
            var result = vm.Execute();
            var usedGas = UnitConversion.ToDecimal((int)(vm.UsedGas * gasPrice), DomainSettings.FuelTokenDecimals);

            simulator.BeginBlock();
            var tx = simulator.GenerateCustomTransaction(neoKeys, ProofOfWork.None, () =>
            {
                return genScript(usedGas);
            });

            simulator.EndBlock();

            var swapToken = simulator.Nexus.GetTokenInfo(simulator.Nexus.RootStorage, swapSymbol);
            var balance = nexus.RootChain.GetTokenBalance(simulator.Nexus.RootStorage, swapToken, transcodedAddress);
            Assert.IsTrue(balance == 0);

            balance = nexus.RootChain.GetTokenBalance(simulator.Nexus.RootStorage, swapToken, testUser.Address);
            Assert.IsTrue(balance > 0);

            var settleHash = (Hash)nexus.RootChain.InvokeContract(nexus.RootStorage, "interop", nameof(InteropContract.GetSettlement), "neo", neoTxHash).ToObject();
            Assert.IsTrue(settleHash == tx.Hash);

            var fuelToken = nexus.GetTokenInfo(simulator.Nexus.RootStorage, DomainSettings.FuelTokenSymbol);
            var leftoverBalance = nexus.RootChain.GetTokenBalance(simulator.Nexus.RootStorage, fuelToken, transcodedAddress);
            //Assert.IsTrue(leftoverBalance == 0);
        }

        [TestMethod]
        public void ChainSwapOut()
        {
            var owner = PhantasmaKeys.Generate();

            var simulator = new NexusSimulator(owner);
            var nexus = simulator.Nexus;

            var rootChain = nexus.RootChain;

            var testUser = PhantasmaKeys.Generate();

            var potAddress = SmartContract.GetAddressForNative(NativeContractKind.Swap);

            // 0 - just send some assets to the 
            simulator.BeginBlock();
            simulator.GenerateTransfer(owner, testUser.Address, nexus.RootChain, DomainSettings.StakingTokenSymbol, UnitConversion.ToBigInteger(10, DomainSettings.StakingTokenDecimals));
            simulator.GenerateTransfer(owner, testUser.Address, nexus.RootChain, DomainSettings.FuelTokenSymbol, UnitConversion.ToBigInteger(10, DomainSettings.FuelTokenDecimals));
            simulator.MintTokens(owner, potAddress, "GAS", UnitConversion.ToBigInteger(1, 8));
            simulator.EndBlock();

            var oldBalance = rootChain.GetTokenBalance(rootChain.Storage, DomainSettings.StakingTokenSymbol, testUser.Address);
            var oldSupply = rootChain.GetTokenSupply(rootChain.Storage, DomainSettings.StakingTokenSymbol);

            // 1 - transfer to an external interop address
            var targetAddress = NeoWallet.EncodeAddress("AG2vKfVpTozPz2MXvye4uDCtYcTnYhGM8F");
            simulator.BeginBlock();
            simulator.GenerateTransfer(testUser, targetAddress, nexus.RootChain, DomainSettings.StakingTokenSymbol, UnitConversion.ToBigInteger(10, DomainSettings.StakingTokenDecimals));
            simulator.EndBlock();

            var currentBalance = rootChain.GetTokenBalance(rootChain.Storage, DomainSettings.StakingTokenSymbol, testUser.Address);
            var currentSupply = rootChain.GetTokenSupply(rootChain.Storage, DomainSettings.StakingTokenSymbol);

            Assert.IsTrue(currentBalance < oldBalance);
            Assert.IsTrue(currentBalance == 0);

            Assert.IsTrue(currentSupply < oldSupply);
        }*/

        [TestMethod]
        public void QuoteConversions()
        {
            var owner = PhantasmaKeys.Generate();

            var simulator = new NexusSimulator(owner);
            var nexus = simulator.Nexus;

            Assert.IsTrue(nexus.PlatformExists(nexus.RootStorage, "neo"));
            Assert.IsTrue(nexus.TokenExists(nexus.RootStorage, "NEO"));

            var context = new StorageChangeSetContext(nexus.RootStorage);
            var runtime = new RuntimeVM(-1, new byte[0], 0, nexus.RootChain, Address.Null, Timestamp.Now, Transaction.Null, context, new OracleSimulator(nexus), ChainTask.Null);

            var temp = runtime.GetTokenQuote("NEO", "KCAL", 1);
            var price = UnitConversion.ToDecimal(temp, DomainSettings.FuelTokenDecimals);
            Assert.IsTrue(price == 100);

            temp = runtime.GetTokenQuote("KCAL", "NEO", UnitConversion.ToBigInteger(100, DomainSettings.FuelTokenDecimals));
            price = UnitConversion.ToDecimal(temp, 0);
            Assert.IsTrue(price == 1);

            temp = runtime.GetTokenQuote("SOUL", "KCAL", UnitConversion.ToBigInteger(1, DomainSettings.StakingTokenDecimals));
            price = UnitConversion.ToDecimal(temp, DomainSettings.FuelTokenDecimals);
            Assert.IsTrue(price == 5);
        }


        [TestMethod]
        public void SideChainTransferDifferentAccounts()
        {
            var owner = PhantasmaKeys.Generate();

            var simulator = new NexusSimulator(owner);
            var nexus = simulator.Nexus;

            var sourceChain = nexus.RootChain;

            var symbol = DomainSettings.FuelTokenSymbol;

            var sender = PhantasmaKeys.Generate();
            var receiver = PhantasmaKeys.Generate();

            var token = nexus.GetTokenInfo(nexus.RootStorage, symbol);
            var originalAmount = UnitConversion.ToBigInteger(10, token.Decimals);
            var sideAmount = originalAmount / 2;

            Assert.IsTrue(sideAmount > 0);

            // Send from Genesis address to "sender" user
            // Send from Genesis address to "sender" user
            simulator.BeginBlock();
            simulator.GenerateTransfer(owner, sender.Address, nexus.RootChain, symbol, originalAmount);
            simulator.EndBlock();

            simulator.BeginBlock();
            simulator.GenerateChain(owner, DomainSettings.ValidatorsOrganizationName, "main", "test");
            simulator.EndBlock();

            var targetChain = nexus.GetChainByName("test");

            // verify test user balance
            var balance = nexus.RootChain.GetTokenBalance(simulator.Nexus.RootStorage, token, sender.Address);
            Assert.IsTrue(balance == originalAmount);

            var crossFee = UnitConversion.ToBigInteger(0.001m, token.Decimals);

            // do a side chain send using test user balance from root to account chain
            simulator.BeginBlock();
            var txA = simulator.GenerateSideChainSend(sender, symbol, sourceChain, receiver.Address, targetChain, sideAmount, crossFee);
            simulator.EndBlock();
            var blockAHash = nexus.RootChain.GetLastBlockHash();
            var blockA = nexus.RootChain.GetBlockByHash(blockAHash);

            // finish the chain transfer
            simulator.BeginBlock();
            var txB = simulator.GenerateSideChainSettlement(receiver, nexus.RootChain, targetChain, txA);
            Assert.IsTrue(simulator.EndBlock().Any());

            // verify balances
            var feeB = targetChain.GetTransactionFee(txB);
            balance = targetChain.GetTokenBalance(targetChain.Storage, token, receiver.Address);
            var expectedAmount = (sideAmount + crossFee) - feeB;
            Assert.IsTrue(balance == expectedAmount);

            var feeA = sourceChain.GetTransactionFee(txA);
            var leftoverAmount = originalAmount - (sideAmount + feeA + crossFee);

            balance = sourceChain.GetTokenBalance(sourceChain.Storage, token, sender.Address);
            Assert.IsTrue(balance == leftoverAmount);
        }

        [TestMethod]
        public void SideChainTransferSameAccount()
        {
            var owner = PhantasmaKeys.Generate();

            var simulator = new NexusSimulator(owner);
            var nexus = simulator.Nexus;

            var sourceChain = nexus.RootChain;

            var symbol = DomainSettings.FuelTokenSymbol;

            var sender = PhantasmaKeys.Generate();

            var token = nexus.GetTokenInfo(nexus.RootStorage, symbol);
            var originalAmount = UnitConversion.ToBigInteger(1, token.Decimals);
            var sideAmount = originalAmount / 2;

            Assert.IsTrue(sideAmount > 0);

            // Send from Genesis address to "sender" user
            simulator.BeginBlock();
            simulator.GenerateTransfer(owner, sender.Address, nexus.RootChain, symbol, originalAmount);
            simulator.EndBlock();

            simulator.BeginBlock();
            simulator.GenerateChain(owner, DomainSettings.ValidatorsOrganizationName, "main", "test");
            simulator.EndBlock();

            var targetChain = nexus.GetChainByName("test");

            // verify test user balance
            var balance = nexus.RootChain.GetTokenBalance(simulator.Nexus.RootStorage, token, sender.Address);
            Assert.IsTrue(balance == originalAmount);

            // do a side chain send using test user balance from root to account chain
            simulator.BeginBlock();
            var txA = simulator.GenerateSideChainSend(sender, symbol, sourceChain, sender.Address, targetChain, sideAmount, 0);
            var blockA = simulator.EndBlock().FirstOrDefault();
            Assert.IsTrue(blockA != null);

            // finish the chain transfer from parent to child
            simulator.BeginBlock();
            var txB = simulator.GenerateSideChainSettlement(sender, sourceChain, targetChain, txA);
            Assert.IsTrue(simulator.EndBlock().Any());

            // verify balances
            var feeB = targetChain.GetTransactionFee(txB);
            balance = targetChain.GetTokenBalance(simulator.Nexus.RootStorage, token, sender.Address);
            //Assert.IsTrue(balance == sideAmount - feeB); TODO CHECK THIS BERNARDO

            var feeA = sourceChain.GetTransactionFee(txA);
            var leftoverAmount = originalAmount - (sideAmount + feeA);

            balance = sourceChain.GetTokenBalance(simulator.Nexus.RootStorage, token, sender.Address);
            Assert.IsTrue(balance == leftoverAmount);

            sideAmount /= 2;
            simulator.BeginBlock();
            var txC = simulator.GenerateSideChainSend(sender, symbol, targetChain, sender.Address, sourceChain, sideAmount, 0);
            var blockC = simulator.EndBlock().FirstOrDefault();
            Assert.IsTrue(blockC != null);

            // finish the chain transfer from child to parent
            simulator.BeginBlock();
            var txD = simulator.GenerateSideChainSettlement(sender, targetChain, sourceChain, txC);
            Assert.IsTrue(simulator.EndBlock().Any());
        }

        [TestMethod]
        public void SideChainTransferMultipleSteps()
        {
            var owner = PhantasmaKeys.Generate();

            var simulator = new NexusSimulator(owner);
            var nexus = simulator.Nexus;

            simulator.BeginBlock();
            simulator.GenerateChain(owner, DomainSettings.ValidatorsOrganizationName, nexus.RootChain.Name, "sale");
            simulator.EndBlock();

            var sourceChain = nexus.RootChain;
            var sideChain = nexus.GetChainByName("sale");
            Assert.IsTrue(sideChain != null);

            var symbol = DomainSettings.FuelTokenSymbol;
            var token = nexus.GetTokenInfo(nexus.RootStorage, symbol);

            var sender = PhantasmaKeys.Generate();
            var receiver = PhantasmaKeys.Generate();

            var originalAmount = UnitConversion.ToBigInteger(10, token.Decimals);
            var sideAmount = originalAmount / 2;

            Assert.IsTrue(sideAmount > 0);

            var newChainName = "testing";

            // Send from Genesis address to "sender" user
            simulator.BeginBlock();
            simulator.GenerateTransfer(owner, sender.Address, nexus.RootChain, symbol, originalAmount);
            simulator.GenerateChain(owner, DomainSettings.ValidatorsOrganizationName, sideChain.Name, newChainName);
            simulator.EndBlock();

            var targetChain = nexus.GetChainByName(newChainName);

            // verify test user balance
            var balance = nexus.RootChain.GetTokenBalance(simulator.Nexus.RootStorage, token, sender.Address);
            Assert.IsTrue(balance == originalAmount);

            // do a side chain send using test user balance from root to apps chain
            simulator.BeginBlock();
            var txA = simulator.GenerateSideChainSend(sender, symbol, sourceChain, sender.Address, sideChain, sideAmount, 0);
            var blockA = simulator.EndBlock().FirstOrDefault();
            var evtsA = blockA.GetEventsForTransaction(txA.Hash);

            // finish the chain transfer
            simulator.BeginBlock();
            var txB = simulator.GenerateSideChainSettlement(sender, nexus.RootChain, sideChain, txA);
            Assert.IsTrue(simulator.EndBlock().Any());

            var txCostA = simulator.Nexus.RootChain.GetTransactionFee(txA);
            var txCostB = sideChain.GetTransactionFee(txB);
            sideAmount = sideAmount - txCostA;

            balance = sideChain.GetTokenBalance(simulator.Nexus.RootStorage, token, sender.Address);
            Console.WriteLine($"{balance}/{sideAmount}");
            Assert.IsTrue(balance == sideAmount);

            var extraFree = UnitConversion.ToBigInteger(0.01m, token.Decimals);

            sideAmount -= extraFree * 10;

            // do another side chain send using test user balance from apps to target chain
            simulator.BeginBlock();
            var txC = simulator.GenerateSideChainSend(sender, symbol, sideChain, receiver.Address, targetChain, sideAmount, extraFree);
            var blockC = simulator.EndBlock().FirstOrDefault();

            var evtsC = blockC.GetEventsForTransaction(txC.Hash);

            var appSupplies = new SupplySheet(symbol, sideChain, nexus);
            var childBalance = appSupplies.GetChildBalance(sideChain.Storage, targetChain.Name);
            var expectedChildBalance = sideAmount + extraFree;

            // finish the chain transfer
            simulator.BeginBlock();
            var txD = simulator.GenerateSideChainSettlement(receiver, sideChain, targetChain, txC);
            Assert.IsTrue(simulator.EndBlock().Any());

            // TODO  verify balances
        }

 

        [TestMethod]
        public void NoGasSameChainTransfer()
        {
            var owner = PhantasmaKeys.Generate();

            var simulator = new NexusSimulator(owner);
            var nexus = simulator.Nexus;

            var accountChain = nexus.GetChainByName("account");

            var symbol = DomainSettings.FuelTokenSymbol;
            var token = nexus.GetTokenInfo(nexus.RootStorage, symbol);

            var sender = PhantasmaKeys.Generate();
            var receiver = PhantasmaKeys.Generate();

            var amount = UnitConversion.ToBigInteger(1, token.Decimals);

            // Send from Genesis address to test user
            simulator.BeginBlock();
            var tx = simulator.GenerateTransfer(owner, sender.Address, nexus.RootChain, symbol, amount);
            simulator.EndBlock();

            var oldBalance = nexus.RootChain.GetTokenBalance(simulator.Nexus.RootStorage, token, sender.Address);
            Assert.IsTrue(oldBalance == amount);

            var gasFee = nexus.RootChain.GetTransactionFee(tx);
            Assert.IsTrue(gasFee > 0);

            amount /= 2;
            simulator.BeginBlock();
            simulator.GenerateTransfer(sender, receiver.Address, nexus.RootChain, symbol, amount);
            simulator.EndBlock();

            // verify test user balance
            var transferBalance = nexus.RootChain.GetTokenBalance(simulator.Nexus.RootStorage, token, receiver.Address);

            var newBalance = nexus.RootChain.GetTokenBalance(simulator.Nexus.RootStorage, token, sender.Address);

            Assert.IsTrue(transferBalance + newBalance + gasFee == oldBalance);

            // create a new receiver
            receiver = PhantasmaKeys.Generate();

            //Try to send the entire balance without affording fees from sender to receiver
            try
            {
                simulator.BeginBlock();
                tx = simulator.GenerateTransfer(sender, receiver.Address, nexus.RootChain, symbol, transferBalance);
                simulator.EndBlock();
            }
            catch (Exception e)
            {
                Assert.IsNotNull(e);
            }

            // verify balances, receiver should have 0 balance
            transferBalance = nexus.RootChain.GetTokenBalance(simulator.Nexus.RootStorage, token, receiver.Address);
            Assert.IsTrue(transferBalance == 0, "Transaction failed completely as expected");
        }

        [TestMethod]
        public void NoGasTestSideChainTransfer()
        {
            var owner = PhantasmaKeys.Generate();

            var simulator = new NexusSimulator(owner);
            var nexus = simulator.Nexus;

            simulator.BeginBlock();
            simulator.GenerateChain(owner, DomainSettings.ValidatorsOrganizationName, nexus.RootChain.Name, "sale");
            simulator.EndBlock();

            var sourceChain = nexus.RootChain;
            var targetChain = nexus.GetChainByName("sale");

            var symbol = DomainSettings.FuelTokenSymbol;
            var token = nexus.GetTokenInfo(nexus.RootStorage, symbol);

            var sender = PhantasmaKeys.Generate();
            var receiver = PhantasmaKeys.Generate();

            var originalAmount = UnitConversion.ToBigInteger(10, token.Decimals);
            var sideAmount = originalAmount / 2;

            Assert.IsTrue(sideAmount > 0);

            // Send from Genesis address to "sender" user
            simulator.BeginBlock();
            simulator.GenerateTransfer(owner, sender.Address, nexus.RootChain, symbol, originalAmount);
            simulator.EndBlock();

            // verify test user balance
            var balance = nexus.RootChain.GetTokenBalance(simulator.Nexus.RootStorage, token, sender.Address);
            Assert.IsTrue(balance == originalAmount);

            Transaction txA = null, txB = null;

            try
            {
                // do a side chain send using test user balance from root to account chain
                simulator.BeginBlock();
                txA = simulator.GenerateSideChainSend(sender, symbol, sourceChain, receiver.Address, targetChain,
                    originalAmount, 1);
                simulator.EndBlock();
            }
            catch (Exception e)
            {
                Assert.IsNotNull(e);
            }

            try
            {
                var blockAHash = nexus.RootChain.GetLastBlockHash();
                var blockA = nexus.RootChain.GetBlockByHash(blockAHash);

                // finish the chain transfer
                simulator.BeginBlock();
                txB = simulator.GenerateSideChainSettlement(sender, nexus.RootChain, targetChain, txA);
                Assert.IsTrue(simulator.EndBlock().Any());
            }
            catch (Exception e)
            {
                Assert.IsNotNull(e);
            }

            // verify balances, receiver should have 0 balance
            balance = targetChain.GetTokenBalance(simulator.Nexus.RootStorage, token, receiver.Address);
            Assert.IsTrue(balance == 0);
        }


        [TestMethod]
        public void AddressComparison()
        {
            var owner = PhantasmaKeys.FromWIF("Kweyrx8ypkoPfzMsxV4NtgH8vXCWC1s1Dn3c2KJ4WAzC5nkyNt3e");
            var expectedAddress = owner.Address.Text;

            var input = "P2K9LSag1D7EFPBvxMa1fW1c4oNbmAQX7qj6omvo17Fwrg8";
            var address = Address.FromText(input);

            Assert.IsTrue(expectedAddress == input);

            /*
            var simulator = new NexusSimulator(owner);
            var nexus = simulator.Nexus;

            var genesisAddress = nexus.GetGenesisAddress(nexus.RootStorage);
            Assert.IsTrue(address == genesisAddress);
            Assert.IsTrue(address.Text == genesisAddress.Text);
            Assert.IsTrue(address.ToByteArray().SequenceEqual(genesisAddress.ToByteArray()));*/
        }

        [TestMethod]
        public void ChainTransferExploit()
        {
            var owner = PhantasmaKeys.FromWIF("L2LGgkZAdupN2ee8Rs6hpkc65zaGcLbxhbSDGq8oh6umUxxzeW25");

            var simulator = new NexusSimulator(owner);
            var nexus = simulator.Nexus;

            var user = PhantasmaKeys.Generate();

            var symbol = DomainSettings.StakingTokenSymbol;

            var chainAddressStr = Base16.Encode(simulator.Nexus.RootChain.Address.ToByteArray());
            var userAddressStr = Base16.Encode(user.Address.ToByteArray());

            simulator.BeginBlock();
            simulator.GenerateTransfer(owner, user.Address, simulator.Nexus.RootChain, DomainSettings.FuelTokenSymbol, 100000000);
            simulator.GenerateTransfer(owner, user.Address, simulator.Nexus.RootChain, DomainSettings.StakingTokenSymbol, 100000000);
            simulator.EndBlock();

            var chainAddress = simulator.Nexus.RootChain.Address;
            simulator.BeginBlock();
            var tx = simulator.GenerateTransfer(owner, chainAddress, simulator.Nexus.RootChain, symbol, 100000000);
            var block = simulator.EndBlock().First();

            var evts = block.GetEventsForTransaction(tx.Hash);
            Assert.IsTrue(evts.Any(x => x.Kind == EventKind.TokenReceive && x.Address == chainAddress));

            var token = simulator.Nexus.GetTokenInfo(simulator.Nexus.RootStorage, symbol);

            var initialBalance = simulator.Nexus.RootChain.GetTokenBalance(simulator.Nexus.RootStorage, token, chainAddress);
            Assert.IsTrue(initialBalance > 10000);

            string[] scriptString = new string[]
            {
                $"alias r5, $sourceAddress",
                $"alias r6, $targetAddress",
                $"alias r7, $amount",
                $"alias r8, $symbol",

                $"load $amount, 10000",
                $@"load $symbol, ""{symbol}""",

                $"load r11 0x{chainAddressStr}",
                $"push r11",
                $@"extcall ""Address()""",
                $"pop $sourceAddress",

                $"load r11 0x{userAddressStr}",
                $"push r11",
                $@"extcall ""Address()""",
                $"pop $targetAddress",

                $"push $amount",
                $"push $symbol",
                $"push $targetAddress",
                $"push $sourceAddress",
                "extcall \"Runtime.TransferTokens\"",
            };

            var script = AssemblerUtils.BuildScript(scriptString);

            simulator.BeginBlock();
            simulator.GenerateCustomTransaction(user, ProofOfWork.None, () =>
                ScriptUtils.BeginScript().
                    AllowGas(user.Address, Address.Null, simulator.MinimumFee, Transaction.DefaultGasLimit).
                    EmitRaw(script).
                    SpendGas(user.Address).
                    EndScript());

            try
            {
                simulator.EndBlock();
            }
            catch (Exception e)
            {
                Assert.IsTrue(e is ChainException);
            }

            var finalBalance = simulator.Nexus.RootChain.GetTokenBalance(simulator.Nexus.RootStorage, token, simulator.Nexus.RootChain.Address);
            Assert.IsTrue(initialBalance == finalBalance);
        }

        [TestMethod]
        public void TransactionFees()
        {
            var owner = PhantasmaKeys.Generate();

            var simulator = new NexusSimulator(owner);
            var nexus = simulator.Nexus;

            var testUserA = PhantasmaKeys.Generate();
            var testUserB = PhantasmaKeys.Generate();

            var fuelAmount = UnitConversion.ToBigInteger(10, DomainSettings.FuelTokenDecimals);
            var transferAmount = UnitConversion.ToBigInteger(10, DomainSettings.StakingTokenDecimals);

            simulator.BeginBlock();
            simulator.GenerateTransfer(owner, testUserA.Address, nexus.RootChain, DomainSettings.FuelTokenSymbol, fuelAmount);
            simulator.GenerateTransfer(owner, testUserA.Address, nexus.RootChain, DomainSettings.StakingTokenSymbol, transferAmount);
            simulator.EndBlock();

            // Send from user A to user B
            simulator.BeginBlock();
            simulator.GenerateTransfer(testUserA, testUserB.Address, nexus.RootChain, DomainSettings.StakingTokenSymbol, transferAmount);
            var block = simulator.EndBlock().FirstOrDefault();

            Assert.IsTrue(block != null);

            var hash = block.TransactionHashes.First();

            var feeValue = nexus.RootChain.GetTransactionFee(hash);
            var feeAmount = UnitConversion.ToDecimal(feeValue, DomainSettings.FuelTokenDecimals);
            Assert.IsTrue(feeAmount >= 0.0009m);

            var token = simulator.Nexus.GetTokenInfo(simulator.Nexus.RootStorage, DomainSettings.StakingTokenSymbol);
            var finalBalance = simulator.Nexus.RootChain.GetTokenBalance(simulator.Nexus.RootStorage, token, testUserB.Address);
            Assert.IsTrue(finalBalance == transferAmount);
        }

        /*[TestMethod]
        public void ValidatorSwitch()
        {
            var owner = PhantasmaKeys.Generate();

            var simulator = new NexusSimulator(owner);
            var nexus = simulator.Nexus;

            simulator.blockTimeSkip = TimeSpan.FromSeconds(5);

            var secondValidator = PhantasmaKeys.Generate();

            var fuelAmount = UnitConversion.ToBigInteger(10, DomainSettings.FuelTokenDecimals);
            var stakeAmount = UnitConversion.ToBigInteger(50000, DomainSettings.StakingTokenDecimals);

            // make first validator allocate 5 more validator spots       
            simulator.BeginBlock();
            simulator.GenerateTransfer(owner, secondValidator.Address, nexus.RootChain, DomainSettings.FuelTokenSymbol, fuelAmount);
            simulator.GenerateTransfer(owner, secondValidator.Address, nexus.RootChain, DomainSettings.StakingTokenSymbol, stakeAmount);
            simulator.GenerateCustomTransaction(owner, ProofOfWork.None, () =>
                ScriptUtils.BeginScript().
                    AllowGas(owner.Address, Address.Null, simulator.MinimumFee, Transaction.DefaultGasLimit).
                    CallContract(NativeContractKind.Governance, "SetValue", ValidatorContract.ValidatorSlotsDefault, new BigInteger(5)).
                    SpendGas(owner.Address).
                    EndScript());
            simulator.EndBlock();

            // make second validator candidate stake enough to become a stake master
            simulator.BeginBlock();
            simulator.GenerateCustomTransaction(secondValidator, ProofOfWork.None, () =>
                ScriptUtils.BeginScript().
                    AllowGas(secondValidator.Address, Address.Null, simulator.MinimumFee, Transaction.DefaultGasLimit).
                    CallContract(NativeContractKind.Stake, "Stake", secondValidator.Address, stakeAmount).
                    SpendGas(secondValidator.Address).
                    EndScript());
            simulator.EndBlock();

            // set a second validator, no election required because theres only one validator for now
            simulator.BeginBlock();
            var tx = simulator.GenerateCustomTransaction(owner, ProofOfWork.None, () =>
                ScriptUtils.BeginScript().
                    AllowGas(owner.Address, Address.Null, simulator.MinimumFee, Transaction.DefaultGasLimit).
                    CallContract(Nexus.ValidatorContractName, "SetValidator", secondValidator.Address, 1, ValidatorType.Primary).
                    SpendGas(owner.Address).
                    EndScript());
            var block = simulator.EndBlock().First();

            // verify that we suceed adding a new validator
            var events = block.GetEventsForTransaction(tx.Hash).ToArray();
            Assert.IsTrue(events.Length > 0);
            Assert.IsTrue(events.Any(x => x.Kind == EventKind.ValidatorPropose));

            // make the second validator accept his spot
            simulator.BeginBlock();
            tx = simulator.GenerateCustomTransaction(secondValidator, ProofOfWork.None, () =>
            ScriptUtils.BeginScript().
                AllowGas(secondValidator.Address, Address.Null, simulator.MinimumFee, Transaction.DefaultGasLimit).
                CallContract(Nexus.ValidatorContractName, "SetValidator", secondValidator.Address, 1, ValidatorType.Primary).
                SpendGas(secondValidator.Address).
                EndScript());
            block = simulator.EndBlock().First();

            // verify that we suceed electing a new validator
            events = block.GetEventsForTransaction(tx.Hash).ToArray();
            Assert.IsTrue(events.Length > 0);
            Assert.IsTrue(events.Any(x => x.Kind == EventKind.ValidatorElect));

            var testUserA = PhantasmaKeys.Generate();
            var testUserB = PhantasmaKeys.Generate();

            var validatorSwitchAttempts = 100;
            var transferAmount = UnitConversion.ToBigInteger(1, DomainSettings.StakingTokenDecimals);
            var accountBalance = transferAmount * validatorSwitchAttempts;

            simulator.BeginBlock();
            simulator.GenerateTransfer(owner, testUserA.Address, nexus.RootChain, DomainSettings.FuelTokenSymbol, fuelAmount);
            simulator.GenerateTransfer(owner, testUserA.Address, nexus.RootChain, DomainSettings.StakingTokenSymbol, accountBalance);
            simulator.EndBlock();
            
            var currentValidatorIndex = 0;

            var token = simulator.Nexus.GetTokenInfo(simulator.Nexus.RootStorage, DomainSettings.StakingTokenSymbol);
            for (int i = 0; i < validatorSwitchAttempts; i++)
            {
                var initialBalance = simulator.Nexus.RootChain.GetTokenBalance(simulator.Nexus.RootStorage, token, testUserB.Address);

                // here we skip to a time where its supposed to be the turn of the given validator index

                SkipToValidatorIndex(simulator, currentValidatorIndex);
                //simulator.CurrentTime = (DateTime)simulator.Nexus.GenesisTime + TimeSpan.FromSeconds(120 * 500 + 130);

                //TODO needs to be checked again
                //var currentValidator = currentValidatorIndex == 0 ? owner : secondValidator;
                var currentValidator = (simulator.Nexus.RootChain.GetValidator(simulator.Nexus.RootStorage, simulator.CurrentTime) == owner.Address) ? owner : secondValidator;

                simulator.BeginBlock(currentValidator);
                simulator.GenerateTransfer(testUserA, testUserB.Address, nexus.RootChain, DomainSettings.StakingTokenSymbol, transferAmount);
                var lastBlock = simulator.EndBlock().First();

                var firstTxHash = lastBlock.TransactionHashes.First();
                events = lastBlock.GetEventsForTransaction(firstTxHash).ToArray();
                Assert.IsTrue(events.Length > 0);
                //Assert.IsTrue(events.Any(x => x.Kind == EventKind.ValidatorSwitch));

                var finalBalance = simulator.Nexus.RootChain.GetTokenBalance(simulator.Nexus.RootStorage, token, testUserB.Address);
                Assert.IsTrue(finalBalance == initialBalance + transferAmount);

                currentValidatorIndex = currentValidatorIndex == 1 ? 0 : 1; //toggle the current validator index
            }

            // Send from user A to user B
            // NOTE this block is baked by the second validator
            
        }*/

        private void SkipToValidatorIndex(NexusSimulator simulator, int i)
        {
            uint skippedSeconds = 0;
            var genesisBlock = simulator.Nexus.GetGenesisBlock();
            DateTime genesisTime = genesisBlock.Timestamp;
            var diff = (simulator.CurrentTime - genesisTime).Seconds;
            //var index = (int)(diff / 120) % 2;
            skippedSeconds = (uint)(120 - diff);
            //Console.WriteLine("index: " + index);

            //while (index != i)
            //{
            //    skippedSeconds++;
            //    diff++;
            //    index = (int)(diff / 120) % 2;
            //}

            Console.WriteLine("skippedSeconds: " + skippedSeconds);
            simulator.CurrentTime = simulator.CurrentTime.AddSeconds(skippedSeconds);
        }

        [TestMethod]
        public void GasFeeCalculation()
        {
            var limit = 400;
            var testUser = PhantasmaKeys.Generate();
            var transcodedAddress = PhantasmaKeys.Generate().Address;
            var swapSymbol = "SOUL";

            var script = new ScriptBuilder()
            .CallContract("interop", "SettleTransaction", transcodedAddress, "neo", "neo", Hash.Null)
            .CallContract("swap", "SwapFee", transcodedAddress, swapSymbol, UnitConversion.ToBigInteger(0.1m, DomainSettings.FuelTokenDecimals))
            .TransferBalance(swapSymbol, transcodedAddress, testUser.Address)
            .AllowGas(transcodedAddress, Address.Null, Transaction.DefaultGasLimit, limit)
            .SpendGas(transcodedAddress).EndScript();

            var vm = new GasMachine(script, 0, null);
            var result = vm.Execute();
            Assert.IsTrue(result == ExecutionState.Halt);
            Assert.IsTrue(vm.UsedGas > 0);
        }

        [TestMethod]
        public void ChainTransferStressTest()
        {
            var owner = PhantasmaKeys.Generate();

            var simulator = new NexusSimulator(owner);
            var nexus = simulator.Nexus;

            simulator.blockTimeSkip = TimeSpan.FromSeconds(5);

            var testUser = PhantasmaKeys.Generate();

            var fuelAmount = UnitConversion.ToBigInteger(100, DomainSettings.FuelTokenDecimals);
            var stakeAmount = UnitConversion.ToBigInteger(50000, DomainSettings.StakingTokenDecimals);

            simulator.BeginBlock();
            simulator.GenerateTransfer(owner, testUser.Address, nexus.RootChain, DomainSettings.FuelTokenSymbol, fuelAmount);
            simulator.GenerateTransfer(owner, testUser.Address, nexus.RootChain, DomainSettings.StakingTokenSymbol, stakeAmount);
            simulator.EndBlock();

            simulator.BeginBlock();
            simulator.GenerateCustomTransaction(testUser, ProofOfWork.None, () =>
                ScriptUtils.BeginScript().AllowGas(testUser.Address, Address.Null, simulator.MinimumFee, Transaction.DefaultGasLimit)
                    .CallContract(NativeContractKind.Stake, "Stake", testUser.Address, stakeAmount).
                    SpendGas(testUser.Address).EndScript());
            simulator.EndBlock();

            simulator.BeginBlock();
            for (int i = 0; i < 1000; i++)
            {
                var target = PhantasmaKeys.Generate();
                simulator.GenerateTransfer(owner, target.Address, nexus.RootChain, DomainSettings.FuelTokenSymbol, 1);
            }
            simulator.EndBlock();

            var x = 0;

            for (int i = 0; i < 1000; i++)
            {
                var target = PhantasmaKeys.Generate();
                simulator.BeginBlock();
                simulator.GenerateTransfer(owner, target.Address, simulator.Nexus.RootChain, DomainSettings.FuelTokenSymbol, 1);
                simulator.EndBlock();
            }

            x = 0;
        }

        [TestMethod]
        public void DeployCustomAccountScript()
        {
            var owner = PhantasmaKeys.Generate();

            var simulator = new NexusSimulator(owner);
            var nexus = simulator.Nexus;

            simulator.blockTimeSkip = TimeSpan.FromSeconds(5);

            var testUser = PhantasmaKeys.Generate();

            var fuelAmount = UnitConversion.ToBigInteger(100, DomainSettings.FuelTokenDecimals);
            var stakeAmount = UnitConversion.ToBigInteger(50000, DomainSettings.StakingTokenDecimals);

            simulator.BeginBlock();
            simulator.GenerateTransfer(owner, testUser.Address, nexus.RootChain, DomainSettings.FuelTokenSymbol, fuelAmount);
            simulator.GenerateTransfer(owner, testUser.Address, nexus.RootChain, DomainSettings.StakingTokenSymbol, stakeAmount);
            simulator.EndBlock();

            simulator.BeginBlock();
            simulator.GenerateCustomTransaction(testUser, ProofOfWork.None, () =>
                ScriptUtils.BeginScript().AllowGas(testUser.Address, Address.Null, simulator.MinimumFee, Transaction.DefaultGasLimit)
                    .CallContract(NativeContractKind.Stake, "Stake", testUser.Address, stakeAmount).
                    SpendGas(testUser.Address).EndScript());
            simulator.EndBlock();

            string[] scriptString;

            string message = "customEvent";
            var addressStr = Base16.Encode(testUser.Address.ToByteArray());

            var onMintTrigger = AccountTrigger.OnMint.ToString();
            var onWitnessTrigger = AccountTrigger.OnWitness.ToString();

            scriptString = new string[]
            {
                $"alias r4, $triggerMint",
                $"alias r5, $triggerWitness",
                $"alias r6, $comparisonResult",
                $"alias r8, $currentAddress",
                $"alias r9, $sourceAddress",

                $"@{onWitnessTrigger}: NOP ",
                $"pop $currentAddress",
                $"load r11 0x{addressStr}",
                $"push r11",
                "extcall \"Address()\"",
                $"pop $sourceAddress",
                $"equal $sourceAddress, $currentAddress, $comparisonResult",
                $"jmpif $comparisonResult, @end",
                $"load r0 \"something failed\"",
                $"throw r0",

                $"@{onMintTrigger}: NOP",
                $"pop $currentAddress",
                $"load r11 0x{addressStr}",
                $"push r11",
                $"extcall \"Address()\"",
                $"pop r11",

                $"load r10, {(int)EventKind.Custom}",
                $@"load r12, ""{message}""",

                $"push r10",
                $"push r11",
                $"push r12",
                $"extcall \"Runtime.Event\"",

                $"@end: ret"
            };

            DebugInfo debugInfo;
            Dictionary<string, int> labels;
            var script = AssemblerUtils.BuildScript(scriptString, "test", out debugInfo, out labels);

            var triggerList = new[] { AccountTrigger.OnWitness, AccountTrigger.OnMint };

            // here we fetch the jump offsets for each trigger
            var triggerMap = new Dictionary<AccountTrigger, int>();
            foreach (var trigger in triggerList)
            {
                var triggerName = trigger.ToString();
                var offset = labels[triggerName];
                triggerMap[trigger] = offset;
            }

            // now with that, we can build an usable contract interface that exposes those triggers as contract calls
            var methods = AccountContract.GetTriggersForABI(triggerMap);
            var abi = new ContractInterface(methods, Enumerable.Empty<ContractEvent>());
            var abiBytes = abi.ToByteArray();

            simulator.BeginBlock();
            simulator.GenerateCustomTransaction(testUser, ProofOfWork.None,
                () => ScriptUtils.BeginScript().AllowGas(testUser.Address, Address.Null, simulator.MinimumFee, Transaction.DefaultGasLimit)
                    .CallContract("account", "RegisterScript", testUser.Address, script, abiBytes)
                    .SpendGas(testUser.Address)
                    .EndScript());
            simulator.EndBlock();
        }

        [TestMethod]
        public void DeployCustomContract()
        {
            var owner = PhantasmaKeys.Generate();

            var simulator = new NexusSimulator(owner);
            var nexus = simulator.Nexus;

            simulator.blockTimeSkip = TimeSpan.FromSeconds(5);

            var testUser = PhantasmaKeys.Generate();

            var fuelAmount = UnitConversion.ToBigInteger(10000, DomainSettings.FuelTokenDecimals);
            var stakeAmount = UnitConversion.ToBigInteger(50000, DomainSettings.StakingTokenDecimals);

            simulator.BeginBlock();
            simulator.GenerateTransfer(owner, testUser.Address, nexus.RootChain, DomainSettings.FuelTokenSymbol, fuelAmount);
            simulator.GenerateTransfer(owner, testUser.Address, nexus.RootChain, DomainSettings.StakingTokenSymbol, stakeAmount);
            simulator.EndBlock();

            simulator.BeginBlock();
            simulator.GenerateCustomTransaction(testUser, ProofOfWork.None, () =>
                ScriptUtils.BeginScript().AllowGas(testUser.Address, Address.Null, simulator.MinimumFee, Transaction.DefaultGasLimit)
                    .CallContract(NativeContractKind.Stake, "Stake", testUser.Address, stakeAmount).
                    SpendGas(testUser.Address).EndScript());
            simulator.EndBlock();

            string[] scriptString;

            var methodName = "sum";

            scriptString = new string[]
            {
                $"@{methodName}: NOP ",
                $"pop r1",
                $"pop r2",
                $"add r1 r2 r3",
                $"push r3",
                $"@end: ret",
                $"@onUpgrade: ret",
                $"@onKill: ret",
            };

            DebugInfo debugInfo;
            Dictionary<string, int> labels;
            var script = AssemblerUtils.BuildScript(scriptString, "test", out debugInfo, out labels);

            var methods = new[]
            {
                new ContractMethod(methodName , VMType.Number, labels[methodName], new []{ new ContractParameter("a", VMType.Number), new ContractParameter("b", VMType.Number) }),
                new ContractMethod("onUpgrade", VMType.None, labels["onUpgrade"], new []{ new ContractParameter("addr", VMType.Object) }),
                new ContractMethod("onKill", VMType.None, labels["onKill"], new []{ new ContractParameter("addr", VMType.Object) }),
            };

            var abi = new ContractInterface(methods, Enumerable.Empty<ContractEvent>());
            var abiBytes = abi.ToByteArray();

            var contractName = "test";

            // deploy it
            simulator.BeginBlock();
            simulator.GenerateCustomTransaction(testUser, ProofOfWork.Minimal,
                () => ScriptUtils.BeginScript().AllowGas(testUser.Address, Address.Null, simulator.MinimumFee, Transaction.DefaultGasLimit)
                    .CallInterop("Runtime.DeployContract", testUser.Address, contractName, script, abiBytes)
                    .SpendGas(testUser.Address)
                    .EndScript());
            simulator.EndBlock();

            // send some funds to contract address
            var contractAddress = SmartContract.GetAddressFromContractName(contractName);
            simulator.BeginBlock();
            simulator.GenerateTransfer(owner, contractAddress, nexus.RootChain, DomainSettings.StakingTokenSymbol, stakeAmount);
            simulator.EndBlock();


            // now stake some SOUL on the contract address
            simulator.BeginBlock();
            simulator.GenerateCustomTransaction(testUser, ProofOfWork.None,
                () => ScriptUtils.BeginScript().AllowGas(testUser.Address, Address.Null, simulator.MinimumFee, Transaction.DefaultGasLimit)
                    .CallContract(NativeContractKind.Stake, nameof(StakeContract.Stake), contractAddress, UnitConversion.ToBigInteger(5, DomainSettings.StakingTokenDecimals))
                    .SpendGas(testUser.Address)
                    .EndScript());
            simulator.EndBlock();

            // upgrade it
            var newScript = Core.Utils.ByteArrayUtils.ConcatBytes(script, new byte[] { (byte)Opcode.NOP }); // concat useless opcode just to make it different
            simulator.BeginBlock();
            simulator.GenerateCustomTransaction(testUser, ProofOfWork.Minimal,
                () => ScriptUtils.BeginScript().AllowGas(testUser.Address, Address.Null, simulator.MinimumFee, Transaction.DefaultGasLimit)
                    .CallInterop("Runtime.UpgradeContract", testUser.Address, contractName, newScript, abiBytes)
                    .SpendGas(testUser.Address)
                    .EndScript());
            simulator.EndBlock();

            // kill it
            Assert.IsTrue(nexus.RootChain.IsContractDeployed(nexus.RootStorage, contractName));
            simulator.BeginBlock();
            simulator.GenerateCustomTransaction(testUser, ProofOfWork.Minimal,
                () => ScriptUtils.BeginScript().AllowGas(testUser.Address, Address.Null, simulator.MinimumFee, Transaction.DefaultGasLimit)
                    .CallInterop("Runtime.KillContract", testUser.Address, contractName)
                    .SpendGas(testUser.Address)
                    .EndScript());
            simulator.EndBlock();

            Assert.IsFalse(nexus.RootChain.IsContractDeployed(nexus.RootStorage, contractName));
        }

        [TestMethod]
        public void Inflation()
        {
            var owner = PhantasmaKeys.Generate();

            var simulator = new NexusSimulator(owner);
            var nexus = simulator.Nexus;

            Block block = null;
            simulator.TimeSkipDays(90, false, x => block = x);

            var inflation = false;
            foreach(var tx in block.TransactionHashes)
            {
                Console.WriteLine("tx: " + tx);
                foreach (var evt in block.GetEventsForTransaction(tx))
                {
                    if (evt.Kind == EventKind.Inflation)
                    {
                        inflation = true;
                    }
                }
            }

            Assert.AreEqual(true, inflation);
        }


        [TestMethod]
        public void PriceOracle()
        {
            var owner = PhantasmaKeys.Generate();

            var simulator = new NexusSimulator(owner);
            var nexus = simulator.Nexus;

            simulator.BeginBlock();
            simulator.GenerateCustomTransaction(owner, ProofOfWork.Moderate,
                () => ScriptUtils.BeginScript()
                    .CallInterop("Oracle.Price", "SOUL")
                    .AllowGas(owner.Address, Address.Null, simulator.MinimumFee, Transaction.DefaultGasLimit)
                    .SpendGas(owner.Address)
                    .EndScript());
            simulator.GenerateCustomTransaction(owner, ProofOfWork.Moderate,
                () => ScriptUtils.BeginScript()
                    .CallInterop("Oracle.Price", "SOUL")
                    .AllowGas(owner.Address, Address.Null, simulator.MinimumFee, 9997)
                    .SpendGas(owner.Address)
                    .EndScript());
            var block = simulator.EndBlock().First();

            foreach (var txHash in block.TransactionHashes)
            {
                var blkResult = block.GetResultForTransaction(txHash);
                var vmObj = VMObject.FromBytes(blkResult);
                Console.WriteLine("price: " + vmObj);
            }

            //TODO finish test
        }

        [TestMethod]
        public void OracleData()
        {
            var owner = PhantasmaKeys.Generate();

            var simulator = new NexusSimulator(owner);
            var nexus = simulator.Nexus;

            simulator.BeginBlock();
            simulator.GenerateCustomTransaction(owner, ProofOfWork.Moderate,
                () => ScriptUtils.BeginScript()
                    .CallInterop("Oracle.Price", "SOUL")
                    .AllowGas(owner.Address, Address.Null, simulator.MinimumFee, Transaction.DefaultGasLimit)
                    .SpendGas(owner.Address)
                    .EndScript());
            simulator.GenerateCustomTransaction(owner, ProofOfWork.Moderate,
                () => ScriptUtils.BeginScript()
                    .CallInterop("Oracle.Price", "KCAL")
                    .AllowGas(owner.Address, Address.Null, simulator.MinimumFee, Transaction.DefaultGasLimit)
                    .SpendGas(owner.Address)
                    .EndScript());
            var block1 = simulator.EndBlock().First();

            simulator.BeginBlock();
            simulator.GenerateCustomTransaction(owner, ProofOfWork.Moderate,
                () => ScriptUtils.BeginScript()
                    .CallInterop("Oracle.Price", "SOUL")
                    .AllowGas(owner.Address, Address.Null, simulator.MinimumFee, Transaction.DefaultGasLimit)
                    .SpendGas(owner.Address)
                    .EndScript());
            simulator.GenerateCustomTransaction(owner, ProofOfWork.Moderate,
                () => ScriptUtils.BeginScript()
                    .CallInterop("Oracle.Price", "KCAL")
                    .AllowGas(owner.Address, Address.Null, simulator.MinimumFee, Transaction.DefaultGasLimit)
                    .SpendGas(owner.Address)
                    .EndScript());
            var block2 = simulator.EndBlock().First();

            var oData1 = block1.OracleData.Count();
            var oData2 = block2.OracleData.Count();

            Console.WriteLine("odata1: " + oData1);
            Console.WriteLine("odata2: " + oData2);

            Assert.IsTrue(oData1 == oData2);
        }

        [TestMethod]
        public void DuplicateTransferTest()
        {
            var owner = PhantasmaKeys.Generate();

            var simulator = new NexusSimulator(owner);
            var nexus = simulator.Nexus;

            var target = PhantasmaKeys.Generate();

            simulator.BeginBlock();
            simulator.GenerateTransfer(owner, target.Address, simulator.Nexus.RootChain as Chain, DomainSettings.FuelTokenSymbol, 1);
            simulator.GenerateTransfer(owner, target.Address, simulator.Nexus.RootChain as Chain, DomainSettings.FuelTokenSymbol, 1);
            Assert.ThrowsException<ChainException>(() =>
            {
                simulator.EndBlock();
            });
        }
    }

}
