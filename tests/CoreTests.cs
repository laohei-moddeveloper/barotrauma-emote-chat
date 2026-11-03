using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using EmoteChat;

static class Tests
{
    static int checks;
    static void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks++; }
    static void Bad(Action action, string name)
    { try { action(); } catch (Exception e) when (e is IOException || e is InvalidDataException || e is ArgumentException) { checks++; return; } throw new Exception("Accepted: " + name); }
    static byte[] Image(int w = 16, int h = 16, int count = 2, int extra = 0, bool random = false)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
        {
            writer.Write(0x31504345); writer.Write((ushort)w); writer.Write((ushort)h); writer.Write((ushort)count); writer.Write((ushort)0);
            for (int i = 0; i < count; i++) writer.Write((ushort)100);
        }
        using (var deflate = new DeflateStream(stream, CompressionLevel.Fastest, true))
        {
            byte[] raw = new byte[w * h * count * 4 + extra];
            if (random) new Random(42).NextBytes(raw);
            deflate.Write(raw);
        }
        return stream.ToArray();
    }
    static Asset Decode(byte[] data, bool pixels = true) => Format.Decode(data, Format.Hash(data), pixels);
    static byte[] Control(byte kind, string hash, int? value = null)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(kind); writer.Write(Convert.FromHexString(hash)); if (value.HasValue) writer.Write(value.Value); return stream.ToArray();
    }
    static void Protocol(double latency)
    {
        var sent = new Queue<(bool toServer, byte[] packet)>();
        var serverCache = new AssetCache(8 * 1024 * 1024, 64);
        var clientCache = new AssetCache(24 * 1024 * 1024, 32);
        using var client = new Session(false, true, clientCache, p => sent.Enqueue((true, p)), 0);
        using var server = new Session(true, false, serverCache, p => sent.Enqueue((false, p)), 0);
        int received = 0, complete = 0;
        server.AllowOffer = _ => true; server.AllowGet = _ => false;
        server.Received = _ => received++;
        client.Sent = _ => complete++;
        var data = Decode(Image(96, 96, 5, random: true));
        clientCache.Put(data); client.Upload(data, 0);
        var wire = new List<(double at, bool toServer, byte[] packet)>();
        double now = 0;
        int packets = 0, dropped = 0;
        while (now < 89 && complete == 0)
        {
            client.Tick(now); server.Tick(now);
            int queued = sent.Count;
            for (int i = 0; i < queued; i++)
            {
                var delivery = sent.Dequeue(); packets++;
                // Realistic loss plus duplicate deliveries. Stop-and-wait makes reordering harmless.
                if (packets % 53 == 0) { dropped++; continue; }
                wire.Add((now + latency, delivery.toServer, delivery.packet));
                if (packets % 29 == 0) wire.Add((now + latency + 0.04, delivery.toServer, delivery.packet));
            }
            foreach (var delivery in wire.Where(p => p.at <= now).ToArray())
            { (delivery.toServer ? server : client).Receive(delivery.packet, now); wire.Remove(delivery); }
            if (server.Receiving) Thread.Sleep(1);
            now += 0.02;
        }
        Check(complete == 1 && received >= 1, "lossy upload completes");
        Check(serverCache.Get(data.Hash)?.Pixels == null, "server stores no decoded pixels");
        Check(serverCache.Get(data.Hash).Encoded.SequenceEqual(data.Encoded), "content survives transport");
        Check(dropped > 0 && now > 10, "loss/retry exercised under rate limit");
        sent.Clear();
        server.Receive(Control(Session.Get, data.Hash), now);
        Check(!server.Busy && sent.Count == 0, "unauthorized resource request rejected");
        server.AllowGet = _ => true;
        server.Receive(Control(Session.Get, data.Hash), now);
        Check(server.Busy, "authorized recipient can download");
        client.AllowOffer = _ => false;
        clientCache.Clear();
        client.Receive(Control(Session.Offer, data.Hash, data.Encoded.Length), now);
        Check(!client.Receiving, "unsolicited server asset rejected");
        Console.WriteLine($"Lossy transfer (RTT {latency * 2000:F0}ms): {data.Encoded.Length} bytes, {now:F2}s simulated, {packets} packets, {dropped} drops");
        server.Dispose(); client.Dispose();
        Check(!client.Receiving && !server.Receiving, "dispose drops incoming state");
    }
    static void Probe(string root, string game)
    {
        AssemblyLoadContext.Default.Resolving += (ctx, name) =>
        {
            string path = Path.Combine(root, name.Name + ".dll");
            return File.Exists(path) ? ctx.LoadFromAssemblyPath(path) : null;
        };
        var asm = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(root, game + ".dll"));
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        Type setup = asm.GetType("Barotrauma.LuaCsSetup", true);
        Check(setup.GetProperty("Instance", flags) != null, "LuaCs instance");
        var networking = setup.GetProperty("Networking", flags) ?? setup.GetProperty("NetworkingService", flags);
        Check(networking != null, "networking property");
        var netType = asm.GetTypes().First(t => t.Name == "NetworkingService");
        foreach (string method in new[] { "Start", "Send", "Receive" })
            Check(netType.GetMethods(flags).Any(m => m.Name == method), "networking " + method);
        Check(asm.GetType("Barotrauma.CoroutineManager").GetMethod("Update", flags, new[] { typeof(bool), typeof(float) }) != null, "pump signature");
        if (game == "Barotrauma")
        {
            Check(asm.GetType("Barotrauma.ChatBox").GetField("chatBox", flags) != null, "chat layout field");
            Check(asm.GetType("Barotrauma.Character").GetMethods(flags).Any(m => m.Name == "DrawGUIMessages"), "bubble hook");
        }
        Console.WriteLine($"Runtime API probe: {asm.GetName()}, {checks} checks (no game boot/render)");
    }
    static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--probe") { Probe(Path.GetFullPath(args[1]), args[2]); return; }
        var encoded = Image(); var asset = Decode(encoded);
        Check(asset.Pixels.Length == 2048 && asset.Frame(0.15) == 1 && asset.Frame(0.2) == 0, "decode and animation timing");
        Check(Format.ParseMarker(Format.Marker(asset.Hash)) == asset.Hash, "strict marker roundtrip");
        Check(Format.ParseMarker("x" + Format.Marker(asset.Hash)) == null, "embedded markers rejected");
        Bad(() => Format.Decode(encoded, new string('0', 64), true), "hash mismatch");
        Bad(() => Decode(Image(129, 1, 1)), "dimension bound");
        Bad(() => Decode(Image(1, 1, 49)), "frame count bound");
        Bad(() => Decode(Image(extra: 1)), "decompression bomb / excess pixels");
        Bad(() => Decode(encoded.Take(13).ToArray()), "truncated header");
        var shortData = Image(); shortData[12] = 0;
        Bad(() => Decode(shortData), "invalid delay");
        var receive = new AssemblyBuffer(asset.Hash, encoded.Length, 0);
        Check(!receive.Append(1, new byte[] { 1 }), "out-of-order offset ignored");
        Check(receive.Offset == 0, "out-of-order offset cannot advance state");
        Check(receive.Append(0, encoded), "single chunk assembly");
        Check(!receive.Append(0, encoded) && receive.Offset == encoded.Length, "duplicate idempotency");
        Bad(() => new AssemblyBuffer(asset.Hash, Format.MaxFile + 1, 0), "oversized allocation");
        Bad(() => receive.Append(int.MaxValue, new byte[] { 1 }), "offset overflow");
        var cache = new AssetCache(asset.Cost * 2, 2); int evicted = 0; cache.Evicted = _ => evicted++;
        cache.Put(asset); var a2 = Decode(Image(8, 8)); var a3 = Decode(Image(4, 4)); cache.Put(a2); cache.Get(asset.Hash); cache.Put(a3);
        Check(cache.Get(a2.Hash) == null && evicted == 1 && cache.Bytes <= asset.Cost * 2, "LRU and memory budget");
        var bucket = new Bucket(10, 20, 0);
        Check(bucket.Take(20, 0) && !bucket.Take(1, 0) && bucket.Take(10, 1) && !bucket.Take(1, 0.5), "token bucket and clock rollback");
        foreach (string path in args) { var fixture = File.ReadAllBytes(path); Check(Decode(fixture).Pixels.Length > 0, "Python importer interoperability " + path); }
        Protocol(0); Protocol(0.4);
        var worst = Image(128, 128, 48);
        var watchDecode = Stopwatch.StartNew();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++) Decode(worst, false);
        long serverBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Console.WriteLine($"100 maximal pixel-count server validations: {watchDecode.ElapsedMilliseconds}ms, {serverBytes / 100} managed bytes/asset (no full-frame allocation)");
        Check(serverBytes / 100 < 65536, "server validation keeps allocation bounded");
        // Adversarial parser corpus: protocol parsing must not cause unbounded allocations.
        var rng = new Random(88); var watch = Stopwatch.StartNew();
        for (int i = 0; i < 10000; i++)
        {
            var fuzz = new byte[rng.Next(16, 1024)]; rng.NextBytes(fuzz);
            try { Decode(fuzz); } catch (Exception e) when (e is IOException || e is InvalidDataException || e is ArgumentException) { }
        }
        for (int i = 0; i < 1000; i++)
        {
            var fuzz = new byte[rng.Next(16, 1024)]; rng.NextBytes(fuzz);
            using (var memory = new MemoryStream(fuzz)) using (var writer = new BinaryWriter(memory))
            { writer.Write(0x31504345); writer.Write((ushort)rng.Next(1, 160)); writer.Write((ushort)rng.Next(1, 160)); writer.Write((ushort)1); writer.Write((ushort)0); writer.Write((ushort)100); }
            try { Decode(fuzz); } catch (Exception e) when (e is IOException || e is InvalidDataException || e is ArgumentException) { }
        }
        Console.WriteLine($"PASS {checks} checks; 11000 malformed/structured format inputs processed in {watch.ElapsedMilliseconds}ms");
    }
}
