using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Runtime.CompilerServices;
using System.Threading;
using EmoteChat;

static class Tests
{
    static int checks;
    static float layoutScale = 1;
    static bool LayoutScalePrefix(ref float __result) { __result = layoutScale; return false; }
    static bool WidthPrefix(ref int __result) { __result = 1920; return false; }
    static bool HeightPrefix(ref int __result) { __result = 1080; return false; }
    static bool SkipFontPrefix() => false;
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
        client.Error = e => Console.WriteLine("Client transfer error: " + e);
        server.Error = e => Console.WriteLine("Server transfer error: " + e);
        var data = Decode(Image(96, 96, 7, random: true));
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
                // Loss, duplicates and delayed chunks exercise bounded window recovery.
                if (packets % 53 == 0) { dropped++; continue; }
                wire.Add((now + latency + (packets % 97 == 0 ? 0.25 : 0), delivery.toServer, delivery.packet));
                if (packets % 29 == 0) wire.Add((now + latency + 0.04, delivery.toServer, delivery.packet));
            }
            foreach (var delivery in wire.Where(p => p.at <= now).ToArray())
            { (delivery.toServer ? server : client).Receive(delivery.packet, now); wire.Remove(delivery); }
            if (server.Receiving) Thread.Sleep(1);
            now += 0.02;
        }
        if (complete != 1)
        {
            const BindingFlags dbg = BindingFlags.Instance | BindingFlags.NonPublic;
            foreach (string field in new[] { "txOffset", "sentOffset", "highWater", "lastProgress" }) Console.WriteLine(field + "=" + typeof(Session).GetField(field, dbg).GetValue(client));
            var buffer = (AssemblyBuffer)typeof(Session).GetField("rx", dbg).GetValue(server);
            Console.WriteLine($"RTT={latency * 2000} now={now} received={buffer?.Offset} total={data.Encoded.Length}");
        }
        Check(complete == 1 && received >= 1, "lossy upload completes");
        Check(serverCache.Get(data.Hash)?.Pixels == null, "server stores no decoded pixels");
        Check(serverCache.Get(data.Hash).Encoded.SequenceEqual(data.Encoded), "content survives transport");
        Check(dropped > 0 && now >= data.Encoded.Length / (double)TransferLimits.PeerBytesPerSecond - 0.2, "loss/retry exercised under rate limit");
        Check(now < (latency == 0 ? 16 : 35), "large first transfer latency regression");
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
    static void Probe(string root, string game, string pluginPath)
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
        if (pluginPath != null)
        {
            var plugin = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(pluginPath));
            Type hooks = plugin.GetType("EmoteChat.Plugin", true);
            var harmonyAssembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(root, "0Harmony.dll"));
            Type harmonyType = harmonyAssembly.GetType("HarmonyLib.Harmony", true);
            Type harmonyMethodType = harmonyAssembly.GetType("HarmonyLib.HarmonyMethod", true);
            object harmony = Activator.CreateInstance(harmonyType, "emotechat.isolated.patch.smoke");
            var patchMethods = harmonyType.GetMethods().Where(m => m.Name == "Patch" && m.GetParameters().Length > 1 && m.GetParameters()[1].ParameterType == harmonyMethodType).ToArray();
            var patch = patchMethods.OrderBy(m => m.GetParameters().Length).First();
            var targets = new List<(MethodInfo target, string hook, bool prefix)>();
            targets.Add((asm.GetType("Barotrauma.CoroutineManager").GetMethod("Update", flags, new[] { typeof(bool), typeof(float) }), "UpdatePostfix", false));
            if (game == "Barotrauma")
            {
                var chatMethod = asm.GetType("Barotrauma.ChatBox").GetMethods(flags).Single(m => m.Name == "AddMessage");
                targets.Add((chatMethod, "ChatPrefix", true));
                targets.Add((chatMethod, "ChatPostfix", false));
                var lobbyMethod = asm.GetType("Barotrauma.NetLobbyScreen").GetMethods(flags).Single(m => m.Name == "NewChatMessage");
                targets.Add((lobbyMethod, "LobbyPrefix", true)); targets.Add((lobbyMethod, "LobbyPostfix", false));
                targets.Add((asm.GetType("Barotrauma.Character").GetMethods(flags).Single(m => m.Name == "ShowSpeechBubble"), "SpeechPrefix", true));
                targets.Add((asm.GetType("Barotrauma.Character").GetMethods(flags).Single(m => m.Name == "DrawGUIMessages"), "BubblePostfix", false));
                targets.Add((asm.GetType("Barotrauma.GameMain").GetMethods(flags).Single(m => m.Name == "Draw" && m.DeclaringType == asm.GetType("Barotrauma.GameMain")), "RenderPrefix", true));
            }
            else
            {
                var direct = asm.GetType("Barotrauma.Networking.GameServer").GetMethods(flags).Single(m => m.Name == "SendDirectChatMessage" && m.GetParameters()[0].ParameterType.Name == "ChatMessage");
                targets.Add((direct, "DirectChatPrefix", true)); targets.Add((direct, "DirectChatPostfix", false));
            }
            try
            {
                foreach (var item in targets)
                {
                    var args = new object[patch.GetParameters().Length]; args[0] = item.target;
                    int index = Array.FindIndex(patch.GetParameters(), p => p.Name == (item.prefix ? "prefix" : "postfix"));
                    args[index] = Activator.CreateInstance(harmonyMethodType, hooks.GetMethod(item.hook, flags));
                    patch.Invoke(harmony, args);
                    Check(harmonyType.GetMethod("GetPatchInfo").Invoke(null, new object[] { item.target }) != null, "actual runtime Harmony patch " + item.hook);
                }
            }
            finally { harmonyType.GetMethod("UnpatchSelf").Invoke(harmony, null); }
            if (game == "Barotrauma")
            {
                // Execute the real sanitizing hooks with native type instances, without starting the game.
                object uninitialized(Type t) => RuntimeHelpers.GetUninitializedObject(t);
                Type rectType = asm.GetType("Barotrauma.RectTransform"), componentType = asm.GetType("Barotrauma.GUIComponent");
                object rect = uninitialized(rectType);
                var childrenField = rectType.GetField("children", flags);
                childrenField.SetValue(rect, Activator.CreateInstance(childrenField.FieldType));
                object content = uninitialized(asm.GetType("Barotrauma.GUIFrame"));
                var rectField = componentType.GetFields(flags).Single(f => f.FieldType == rectType);
                rectField.SetValue(content, rect);
                object list = uninitialized(asm.GetType("Barotrauma.GUIListBox"));
                var contentProperty = list.GetType().GetProperty("Content", flags);
                contentProperty.SetValue(list, content);
                object pluginInstance = Activator.CreateInstance(hooks);
                hooks.GetField("Active", flags).SetValue(null, pluginInstance);
                try
                {
                    Type messageType = asm.GetType("Barotrauma.Networking.ChatMessage");
                    object message = uninitialized(messageType);
                    string marker = Format.Marker(new string('a', 64));
                    var textField = messageType.GetField("Text", flags);
                    foreach (var pair in new[] { ("Barotrauma.ChatBox", "ChatPrefix"), ("Barotrauma.NetLobbyScreen", "LobbyPrefix") })
                    {
                        object box = uninitialized(asm.GetType(pair.Item1));
                        box.GetType().GetField("chatBox", flags).SetValue(box, list);
                        if (pair.Item2 == "ChatPrefix")
                        {
                            var popupsField = box.GetType().GetField("popupMessages", flags);
                            popupsField.SetValue(box, Activator.CreateInstance(popupsField.FieldType));
                        }
                        textField.SetValue(message, marker);
                        messageType.GetField("translatedText", flags).SetValue(message, marker);
                        object[] inputs = { box, message, true, null };
                        hooks.GetMethod(pair.Item2, flags).Invoke(null, inputs);
                        Check((string)textField.GetValue(message) == "", pair.Item2 + " strips token before native text measurement");
                        Check(messageType.GetField("translatedText", flags).GetValue(message) == null, "cached translated token cannot leak into native popup");
                        hooks.GetMethod("ChatFinalizer", flags).Invoke(null, new object[] { null, message, inputs[3] });
                        Check((string)textField.GetValue(message) == marker, "sanitization does not mutate shared network metadata");
                    }
                    Check(!(bool)hooks.GetMethod("SpeechPrefix", flags).Invoke(null, new object[] { marker }), "native overhead text does not show transport ID");
                    Check((bool)hooks.GetMethod("SpeechPrefix", flags).Invoke(null, new object[] { "normal chat" }), "ordinary speech unaffected");

                    // Test-only font/scale inputs let real native RectTransform/GUILayoutGroup run without game assets.
                    var layoutHarmony = Activator.CreateInstance(harmonyType, "emotechat.isolated.layout.inputs");
                    void inputPatch(MethodInfo target, string prefix)
                    {
                        var pa = new object[patch.GetParameters().Length]; pa[0] = target;
                        pa[Array.FindIndex(patch.GetParameters(), p => p.Name == "prefix")] = Activator.CreateInstance(harmonyMethodType, typeof(Tests).GetMethod(prefix, flags));
                        patch.Invoke(layoutHarmony, pa);
                    }
                    inputPatch(asm.GetType("Barotrauma.GUI").GetProperty("Scale", flags).GetMethod, nameof(LayoutScalePrefix));
                    inputPatch(asm.GetType("Barotrauma.GameMain").GetProperty("GraphicsWidth", flags).GetMethod, nameof(WidthPrefix));
                    inputPatch(asm.GetType("Barotrauma.GameMain").GetProperty("GraphicsHeight", flags).GetMethod, nameof(HeightPrefix));
                    try
                    {
                        object construct(ConstructorInfo ctor, object first, object parent = null)
                        {
                            object[] values = ctor.GetParameters().Select(p => p.HasDefaultValue ? p.DefaultValue : null).ToArray();
                            values[0] = first;
                            if (parent != null) values[1] = parent;
                            return ctor.Invoke(values);
                        }
                        var rectCtor = rectType.GetConstructors().First(c => c.GetParameters()[0].ParameterType.Name == "Point");
                        Type pointType = rectCtor.GetParameters()[0].ParameterType;
                        object point(int x, int y) => Activator.CreateInstance(pointType, x, y);
                        object frame(int w, int h, object parent)
                        {
                            object r = construct(rectCtor, point(w, h), parent);
                            var fc = asm.GetType("Barotrauma.GUIFrame").GetConstructors().First(c => c.GetParameters()[0].ParameterType == rectType);
                            var va = fc.GetParameters().Select(p => p.HasDefaultValue ? p.DefaultValue : null).ToArray(); va[0] = r; va[1] = null;
                            return fc.Invoke(va);
                        }
                        int coord(object component, string name)
                        {
                            object rectangle = componentType.GetProperty("Rect").GetValue(component);
                            return (int)(rectangle.GetType().GetProperty(name)?.GetValue(rectangle) ?? rectangle.GetType().GetField(name).GetValue(rectangle));
                        }
                        object canvas = frame(600, 500, null);
                        object canvasRect = rectField.GetValue(canvas);
                        foreach (float scale in new[] { 0.75f, 1f, 1.5f, 2f })
                        {
                            layoutScale = scale;
                            object row = frame(300, 23, canvasRect), rowRect = rectField.GetValue(row);
                            rectType.GetProperty("AbsoluteOffset").SetValue(rowRect, point(37, 41));
                            object header = frame(300, 23, rowRect);
                            hooks.GetMethod("AttachImage", flags).Invoke(pluginInstance, new[] { row, null, (object)new string('a', 64), null, false });
                            var rowChildren = (System.Collections.IEnumerable)childrenField.GetValue(rowRect);
                            object image = rowChildren.Cast<object>().Select(r => rectType.GetProperty("GUIComponent").GetValue(r)).Single(c => c.GetType().Name == "GUICustomComponent");
                            Check(coord(image, "Y") == coord(header, "Bottom"), "native chat image starts below header at scale " + scale);
                            Check(coord(image, "Bottom") <= coord(row, "Bottom"), "native chat row reserves actual image height");
                            rectType.GetMethod("Resize", new[] { pointType, typeof(bool) }).Invoke(rowRect, new[] { point(100, coord(row, "Height")), (object)false });
                            Check(coord(image, "Right") <= coord(row, "Right"), "native image stays inside narrow resized chat row");

                            object popup = frame(80, 50, canvasRect);
                            var relativeCtor = rectType.GetConstructors().First(c => c.GetParameters()[0].ParameterType.Name == "Vector2");
                            object relative = Activator.CreateInstance(relativeCtor.GetParameters()[0].ParameterType, 0.95f, 0.9f);
                            object contentRect = construct(relativeCtor, relative, rectField.GetValue(popup));
                            var groupCtor = asm.GetType("Barotrauma.GUILayoutGroup").GetConstructors().First(c => c.GetParameters()[0].ParameterType == rectType);
                            object group = construct(groupCtor, contentRect);
                            object popupHeader = frame(50, 23, contentRect);
                            hooks.GetMethod("AttachImage", flags).Invoke(pluginInstance, new[] { group, null, (object)new string('b', 64), null, true });
                            var popupChildren = (System.Collections.IEnumerable)childrenField.GetValue(contentRect);
                            object popupImage = popupChildren.Cast<object>().Select(r => rectType.GetProperty("GUIComponent").GetValue(r)).Single(c => c.GetType().Name == "GUICustomComponent");
                            Check(coord(popupImage, "Width") == (int)(88 * scale), "short caption cannot shrink popup image");
                            Check(coord(popupImage, "Y") >= coord(popupHeader, "Bottom"), "native popup image does not overlap header");
                            Check(coord(popupImage, "Bottom") <= coord(popup, "Bottom"), "native popup expands around image");
                        }
                        hooks.GetMethod("ClearPresentation", flags).Invoke(pluginInstance, null);
                    }
                    finally { harmonyType.GetMethod("UnpatchSelf").Invoke(layoutHarmony, null); }
                }
                finally { hooks.GetField("Active", flags).SetValue(null, null); }
            }
        }
        Console.WriteLine($"Runtime API/patch probe: {asm.GetName()}, {checks} checks (no game boot/render)");
    }
    static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--probe") { Probe(Path.GetFullPath(args[1]), args[2], args.Length > 3 ? args[3] : null); return; }
        if (args.Length > 0 && args[0] == "--library-location") { Console.WriteLine(LibraryLocation.UserLibrary); return; }
        var encoded = Image(); var asset = Decode(encoded);
        Check(asset.Pixels.Length == 2048 && asset.Frame(0.15) == 1 && asset.Frame(0.2) == 0, "decode and animation timing");
        byte[] animatedPixels = Enumerable.Repeat((byte)17, 256).Concat(Enumerable.Repeat((byte)203, 256)).ToArray();
        var animation = new AnimationPlayback(new Asset(new string('a', 64), Array.Empty<byte>(), 8, 8, new[] { 50, 50 }, animatedPixels));
        animation.Prepare(animation.DesiredFrame(0.001, true)); Check(animation.FramePixels.All(p => p == 17), "first animation frame staging");
        animation.Prepare(animation.DesiredFrame(0.051, true)); Check(animation.FramePixels.All(p => p == 203), "short GIF next frame staging at zero offset");
        Check(animation.DesiredFrame(0.051, false) == 0, "motion toggle freezes only when requested");
        foreach (float scale in new[] { 0.75f, 1f, 1.5f, 2f })
        {
            int side = EmoteLayout.ImageSide(scale, 70);
            Check(side <= 70 && EmoteLayout.RowHeight(23, side, 10) >= 23 + side, "bounded chat body at UI scale " + scale);
        }
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
        Bad(() => receive.Append(1, new byte[] { 1 }), "unaligned offset rejected");
        Check(receive.Offset == 0, "invalid offset cannot advance state");
        Check(receive.Append(0, encoded), "single chunk assembly");
        Check(!receive.Append(0, encoded) && receive.Offset == encoded.Length, "duplicate idempotency");
        Bad(() => new AssemblyBuffer(asset.Hash, Format.MaxFile + 1, 0), "oversized allocation");
        Bad(() => receive.Append(int.MaxValue, new byte[] { 1 }), "offset overflow");
        var reordered = new AssemblyBuffer(asset.Hash, 1600, 0);
        Check(!reordered.Append(768, new byte[768]) && reordered.Offset == 0, "out-of-order chunk retained without advancing acknowledgement");
        Check(!reordered.Append(0, new byte[768]) && reordered.Offset == 1536, "gap fill advances through buffered chunk");
        Check(reordered.Append(1536, new byte[64]), "reordered assembly completes");
        var cache = new AssetCache(asset.Cost * 2, 2); int evicted = 0; cache.Evicted = _ => evicted++;
        cache.Put(asset); var a2 = Decode(Image(8, 8)); var a3 = Decode(Image(4, 4)); cache.Put(a2); cache.Get(asset.Hash); cache.Put(a3);
        Check(cache.Get(a2.Hash) == null && evicted == 1 && cache.Bytes <= asset.Cost * 2, "LRU and memory budget");
        var bucket = new Bucket(10, 20, 0);
        Check(bucket.Take(20, 0) && !bucket.Take(1, 0) && bucket.Take(10, 1) && !bucket.Take(1, 0.5), "token bucket and clock rollback");
        foreach (string path in args) { var fixture = File.ReadAllBytes(path); Check(Decode(fixture).Pixels.Length > 0, "Python importer interoperability " + path); }
        Protocol(0); Protocol(0.4);
        checks += DistributionTests.Run(Decode(Image(96, 96, 7, random: true)));
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
