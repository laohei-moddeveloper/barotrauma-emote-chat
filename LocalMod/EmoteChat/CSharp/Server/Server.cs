using System;
using System.Collections.Generic;
using System.Linq;
using Barotrauma;
using Barotrauma.Networking;
using HarmonyLib;

namespace EmoteChat
{
    public partial class Plugin
    {
        private sealed class Peer : IDisposable
        {
            public Session Session;
            public readonly Dictionary<string, double> Owned = new Dictionary<string, double>();
            public readonly Dictionary<string, double> Grants = new Dictionary<string, double>();
            public void Dispose() => Session.Dispose();
        }
        private readonly AssetCache cache = new AssetCache(8 * 1024 * 1024, 64);
        private readonly Dictionary<Client, Peer> peers = new Dictionary<Client, Peer>();
        private readonly List<Peer> schedule = new List<Peer>();
        private double nextSweep;
        private int roundRobin;
        [ThreadStatic] private static string routingHash;
        [ThreadStatic] private static Client routingSender;
        private struct RoutingState { public bool Initialized; public string Hash; public Client Sender; }
        private static void RoutePrefix(string message, Client senderClient, out RoutingState __state)
        {
            __state = new RoutingState { Initialized = true, Hash = routingHash, Sender = routingSender };
            routingHash = Format.FindMarker(message); routingSender = senderClient;
        }
        private static Exception RouteFinalizer(Exception __exception, RoutingState __state)
        {
            if (__state.Initialized) { routingHash = __state.Hash; routingSender = __state.Sender; }
            return __exception;
        }
        partial void Install()
        {
            var route = AccessTools.Method(typeof(GameServer), "SendChatMessage", new[] { typeof(string), typeof(ChatMessageType?), typeof(Client), typeof(Character), typeof(PlayerConnectionChangeType), typeof(ChatMode) });
            harmony.Patch(route, prefix: new HarmonyMethod(typeof(Plugin), nameof(RoutePrefix)) { priority = Priority.First }, finalizer: new HarmonyMethod(typeof(Plugin), nameof(RouteFinalizer)));
            Patch(typeof(GameServer), "SendDirectChatMessage", new[] { typeof(ChatMessage), typeof(Client) }, nameof(DirectChatPrefix), true);
            Patch(typeof(GameServer), "SendDirectChatMessage", new[] { typeof(ChatMessage), typeof(Client) }, nameof(DirectChatPostfix), false);
        }
        private static bool DirectChatPrefix(ChatMessage msg, Client recipient)
        {
            var plugin = Active;
            string hash = Format.ParseMarker(msg.Text);
            if (plugin == null) return true;
            // Native distance/radio garbling must never turn an internal hash into visible text.
            // Drop degraded image messages rather than bypassing native hearing restrictions.
            if (routingHash != null && ReferenceEquals(msg.SenderClient, routingSender) && hash != routingHash) return false;
            if (hash == null) return !Format.IsTransportText(msg.Text);
            // Native routing already chose this recipient. Never broadcast separately.
            if (msg.SenderClient == null || !plugin.peers.TryGetValue(msg.SenderClient, out var owner)
                || !owner.Owned.ContainsKey(hash) || plugin.cache.Get(hash) == null) return false;
            return true;
        }
        private static void DirectChatPostfix(ChatMessage msg, Client recipient, bool __runOriginal)
        {
            var plugin = Active;
            string hash = Format.ParseMarker(msg.Text);
            // Other prefixes may cancel delivery. Grant only after native queuing actually ran.
            if (!__runOriginal || plugin == null || hash == null || recipient == null || !recipient.ChatMsgQueue.Contains(msg)) return;
            if (plugin.peers.TryGetValue(recipient, out var peer))
            {
                if (peer.Grants.Count >= 64 && !peer.Grants.ContainsKey(hash)) peer.Grants.Remove(peer.Grants.OrderBy(p => p.Value).First().Key);
                peer.Grants[hash] = Now + 60;
            }
        }
        partial void Receive(byte[] packet, Client client)
        {
            if (client == null || GameMain.Server == null) return;
            if (!peers.TryGetValue(client, out var peer))
            {
                // Only a valid handshake allocates per-client state. Identity comes from the connection.
                if (packet.Length != 2 || packet[0] != Session.Hello || packet[1] != 2 || peers.Count >= 64) return;
                peer = new Peer();
                var state = peer;
                peer.Session = new Session(true, false, cache, data => network.Send(data, client), Now);
                peer.Session.CanSend = size => global.Available(size + 32, Now);
                peer.Session.AllowOffer = _ => true;
                peer.Session.AllowGet = hash => state.Grants.TryGetValue(hash, out double until) && until >= Now;
                peer.Session.Received = hash =>
                {
                    if (state.Owned.Count >= 32 && !state.Owned.ContainsKey(hash)) state.Owned.Remove(state.Owned.OrderBy(p => p.Value).First().Key);
                    state.Owned[hash] = Now;
                };
                peers.Add(client, peer); schedule.Add(peer);
            }
            // Bound concurrent incoming allocations/worker queue to eight clients.
            if (packet[0] == Session.Offer && peers.Values.Count(p => p.Session.Receiving) >= 8 && !peer.Session.Receiving) return;
            peer.Session.Receive(packet, Now);
        }
        partial void Tick()
        {
            double now = Now;
            if (now >= nextSweep)
            {
                nextSweep = now + 2;
                foreach (var pair in peers.ToArray())
                {
                    if (GameMain.Server == null || !GameMain.Server.ConnectedClients.Contains(pair.Key))
                    { pair.Value.Dispose(); peers.Remove(pair.Key); schedule.Remove(pair.Value); continue; }
                    foreach (var grant in pair.Value.Grants.Where(g => g.Value < now).Select(g => g.Key).ToArray()) pair.Value.Grants.Remove(grant);
                }
            }
            if (peers.Count == 0) return;
            // Rotate peers so one download cannot starve every other recipient.
            for (int i = 0; i < schedule.Count; i++) schedule[(i + roundRobin) % schedule.Count].Session.Tick(now);
            roundRobin = (roundRobin + 1) % schedule.Count;
        }
        partial void Cleanup() { foreach (var peer in peers.Values) peer.Dispose(); peers.Clear(); schedule.Clear(); cache.Clear(); }
    }
}
