using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using EmoteChat;

static class DistributionTests
{
    public static int Run(Asset asset)
    {
        const int clients = 16;
        double now = 0, uploadedAt = -1;
        long serverBytes = 0, sourcePixelBytes = 0;
        int received = 0;
        bool published = false;
        var wire = new Queue<(Session target, byte[] data)>();
        var all = new List<Session>();
        var serverCache = new AssetCache(8 * 1024 * 1024, 64);
        var outgoing = new Bucket(TransferLimits.GlobalBytesPerSecond, TransferLimits.GlobalBurstBytes, 0);
        var receivers = new List<Session>();
        bool sendServer(Session target, byte[] packet)
        {
            if (!outgoing.Take(packet.Length + 32, now)) return false;
            serverBytes += packet.Length + 32; wire.Enqueue((target, packet)); return true;
        }
        Session source = null, sourceServer = null;
        source = new Session(false, true, new AssetCache(24 * 1024 * 1024, 32), packet =>
        {
            if (packet[0] == Session.Chunk) sourcePixelBytes += packet.Length - 37;
            wire.Enqueue((sourceServer, packet));
        }, 0);
        sourceServer = new Session(true, false, serverCache, (Func<byte[], bool>)(p => sendServer(source, p)), 0);
        sourceServer.AllowOffer = _ => true;
        sourceServer.CanSend = length => outgoing.Available(length + 32, now);
        all.Add(source); all.Add(sourceServer);
        for (int i = 0; i < clients; i++)
        {
            Session receiver = null, server = null;
            receiver = new Session(false, true, new AssetCache(24 * 1024 * 1024, 32), p => wire.Enqueue((server, p)), 0);
            server = new Session(true, false, serverCache, (Func<byte[], bool>)(p => sendServer(receiver, p)), 0);
            server.CanSend = length => outgoing.Available(length + 32, now);
            server.AllowGet = _ => published;
            receiver.AllowOffer = _ => published;
            receiver.Received = _ => received++;
            receivers.Add(receiver); all.Add(receiver); all.Add(server);
        }
        source.Sent = hash => { published = true; uploadedAt = now; foreach (var client in receivers) client.Request(hash); };
        source.Upload(asset, 0);
        int turn = 0;
        try
        {
            while (now < 80 && received < clients)
            {
                for (int i = 0; i < all.Count; i++) all[(i + turn) % all.Count].Tick(now);
                turn = (turn + 1) % all.Count;
                int packets = wire.Count;
                while (packets-- > 0) { var packet = wire.Dequeue(); packet.target.Receive(packet.data, now); }
                if (all.Any(s => s.Receiving)) Thread.Sleep(1);
                now += 1.0 / 60;
            }
            if (received != clients || uploadedAt > 3.5 || now > 46) throw new Exception($"First-send fanout regression: {received}/{clients}, upload={uploadedAt:F2}s, all={now:F2}s");
            if (sourcePixelBytes != asset.Encoded.Length) throw new Exception("Source re-uploaded data for each recipient");
            if (serverCache.Count != 1 || serverCache.Get(asset.Hash).Pixels != null) throw new Exception("Server duplicated/decompressed animation storage");
            if (serverBytes > now * TransferLimits.GlobalBytesPerSecond + TransferLimits.GlobalBurstBytes) throw new Exception("Server total bandwidth cap exceeded");
            Console.WriteLine($"16 uncached recipients: uploader {uploadedAt:F2}s, all recipients {now:F2}s, {serverBytes} server bytes; one compressed server asset, 128 KiB/s aggregate cap");
        }
        finally { foreach (var session in all) session.Dispose(); }

        using var congested = new Session(true, false, serverCache, p => { }, 0);
        congested.Receive(new byte[] { Session.Hello, 2 }, 0);
        congested.CanSend = _ => false; congested.Upload(asset, 0);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++) congested.Tick(0.01 * i);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        if (allocated != 0) throw new Exception("Congested server allocated unsendable packets: " + allocated);
        Console.WriteLine("10,000 congested server ticks: zero managed packet allocations");
        return 6;
    }
}
