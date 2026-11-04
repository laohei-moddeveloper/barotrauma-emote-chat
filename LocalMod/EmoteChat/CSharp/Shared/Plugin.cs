using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Barotrauma;
using Barotrauma.LuaCs;
using Barotrauma.Networking;
using HarmonyLib;

[assembly: System.Runtime.CompilerServices.IgnoresAccessChecksTo("Barotrauma")]
[assembly: System.Runtime.CompilerServices.IgnoresAccessChecksTo("DedicatedServer")]
namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class IgnoresAccessChecksToAttribute : Attribute
    {
        public string AssemblyName { get; }
        public IgnoresAccessChecksToAttribute(string assemblyName) { AssemblyName = assemblyName; }
    }
}

namespace EmoteChat
{
    public partial class Plugin : IAssemblyPlugin
    {
        private const string PatchId = "barotrauma.emotechat.v01";
        internal static Plugin Active;
        internal static readonly Stopwatch Clock = Stopwatch.StartNew();
        internal static double Now => Clock.Elapsed.TotalSeconds;
        private Harmony harmony;
        internal NetworkBridge network;
        internal readonly Bucket global = new Bucket(TransferLimits.GlobalBytesPerSecond, TransferLimits.GlobalBurstBytes, Now);
        private readonly Bucket ingressBytes = new Bucket(TransferLimits.GlobalReceiveBytesPerSecond, 65536, Now);
        private readonly Bucket ingressPackets = new Bucket(TransferLimits.GlobalPacketsPerSecond, 512, Now);
        public void PreInitPatching() { }
        public void OnLoadCompleted() { }
        public void Initialize()
        {
            if (Active != null) { Log("Already active; duplicate plugin ignored"); return; }
            Active = this;
            try
            {
                var version = typeof(GameMain).Assembly.GetName().Version;
                if (version != new Version(1, 13, 5, 0)) throw new NotSupportedException("This preview targets Barotrauma 1.13.5.0; detected " + version);
                network = new NetworkBridge(OnPacket);
                harmony = new Harmony(PatchId);
                Patch(typeof(CoroutineManager), "Update", new[] { typeof(bool), typeof(float) }, nameof(UpdatePostfix), false);
                Install();
                Log("0.2.0 loaded. Protocol v2; bounded pixel assets only.");
            }
            catch (Exception error) { Log("Disabled: " + error.GetBaseException().Message); Dispose(); }
        }
        internal void Patch(Type type, string method, Type[] args, string handler, bool prefix)
        {
            var target = AccessTools.Method(type, method, args);
            if (target == null) throw new MissingMethodException(type.FullName, method);
            var hook = new HarmonyMethod(typeof(Plugin), handler);
            harmony.Patch(target, prefix: prefix ? hook : null, postfix: prefix ? null : hook);
        }
        private static void UpdatePostfix()
        {
            try { Active?.Tick(); }
            catch (Exception error) { Log("Stopped after update failure: " + error.GetBaseException().Message); Active?.Dispose(); }
        }
        internal static void Log(string text) => DebugConsole.NewMessage("[EmoteChat] " + text);
        private void OnPacket(IReadMessage message, Client client)
        {
            if (Active != this) return;
            try
            {
                if (message.LengthBits - message.BitPosition < 16) return;
                int size = message.ReadUInt16();
                if (size < 1 || size > 810 || message.LengthBits - message.BitPosition != size * 8) return;
                if (!ingressPackets.Take(1, Now) || !ingressBytes.Take(size + 16, Now)) return;
                Receive(message.ReadBytes(size), client);
            }
            catch (Exception e) when (e is IOException || e is InvalidDataException || e is ArgumentException || e is InvalidOperationException || e is IndexOutOfRangeException)
            { /* Malformed input is dropped without flooding logs. */ }
        }
        partial void Install();
        partial void Tick();
        partial void Receive(byte[] packet, Client client);
        partial void Cleanup();
        public void Dispose()
        {
            if (Active != this) return;
            Active = null;
            harmony?.UnpatchSelf(); network?.Dispose(); Cleanup();
        }
    }

