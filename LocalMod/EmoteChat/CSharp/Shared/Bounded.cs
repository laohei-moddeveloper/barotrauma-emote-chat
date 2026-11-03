using System;
using System.Collections.Generic;
using System.IO;

namespace EmoteChat
{
    public sealed class Bucket
    {
        private readonly double rate, capacity;
        private double tokens, last;
        public Bucket(double rate, double capacity, double now)
        { this.rate = rate; this.capacity = capacity; tokens = capacity; last = now; }
        public bool Take(int amount, double now)
        {
            tokens = Math.Min(capacity, tokens + Math.Max(0, now - last) * rate); last = Math.Max(last, now);
            if (amount < 0 || amount > tokens) return false;
            tokens -= amount; return true;
        }
    }
    public sealed class AssemblyBuffer
    {
        public readonly string Hash;
        public readonly byte[] Data;
        public int Offset { get; private set; }
        public readonly double Started;
        public AssemblyBuffer(string hash, int size, double now)
        {
            if (!Format.IsHash(hash) || size < 16 || size > Format.MaxFile) throw new InvalidDataException("Invalid offer");
            Hash = hash; Data = new byte[size]; Started = now;
        }
        public bool Append(int offset, byte[] bytes)
        {
            if (bytes == null || bytes.Length < 1 || bytes.Length > 768 || offset < 0 || offset > Data.Length - bytes.Length)
                throw new InvalidDataException("Invalid chunk");
            // Duplicate/out-of-order chunks never mutate state. Sender retries from acknowledged offset.
            if (offset != Offset) return false;
            Buffer.BlockCopy(bytes, 0, Data, Offset, bytes.Length); Offset += bytes.Length;
            return Offset == Data.Length;
        }
    }
    public sealed class AssetCache
    {
        private readonly int maxBytes, maxCount;
        private int bytes;
        private readonly Dictionary<string, LinkedListNode<Asset>> entries = new Dictionary<string, LinkedListNode<Asset>>();
        private readonly LinkedList<Asset> lru = new LinkedList<Asset>();
        public Action<string> Evicted;
        public int Bytes => bytes;
        public int Count => entries.Count;
        public AssetCache(int maxBytes, int maxCount) { this.maxBytes = maxBytes; this.maxCount = maxCount; }
        public Asset Get(string hash)
        {
            if (hash == null || !entries.TryGetValue(hash, out var node)) return null;
            lru.Remove(node); lru.AddFirst(node); return node.Value;
        }
        public void Put(Asset asset)
        {
            if (asset.Cost > maxBytes) throw new InvalidDataException("Cache entry too large");
            if (entries.ContainsKey(asset.Hash)) return;
            while (bytes + asset.Cost > maxBytes || entries.Count >= maxCount) RemoveLast();
            entries.Add(asset.Hash, lru.AddFirst(asset)); bytes += asset.Cost;
        }
        private void RemoveLast()
        {
            var value = lru.Last.Value; lru.RemoveLast(); entries.Remove(value.Hash); bytes -= value.Cost; Evicted?.Invoke(value.Hash);
        }
        public void Clear() { while (lru.Count > 0) RemoveLast(); }
    }
}
