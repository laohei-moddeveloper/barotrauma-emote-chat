using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace EmoteChat
{
    // Four-chunk window: bounded memory, explicit offsets, retry-safe, no reliance on packet ordering.
    public sealed class Session : IDisposable
    {
        public const byte Hello = 1, Welcome = 2, Offer = 3, Ack = 4, Chunk = 5, Complete = 6, Get = 7, Reject = 8;
        private static readonly SemaphoreSlim DecodeGate = new SemaphoreSlim(1, 1);
        private readonly Action<byte[]> send;
        private readonly bool server;
        private readonly AssetCache cache;
        private readonly Bucket incoming, outgoing, offers, packets;
        private AssemblyBuffer rx;
        private Task<Asset> decoding;
        private Asset tx;
        private int txOffset = -1, sentOffset;
        private double txStarted, lastSend = -10, lastWelcome = -10, lastProgress;
        private bool disposed;
        public bool Ready { get; private set; }
        public Func<string, bool> AllowOffer = _ => false;
        public Func<string, bool> AllowGet = _ => false;
        public Action<string> Sent, Received;
        public Action<string> Error;
        public bool Busy => tx != null;
        public bool Receiving => rx != null || decoding != null;
        public int Progress => tx == null || txOffset < 0 ? 0 : txOffset * 100 / tx.Encoded.Length;
        public Session(bool server, bool pixels, AssetCache cache, Action<byte[]> send, double now)
        {
            this.server = server; WantPixels = pixels; this.cache = cache; this.send = send;
            incoming = new Bucket(16384, 32768, now); outgoing = new Bucket(8192, 1024, now); offers = new Bucket(0.1, 2, now);
            packets = new Bucket(32, 16, now);
        }
        private readonly bool WantPixels;
        public void Upload(Asset asset, double now)
        {
            if (disposed || tx != null) throw new InvalidOperationException("Transfer busy");
            tx = asset; txOffset = -1; sentOffset = 0; txStarted = now; lastProgress = now; lastSend = -10;
        }
        public void Request(string hash) { if (Ready && Format.IsHash(hash)) Write(Get, hash); }
        public void Tick(double now)
        {
            if (disposed) return;
            if (decoding != null && decoding.IsCompleted)
            {
                var task = decoding; decoding = null;
                if (task.Status == TaskStatus.RanToCompletion)
                {
                    var asset = task.Result; cache.Put(asset); rx = null;
                    Write(Complete, asset.Hash); Received?.Invoke(asset.Hash);
                }
                else { _ = task.Exception; string hash = rx.Hash; rx = null; Write(Reject, hash); Error?.Invoke("Invalid image data"); }
            }
            if (rx != null && decoding == null && now - rx.Started > 90) rx = null;
            if (!Ready)
            {
                if (!server && now - lastSend > 3) { Write(Hello); lastSend = now; }
                if (tx != null && now - txStarted > 15) { tx = null; Error?.Invoke("Server does not answer Emote Chat handshake"); }
                return;
            }
            if (tx == null) return;
            if (now - txStarted > 90) { tx = null; Error?.Invoke("Transfer timeout"); return; }
            if (now - lastSend < 0.085) return;
            bool offer = txOffset < 0;
            if (offer && now - lastSend < 2) return;
            if (!offer && sentOffset >= Math.Min(tx.Encoded.Length, txOffset + 4 * 768))
            {
                if (now - lastProgress < 2) return;
                if (sentOffset == tx.Encoded.Length) offer = true; // repeat receipt request
                else sentOffset = txOffset;
            }
            int size = offer ? 48 : Math.Min(768, tx.Encoded.Length - sentOffset) + 48;
            if (!outgoing.Take(size, now)) return;
            if (offer) Write(Offer, tx.Hash, tx.Encoded.Length);
            else
            {
                int count = Math.Min(768, tx.Encoded.Length - sentOffset);
                Write(Chunk, tx.Hash, sentOffset, tx.Encoded, sentOffset, count); sentOffset += count;
            }
            lastSend = now;
        }
        public void Receive(byte[] packet, double now)
        {
            if (disposed || packet == null || packet.Length < 1 || packet.Length > 810 || !packets.Take(1, now) || !incoming.Take(packet.Length + 16, now)) return;
            using (var stream = new MemoryStream(packet, false))
            using (var reader = new BinaryReader(stream))
            {
                byte kind = reader.ReadByte();
                if (kind == Hello || kind == Welcome)
                {
                    if (stream.Length != 2 || reader.ReadByte() != 1) return;
                    if (server && kind == Hello && now - lastWelcome >= 1) { Ready = true; Write(Welcome); lastWelcome = now; }
                    else if (!server && kind == Welcome) Ready = true;
                    return;
                }
                if (!Ready || packet.Length < 33) return;
                byte[] digest = reader.ReadBytes(32);
                string hash = Convert.ToHexString(digest).ToLowerInvariant();
                switch (kind)
                {
                    case Offer:
                        if (packet.Length != 37) return;
                        int size = reader.ReadInt32();
                        if (size < 16 || size > Format.MaxFile || !AllowOffer(hash)) { Write(Reject, hash); return; }
                        var existing = cache.Get(hash);
                        if (existing != null && existing.Encoded.Length == size) { Write(Complete, hash); Received?.Invoke(hash); return; }
                        if (rx != null)
                        {
                            if (rx.Hash == hash && rx.Data.Length == size && decoding == null) Write(Ack, hash, rx.Offset);
                            return;
                        }
                        if (!offers.Take(1, now)) { Write(Reject, hash); return; }
                        rx = new AssemblyBuffer(hash, size, now); Write(Ack, hash, 0); break;
                    case Chunk:
                        if (packet.Length < 38 || rx == null || rx.Hash != hash || decoding != null) return;
                        int offset = reader.ReadInt32();
                        byte[] chunk = reader.ReadBytes(packet.Length - 37);
                        bool done = rx.Append(offset, chunk);
                        if (done)
                        {
                            byte[] encoded = rx.Data;
                            decoding = Task.Run(async () =>
                            {
                                await DecodeGate.WaitAsync().ConfigureAwait(false);
                                try { return Format.Decode(encoded, hash, WantPixels); }
                                finally { DecodeGate.Release(); }
                            });
                        }
                        else Write(Ack, hash, rx.Offset);
                        break;
                    case Ack:
                        if (packet.Length != 37 || tx == null || tx.Hash != hash) return;
                        int next = reader.ReadInt32();
                        if ((txOffset < 0 && next == 0) || (next > txOffset && next <= sentOffset && (next % 768 == 0 || next == tx.Encoded.Length)))
                        { txOffset = next; lastProgress = now; }
                        break;
                    case Complete:
                        if (packet.Length != 33 || tx == null || tx.Hash != hash) return;
                        tx = null; Sent?.Invoke(hash); break;
                    case Get:
                        if (packet.Length != 33 || tx != null || !AllowGet(hash)) return;
                        Asset asset = cache.Get(hash); if (asset != null) Upload(asset, now); else Write(Reject, hash);
                        break;
                    case Reject:
                        if (packet.Length != 33) return;
                        if (tx?.Hash == hash) { tx = null; Error?.Invoke("Transfer rejected"); }
                        break;
                }
            }
        }
        private void Write(byte kind, string hash = null, int value = 0, byte[] data = null, int offset = 0, int count = 0)
        {
            using (var memory = new MemoryStream(810))
            using (var writer = new BinaryWriter(memory))
            {
                writer.Write(kind);
                if (hash == null) writer.Write((byte)1);
                else
                {
                    writer.Write(Convert.FromHexString(hash));
                    if (kind == Offer || kind == Ack || kind == Chunk) writer.Write(value);
                    if (data != null) writer.Write(data, offset, count);
                }
                send(memory.ToArray());
            }
        }
        public void Dispose()
        {
            disposed = true; tx = null; rx = null;
            // Observe possible worker faults after unload; completed pixels are never published to the game.
            if (decoding != null) decoding.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
            decoding = null;
        }
    }
}