    // Reflect only at startup, then cache typed send/start delegates. API mismatch disables the mod.
    internal sealed class NetworkBridge : IDisposable
    {
        private const string Channel = "emotechat.pixels.v2";
        private Action<IReadMessage, Client> callback;
        private Func<string, IWriteMessage> start;
        private Action<IWriteMessage, Client> send;
        public NetworkBridge(Action<IReadMessage, Client> callback)
        {
            this.callback = callback;
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
            Type setupType = typeof(GameMain).Assembly.GetType("Barotrauma.LuaCsSetup", true);
            object setup = setupType.GetProperty("Instance", flags)?.GetValue(null)
                ?? typeof(GameMain).GetField("LuaCs", flags)?.GetValue(null);
            if (setup == null) throw new NotSupportedException("LuaCsSetup.Instance is required");
            object service = (setupType.GetProperty("Networking", flags) ?? setupType.GetProperty("NetworkingService", flags))?.GetValue(setup);
            if (service == null) throw new NotSupportedException("LuaCs networking service missing");
            var methods = service.GetType().GetMethods(flags);
            var startMethod = methods.Single(m => m.Name == "Start" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(string));
            start = (Func<string, IWriteMessage>)startMethod.CreateDelegate(typeof(Func<string, IWriteMessage>), service);
            MethodInfo receive = methods.FirstOrDefault(m => m.Name == "Receive" && m.GetParameters().Length == 2
                && m.GetParameters()[0].ParameterType == typeof(string)
                && m.GetParameters()[1].ParameterType.GetMethod("Invoke")?.GetParameters().FirstOrDefault()?.ParameterType == typeof(IReadMessage));
            if (receive == null) throw new NotSupportedException("Typed LuaCs Receive callback missing");
            Type actionType = receive.GetParameters()[1].ParameterType;
            var parameters = actionType.GetMethod("Invoke").GetParameters().Select(p => Expression.Parameter(p.ParameterType)).ToArray();
            Expression sender = parameters.Length == 2 ? (Expression)parameters[1] : Expression.Constant(null, typeof(Client));
            var body = Expression.Call(Expression.Constant(this), typeof(NetworkBridge).GetMethod(nameof(Dispatch), flags), parameters[0], sender);
            Delegate handler = Expression.Lambda(actionType, body, parameters).Compile();
            var sendMethod = methods.First(m => m.Name == "Send" && m.GetParameters().Length >= 2 && m.GetParameters()[0].ParameterType == typeof(IWriteMessage));
            var messageParam = Expression.Parameter(typeof(IWriteMessage));
            var clientParam = Expression.Parameter(typeof(Client));
            Expression[] arguments;
            if (sendMethod.GetParameters().Length == 3)
                arguments = new Expression[] { messageParam, Expression.PropertyOrField(clientParam, "Connection"), Expression.Constant(DeliveryMethod.Reliable) };
            else arguments = new Expression[] { messageParam, Expression.Constant(DeliveryMethod.Reliable) };
            send = Expression.Lambda<Action<IWriteMessage, Client>>(Expression.Call(Expression.Constant(service), sendMethod, arguments), messageParam, clientParam).Compile();
            receive.Invoke(service, new object[] { Channel, handler });
        }
        private void Dispatch(IReadMessage message, Client client) => callback?.Invoke(message, client);
        public bool Send(byte[] packet, Client recipient = null)
        {
            if (start == null || Plugin.Active == null || !Plugin.Active.global.Take(packet.Length + 32, Plugin.Now)) return false;
            var message = start(Channel); message.WriteUInt16((ushort)packet.Length); message.WriteBytes(packet, 0, packet.Length); send(message, recipient);
            return true;
        }
        public void Dispose() { callback = null; start = null; send = null; }
    }
}
