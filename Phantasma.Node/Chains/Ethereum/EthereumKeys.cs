﻿using Phantasma.Core;
using Phantasma.Core.ECC;
//using Phantasma.Ethereum.Hex.HexConvertors.Extensions;
//using Phantasma.Ethereum.Util;
using Nethereum.Hex.HexConvertors.Extensions;
using System;
using System.Linq;

namespace Phantasma.Node.Chains
{
    public class EthereumKey : IKeyPair
    {
        public byte[] PrivateKey{ get; private set; }
        public byte[] PublicKey { get; private set; }
        public readonly string Address;
        public readonly byte[] UncompressedPublicKey;

        public EthereumKey(byte[] privateKey)
        {
            if (privateKey.Length != 32 && privateKey.Length != 96 && privateKey.Length != 104)
                throw new ArgumentException();
            this.PrivateKey = new byte[32];
            Buffer.BlockCopy(privateKey, privateKey.Length - 32, PrivateKey, 0, 32);

            this.PublicKey = ECDsa.GetPublicKey(privateKey, true, ECDsaCurve.Secp256k1);
            this.UncompressedPublicKey = ECDsa.GetPublicKey(privateKey, false, ECDsaCurve.Secp256k1).Skip(1).ToArray();

            var kak = new Sha3Keccak().CalculateHash(this.UncompressedPublicKey);
            this.Address = "0x"+Base16.Encode( kak.Skip(12).ToArray());
        }

        public static EthereumKey FromPrivateKey(string prv)
        {
            if (prv == null) throw new ArgumentNullException();
            return new EthereumKey(prv.HexToByteArray());
        }

        public static EthereumKey FromWIF(string wif)
        {
            if (wif == null) throw new ArgumentNullException();
            byte[] data = wif.Base58CheckDecode();
            if (data.Length != 34 || data[0] != 0x80 || data[33] != 0x01)
                throw new FormatException();
            byte[] privateKey = new byte[32];
            Buffer.BlockCopy(data, 1, privateKey, 0, privateKey.Length);
            Array.Clear(data, 0, data.Length);
            return new EthereumKey(privateKey);
        }

        public static EthereumKey Generate()
        {
            var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
            return new EthereumKey(bytes);
        }

        public string GetWIF()
        {
            byte[] data = new byte[34];
            data[0] = 0x80;
            Buffer.BlockCopy(PrivateKey, 0, data, 1, 32);
            data[33] = 0x01;
            string wif = data.Base58CheckEncode();
            Array.Clear(data, 0, data.Length);
            return wif;
        }

        private static byte[] XOR(byte[] x, byte[] y)
        {
            if (x.Length != y.Length) throw new ArgumentException();
            return x.Zip(y, (a, b) => (byte)(a ^ b)).ToArray();
        }

        public override string ToString()
        {
            return this.Address;
        }

        public Signature Sign(byte[] msg, Func<byte[], byte[], byte[], byte[]> customSignFunction = null)
        {
            return ECDsaSignature.Generate(this, msg, ECDsaCurve.Secp256k1, customSignFunction);
        }
    }
}
