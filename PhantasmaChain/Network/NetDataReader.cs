using System;
using System.Text;

namespace Phantasma.Network
{
    public class NetDataReader
    {
        protected byte[] _data;
        protected int _position;
        protected int _dataSize;

        public byte[] Data
        {
            get { return _data; }
        }

        public int Position
        {
            get { return _position; }
        }

        public bool EndOfData
        {
            get { return _position == _dataSize; }
        }

        public int AvailableBytes
        {
            get { return _dataSize - _position; }
        }

        public void SetSource(byte[] source)
        {
            _data = source;
            _position = 0;
            _dataSize = source.Length;
        }

        public void SetSource(byte[] source, int offset)
        {
            _data = source;
            _position = offset;
            _dataSize = source.Length;
        }

        public void SetSource(byte[] source, int offset, int dataSize)
        {
            _data = source;
            _position = offset;
            _dataSize = dataSize;
        }

        public NetDataReader()
        {

        }

        public NetDataReader(byte[] source)
        {
            SetSource(source);
        }

        public NetDataReader(byte[] source, int offset)
        {
            SetSource(source, offset);
        }

        public NetDataReader(byte[] source, int offset, int maxSize)
        {
            SetSource(source, offset, maxSize);
        }

        public Endpoint GetEndPoint()
        {
            string host = GetString(1000);
            int port = GetInt();
            return new Endpoint(host, port);
        }

        public byte GetByte()
        {
            byte res = _data[_position];
            _position += 1;
            return res;
        }

        public sbyte GetSByte()
        {
            var b = (sbyte)_data[_position];
            _position++;
            return b;
        }

        public bool[] GetBoolArray()
        {
            ushort size = BitConverter.ToUInt16(_data, _position);
            _position += 2;
            var arr = new bool[size];
            for (int i = 0; i < size; i++)
            {
                arr[i] = GetBool();
            }
            return arr;
        }

        public ushort[] GetUShortArray()
        {
            ushort size = BitConverter.ToUInt16(_data, _position);
            _position += 2;
            var arr = new ushort[size];
            for (int i = 0; i < size; i++)
            {
                arr[i] = GetUShort();
            }
            return arr;
        }

        public short[] GetShortArray()
        {
            ushort size = BitConverter.ToUInt16(_data, _position);
            _position += 2;
            var arr = new short[size];
            for (int i = 0; i < size; i++)
            {
                arr[i] = GetShort();
            }
            return arr;
        }

        public long[] GetLongArray()
        {
            ushort size = BitConverter.ToUInt16(_data, _position);
            _position += 2;
            var arr = new long[size];
            for (int i = 0; i < size; i++)
            {
                arr[i] = GetLong();
            }
            return arr;
        }

        public ulong[] GetULongArray()
        {
            ushort size = BitConverter.ToUInt16(_data, _position);
            _position += 2;
            var arr = new ulong[size];
            for (int i = 0; i < size; i++)
            {
                arr[i] = GetULong();
            }
            return arr;
        }

        public int[] GetIntArray()
        {
            ushort size = BitConverter.ToUInt16(_data, _position);
            _position += 2;
            var arr = new int[size];
            for (int i = 0; i < size; i++)
            {
                arr[i] = GetInt();
            }
            return arr;
        }

        public uint[] GetUIntArray()
        {
            ushort size = BitConverter.ToUInt16(_data, _position);
            _position += 2;
            var arr = new uint[size];
            for (int i = 0; i < size; i++)
            {
                arr[i] = GetUInt();
            }
            return arr;
        }

        public float[] GetFloatArray()
        {
            ushort size = BitConverter.ToUInt16(_data, _position);
            _position += 2;
            var arr = new float[size];
            for (int i = 0; i < size; i++)
            {
                arr[i] = GetFloat();
            }
            return arr;
        }

        public double[] GetDoubleArray()
        {
            ushort size = BitConverter.ToUInt16(_data, _position);
            _position += 2;
            var arr = new double[size];
            for (int i = 0; i < size; i++)
            {
                arr[i] = GetDouble();
            }
            return arr;
        }

        public string[] GetStringArray(int maxLength)
        {
            ushort size = BitConverter.ToUInt16(_data, _position);
            _position += 2;
            var arr = new string[size];
            for (int i = 0; i < size; i++)
            {
                arr[i] = GetString(maxLength);
            }
            return arr;
        }

        public bool GetBool()
        {
            bool res = _data[_position] > 0;
            _position += 1;
            return res;
        }

        public ushort GetUShort()
        {
            ushort result = BitConverter.ToUInt16(_data, _position);
            _position += 2;
            return result;
        }

        public short GetShort()
        {
            short result = BitConverter.ToInt16(_data, _position);
            _position += 2;
            return result;
        }

        public long GetLong()
        {
            long result = BitConverter.ToInt64(_data, _position);
            _position += 8;
            return result;
        }

        public ulong GetULong()
        {
            ulong result = BitConverter.ToUInt64(_data, _position);
            _position += 8;
            return result;
        }

        public int GetInt()
        {
            int result = BitConverter.ToInt32(_data, _position);
            _position += 4;
            return result;
        }

        public uint GetUInt()
        {
            uint result = BitConverter.ToUInt32(_data, _position);
            _position += 4;
            return result;
        }

        public float GetFloat()
        {
            float result = BitConverter.ToSingle(_data, _position);
            _position += 4;
            return result;
        }

        public double GetDouble()
        {
            double result = BitConverter.ToDouble(_data, _position);
            _position += 8;
            return result;
        }

        public string GetString(int maxLength)
        {
            int bytesCount = GetInt();
            if (bytesCount <= 0 || bytesCount > maxLength*2)
            {
                return string.Empty;
            }

            int charCount = Encoding.UTF8.GetCharCount(_data, _position, bytesCount);
            if (charCount > maxLength)
            {
                return string.Empty;
            }

            string result = Encoding.UTF8.GetString(_data, _position, bytesCount);
            _position += bytesCount;
            return result;
        }

        public byte[] GetBytes()
        {
            byte[] outgoingData = new byte[AvailableBytes];
            Buffer.BlockCopy(_data, _position, outgoingData, 0, AvailableBytes);
            _position = _data.Length;
            return outgoingData;
        }

        public void GetBytes(byte[] destination)
        {
            Buffer.BlockCopy(_data, _position, destination, 0, AvailableBytes);
            _position = _data.Length;
        }

        public void GetBytes(byte[] destination, int lenght)
        {
            Buffer.BlockCopy(_data, _position, destination, 0, lenght);
            _position += lenght;
        }

        public void Clear()
        {
            _position = 0;
            _dataSize = 0;
            _data = null;
        }
    }
}
