﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Phantasma.Core.Domain;
using Phantasma.Shared;
using Phantasma.Shared.Utils;

namespace Phantasma.Core.Storage
{
    public interface IKeyValueStoreAdapter
    {
        void SetValue(byte[] key, byte[] value);
        byte[] GetValue(byte[] key);
        bool ContainsKey(byte[] key);
        void Remove(byte[] key);
        uint Count { get; }
        void Visit(Action<byte[], byte[]> visitor, ulong searchCount, byte[] prefix);
    }

    public class MemoryStore : IKeyValueStoreAdapter
    {
        private Dictionary<byte[], byte[]> _entries = new Dictionary<byte[], byte[]>(new ByteArrayComparer());

        public uint Count => (uint)_entries.Count;

        public MemoryStore()
        {
        }

        public void SetValue(byte[] key, byte[] value)
        {
            if (value == null || value.Length == 0)
            {
                Remove(key);
                return;
            }

            _entries[key] = value;
        }

        public byte[] GetValue(byte[] key)
        {
            if (ContainsKey(key))
            {
                return _entries[key];
            }

            return null;
        }

        public bool ContainsKey(byte[] key)
        {
            var result = _entries.ContainsKey(key);
            return result;
        }

        public void Remove(byte[] key)
        {
            _entries.Remove(key);
        }

        public void Visit(Action<byte[], byte[]> visitor, ulong searchCount, byte[] prefix)
        {
            ulong count = 0;
            foreach(var entry in _entries)
            {
                var entryPrefix = entry.Key.Take(prefix.Length);
                if (count <= searchCount && entryPrefix.SequenceEqual(prefix))
                {
                    visitor(entry.Key, entry.Value);
                    count++;
                }

                if (count > searchCount)
                    break;
            }
        }
    }

    public class BasicDiskStore : IKeyValueStoreAdapter
    {
        private Dictionary<byte[], byte[]> _cache = new Dictionary<byte[], byte[]>(new ByteArrayComparer());

        public uint Count => (uint)_cache.Count;

        private string fileName;

        public bool AutoFlush = true;

        public BasicDiskStore(string fileName)
        {
            this.fileName = fileName.Replace("\\", "/");

            var path = Path.GetDirectoryName(fileName);
            if (!string.IsNullOrEmpty(path) && !Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            if (File.Exists(fileName))
            {
                var lines = File.ReadAllLines(fileName);
                lock (_cache)
                {
                    foreach (var line in lines)
                    {
                        var temp = line.Split(',');
                        var key = Convert.FromBase64String(temp[0]);
                        var val = Convert.FromBase64String(temp[1]);
                        _cache[key] = val;
                    }
                }
            }
        }

        public void Visit(Action<byte[], byte[]> visitor, ulong searchCount, byte[] prefix)
        {
            lock (_cache)
            {
                //TODO use prefix
                foreach (var entry in _cache)
                {
                    visitor(entry.Key, entry.Value);
                }
            }
        }

        private void UpdateToDisk()
        {
            File.WriteAllLines(fileName, _cache.Select(x => Convert.ToBase64String(x.Key) + "," + Convert.ToBase64String(x.Value)));
        }

        public void SetValue(byte[] key, byte[] value)
        {
            Throw.IfNull(key, nameof(key));

            if (value == null || value.Length == 0)
            {
                Remove(key);
            }
            else
            {
                lock (_cache)
                {
                    _cache[key] = value;
                    if (AutoFlush)
                    {
                        UpdateToDisk();
                    }
                }
            }
        }

        public byte[] GetValue(byte[] key)
        {
            if (ContainsKey(key))
            {
                lock (_cache)
                {
                    return _cache[key];
                }
            }

            return null;
        }

        public bool ContainsKey(byte[] key)
        {
            lock (_cache)
            {
                var result = _cache.ContainsKey(key);
                return result;
            }
        }

        public void Remove(byte[] key)
        {
            lock (_cache)
            {
                _cache.Remove(key);
                if (AutoFlush)
                {
                    UpdateToDisk();
                }
            }
        }

        public void Flush()
        {
            if (!AutoFlush)
            {
                UpdateToDisk();
            }
        }
    }

    public class KeyValueStore<K, V>
    {
        public readonly string Name;

        public readonly IKeyValueStoreAdapter Adapter;

        public uint Count => Adapter.Count;

        public KeyValueStore()
        {
        }

        public KeyValueStore(IKeyValueStoreAdapter adapter)
        {
            Adapter = adapter;
        }

        public V this[K key]
        {
            get { return Get(key); }
            set { Set(key, value); }
        }

        public void Set(K key, V value)
        {
            var keyBytes = Serialization.Serialize(key);
            var valBytes = Serialization.Serialize(value);
            Adapter.SetValue(keyBytes, valBytes);
        }

        public bool TryGet(K key, out V value)
        {
            var keyBytes = Serialization.Serialize(key);
            var bytes = Adapter.GetValue(keyBytes);
            if (bytes == null)
            {
                value = default(V);
               return false;
            }
            value = Serialization.Unserialize<V>(bytes);
            return true;
        }

        public V Get(K key)
        {
            var keyBytes = Serialization.Serialize(key);
            var bytes = Adapter.GetValue(keyBytes);
            if (bytes == null)
            {
                Throw.If(bytes == null, "item not found in keystore");

            }
            return Serialization.Unserialize<V>(bytes);
        }

        public bool ContainsKey(K key)
        {
            var keyBytes = Serialization.Serialize(key);
            return Adapter.ContainsKey(keyBytes);
        }

        public void Remove(K key)
        {
            var keyBytes = Serialization.Serialize(key);
            Adapter.Remove(keyBytes);
        }

        public void Visit(Action<K, V> visitor, ulong searchCount = 0, byte[] prefix = null)
        {
            Adapter.Visit((keyBytes, valBytes) =>
            {
                var key = Serialization.Unserialize<K>(keyBytes);
                var val = Serialization.Unserialize<V>(valBytes);
                visitor(key, val);
            }, searchCount, prefix);
        }
    }
}
