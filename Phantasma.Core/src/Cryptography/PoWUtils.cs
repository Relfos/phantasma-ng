namespace Phantasma.Core.Cryptography
{
    public static class PoWUtils
    {
        public static int GetDifficulty(this Hash hash)
        {
            var bytes = hash.ToByteArray();

            int result = 0;
            for (int i=0; i<bytes.Length; i++)
            {
                var n = bytes[i];

                for (int j=0; j<8; j++)
                {
                    if ((n & (1 << j)) != 0)
                    {
                        result = 1 + (i << 3) + j;
                    }
                }
            }

            return 256 - result;
        }
    }
}
