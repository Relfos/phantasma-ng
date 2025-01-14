﻿using System;
using System.Linq;
using System.Collections.Generic;
using Phantasma.Shared;
using Phantasma.Shared.Utils;
using Phantasma.Core;
using Phantasma.Core.ECC;

namespace Phantasma.Infrastructure.Chains
{
    public class NeoWallet : CryptoWallet
    {
        private static readonly object _lockObj = new object();
        public const string NeoPlatform = "neo";
        public const byte NeoID = 1;

        private string neoscanURL;

        public NeoWallet(PhantasmaKeys keys, string neoscanURL) : base(keys)
        {
            if (!neoscanURL.EndsWith("/"))
            {
                neoscanURL += "/";
            }

            this.neoscanURL = neoscanURL;
        }

        public override string Platform => NeoPlatform;

        public override void MakePayment(string symbol, decimal amount, string targetAddress, Action<bool> callback)
        {
            throw new NotImplementedException();
        }

        public override void SyncBalances(Action<bool> callback)
        {
            _balances.Clear();

            var url = $"{neoscanURL}api/main_net/v1/get_balance/{Address}";
            JSONRequest(url, (response) =>
            {
                if (response == null)
                {
                    callback(false);
                    return;
                }

                var temp = GetCryptoCurrencyInfos().Select(x => x.Symbol);
                var symbols = new HashSet<string>(temp);

                var root = response.RootElement.GetProperty("balance");
                foreach (var child in root.EnumerateArray())
                {
                    var symbol = child.GetProperty("asset_symbol").GetString();
                    var amount = child.GetProperty("amount").GetDecimal();
                    if (amount > 0 && symbols.Contains(symbol))
                    {
                        _balances.Add(new WalletBalance(symbol, amount));
                    }
                }

                callback(true);
            });
        }

        public static Address EncodeByteArray(byte[] scriptHash)
        {
            if (scriptHash[0] != 23 && scriptHash.Length == 20)
            {
                byte[] temp = new byte[scriptHash.Length + 1];
                temp[0] = 23; // assumed to be the neo interop identifier ??
                ByteArrayUtils.CopyBytes(scriptHash, 0, temp, 1, scriptHash.Length);
                scriptHash = temp;
            }

            var pubKey = new byte[33];
            ByteArrayUtils.CopyBytes(scriptHash, 0, pubKey, 0, scriptHash.Length);

            return Core.Address.FromInterop(NeoID, pubKey);
        }

        public static Address EncodeAddress(string addressText)
        {
            Throw.If(!IsValidAddress(addressText), "invalid neo address");
            var scriptHash = addressText.Base58CheckDecode();

            return EncodeByteArray(scriptHash);
        }

        public static string DecodeAddress(Address address)
        {
            if (!address.IsInterop)
            {
                throw new Exception("not an interop address");
            }

            byte platformID;
            byte[] scriptHash;
            address.DecodeInterop(out platformID, out scriptHash);

            if (platformID != NeoID)
            {
                throw new Exception("not a NEO interop address");
            }

            if (scriptHash[0] != 23)
            {
                throw new Exception("invalid NEO address");
            }

            scriptHash = scriptHash.Take(21).ToArray();

            return scriptHash.Base58CheckEncode();
        }

        protected override string DeriveAddress(PhantasmaKeys keys)
        {
            var bytes = ECDsa.GetPublicKey(keys.PrivateKey, true, ECDsaCurve.Secp256r1);

            var script = new byte[bytes.Length + 2];
            script[0] = 0x21;// OpCode.PUSHBYTES33;
            Array.Copy(bytes, 0, script, 1, bytes.Length);
            script[script.Length - 1] = 0xAC; // OpCode.CHECKSIG;

            var scriptHash = script.Sha256().RIPEMD160();

            //this.PublicKey = pKey.EncodePoint(false).Skip(1).ToArray();

            byte[] data = new byte[21];
            data[0] = 23;
            Buffer.BlockCopy(scriptHash.ToArray(), 0, data, 1, 20);
            return data.Base58CheckEncode();
        }

        public override IEnumerable<CryptoCurrencyInfo> GetCryptoCurrencyInfos()
        {
            yield return new CryptoCurrencyInfo("NEO", "NEO", 0, NeoPlatform, CryptoCurrencyCaps.Balance); // TODO check if 1 or 0
            yield return new CryptoCurrencyInfo("GAS", "GAS", 8, NeoPlatform, CryptoCurrencyCaps.Balance);
            yield return new CryptoCurrencyInfo("SOUL", "Phantasma Stake", 8, NeoPlatform, CryptoCurrencyCaps.Balance);
            yield break;
        }

        public static bool IsValidAddress(string address)
        {
            if (string.IsNullOrEmpty(address))
            {
                return false;
            }

            if (address.Length != 34)
            {
                return false;
            }

            byte[] buffer;
            try
            {
                buffer = Core.Base58.Decode(address);

            }
            catch
            {
                return false;
            }

            if (buffer.Length < 4) return false;

            byte[] checksum = buffer.Sha256(0, (uint)(buffer.Length - 4)).Sha256();
            return buffer.Skip(buffer.Length - 4).SequenceEqual(checksum.Take(4));
        }

    }
}
