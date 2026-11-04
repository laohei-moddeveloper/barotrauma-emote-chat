using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Barotrauma;
using Barotrauma.Networking;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace EmoteChat
{
    public partial class Plugin
    {
        private readonly AssetCache cache = new AssetCache(24 * 1024 * 1024, 32);
        private sealed class RequestState { public double Until, Next; public bool Done; }
        private sealed class Bubble { public string Hash; public Character Character; public double Until, PendingUntil; }
        private readonly Dictionary<string, Picture> textures = new Dictionary<string, Picture>();
        private readonly Dictionary<string, RequestState> requests = new Dictionary<string, RequestState>();
        private readonly Dictionary<ushort, Bubble> bubbles = new Dictionary<ushort, Bubble>();
        private readonly Queue<string> loads = new Queue<string>();
        private Session session;
        private object connection;
        private Task<Asset> loading;
        private Asset selectedAsset;
        private string loadingHash, selectedHash, library, legacyLibrary, cacheServerKey;
        private readonly Dictionary<string, string> localFiles = new Dictionary<string, string>();
        private bool enabled = true, animate = true;
        private GUIFrame picker;
        private GUITextBlock status;
        private DebugConsole.Command command;
        private double nextSend, nextSweep, nextStatus;
        private static readonly System.Reflection.FieldInfo ChatList = AccessTools.Field(typeof(ChatBox), "chatBox");
        partial void Install()
        {
            if (ChatList == null || ChatList.FieldType != typeof(GUIListBox)) throw new NotSupportedException("ChatBox layout changed");
            InstallPresentation();
            Patch(typeof(GameMain), "Draw", new[] { typeof(GameTime) }, nameof(RenderPrefix), true);
            Patch(typeof(Character), "DrawGUIMessages", new[] { typeof(SpriteBatch), typeof(Camera) }, nameof(BubblePostfix), false);
            command = new DebugConsole.Command("ec", "Emote Chat: open picker (also F8)", _ => OpenPicker());
            DebugConsole.Commands.Add(command);
            cache.Evicted = RemovePicture;
            var package = ContentPackageManager.EnabledPackages.All.FirstOrDefault(p => p.Name == "表情包聊天和气泡显示 | chat with emote" || p.Name.StartsWith("Emote Chat ", StringComparison.Ordinal));
            legacyLibrary = Path.GetFullPath(Path.Combine(package?.Dir ?? Path.Combine("LocalMods", "EmoteChat"), "Library"));
            library = LibraryLocation.UserLibrary;
        }
        private void NewSession()
        {
            session?.Dispose(); session = null;
            requests.Clear(); bubbles.Clear(); selectedHash = null; selectedAsset = null;
            string key = GameMain.Client?.ClientPeer?.ServerConnection?.Endpoint?.ToString();
            if (key != null && key != cacheServerKey) { cache.Clear(); cacheServerKey = key; }
            if (connection == null) return;
            session = new Session(false, true, cache, data => network.Send(data), Now);
            session.CanSend = size => global.Available(size + 32, Now);
            session.AllowOffer = hash => enabled && requests.TryGetValue(hash, out var request) && request.Until >= Now && !request.Done;
            session.Received = hash =>
            {
                if (requests.TryGetValue(hash, out var request)) request.Done = true;
                foreach (var bubble in bubbles.Values)
                    if (bubble.Hash == hash && bubble.Until == 0 && bubble.PendingUntil >= Now) bubble.Until = Now + 6;
            };
            session.Sent = hash =>
            {
                if (selectedHash != hash || !enabled || GameMain.Client == null) return;
                if (selectedAsset != null) cache.Put(selectedAsset);
                selectedAsset = null;
                selectedHash = null;
                GameMain.Client.SendChatMessage(Format.Marker(hash));
                recent.Remove(hash); recent.Insert(0, hash); if (recent.Count > 24) recent.RemoveAt(24);
                ClosePicker();
            };
            session.Error = reason => { selectedHash = null; selectedAsset = null; Log(reason); };
        }
        partial void Tick()
        {
            if (picker != null && !GUIMessageBox.MessageBoxes.Contains(picker)) ClosePicker();
            if (picker != null && (pickerWidth != GameMain.GraphicsWidth || pickerHeight != GameMain.GraphicsHeight || pickerScale != GUI.Scale)) { ClosePicker(); OpenPicker(); }
            if (picker != null && PlayerInput.KeyHit(Keys.Escape)) ClosePicker();
            object current = GameMain.Client?.ClientPeer?.ServerConnection;
            if (!ReferenceEquals(current, connection)) { connection = current; NewSession(); }
            if (PlayerInput.KeyHit(Keys.F8)) OpenPicker();
            if (status != null && Now >= nextStatus)
            {
                nextStatus = Now + 0.25;
                status.Text = !enabled ? "表情已关闭" : connection == null ? "请先加入服务器" : selectedHash != null
                    ? session?.Busy == true ? "正在同步表情 · " + session.Progress + "%" : "正在准备表情…"
                    : session?.Ready == true ? "选择一张表情发送" : "正在连接表情服务…";
            }
            if (loading != null && loading.IsCompleted)
            {
                var task = loading; loading = null;
                if (task.Status == TaskStatus.RanToCompletion && enabled)
                {
                    cache.Put(task.Result);
                    if (selectedHash == task.Result.Hash) StartSend(task.Result);
                }
                else if (task.Status != TaskStatus.RanToCompletion) { _ = task.Exception; if (selectedHash == loadingHash) selectedHash = null; Log("Import package rejected; re-import the original image."); }
                loadingHash = null;
            }
            if (loading == null && loads.Count > 0)
            {
                string hash = loads.Dequeue(); loadingHash = hash;
                string path = localFiles.TryGetValue(hash, out var localPath) ? localPath : Path.Combine(library, hash + ".ecp");
                loading = Task.Run(() => Format.Decode(ReadLocal(path, Format.MaxFile), hash, true));
            }
            session?.Tick(Now);
            foreach (var request in requests)
            {
                if (!request.Value.Done && request.Value.Until > Now && request.Value.Next <= Now && enabled)
                { request.Value.Next = Now + 0.5; session?.Request(request.Key); break; }
            }
            if (Now > nextSweep)
            {
                nextSweep = Now + 2;
                foreach (string hash in requests.Where(p => p.Value.Until < Now).Select(p => p.Key).ToArray()) requests.Remove(hash);
                foreach (ushort id in bubbles.Where(p => p.Value.Until == 0 ? p.Value.PendingUntil < Now : p.Value.Until < Now).Select(p => p.Key).ToArray()) bubbles.Remove(id);
            }
        }
        partial void Receive(byte[] packet, Client client) { if (connection != null) session?.Receive(packet, Now); }
        private void QueueLoad(string hash)
        {
            if (enabled && loads.Count < 8 && loadingHash != hash && !loads.Contains(hash) && cache.Get(hash) == null) loads.Enqueue(hash);
        }
        private void Select(string hash)
        {
            if (!enabled || session == null || session.Busy || selectedHash != null || Now < nextSend)
            { Log("Join a mod-enabled server, enable emotes, and wait for the current transfer/cooldown."); return; }
            selectedHash = hash; nextSend = Now + 2;
            var asset = cache.Get(hash);
            if (asset == null) QueueLoad(hash); else StartSend(asset);
        }
        private void StartSend(Asset asset)
        {
            if (session == null || session.Busy || !enabled) { selectedHash = null; return; }
            session.Upload(asset, Now);
            selectedAsset = asset;
            Log("Preparing emote. First transfer is rate-limited; cached sends complete quickly.");
        }
        private void ReadLibrary()
        {
            localNames.Clear();
            localFiles.Clear();
            // New imports use the fixed per-user folder; old installed libraries remain readable.
            foreach (string folder in new[] { library, legacyLibrary }.Distinct())
            {
                if (!Directory.Exists(folder)) continue;
                try
                {
                    string namesPath = Path.Combine(folder, "names.json");
                    if (File.Exists(namesPath))
                    {
                        var found = JsonSerializer.Deserialize<Dictionary<string, string>>(ReadLocal(namesPath, 32768));
                        if (found != null) foreach (var pair in found) if (!localNames.ContainsKey(pair.Key)) localNames[pair.Key] = pair.Value;
                    }
                    foreach (string path in Directory.EnumerateFiles(folder, "*.ecp").Take(128))
                    {
                        string hash = Path.GetFileNameWithoutExtension(path);
                        if (Format.IsHash(hash) && localFiles.Count < 128 && !localFiles.ContainsKey(hash)) localFiles[hash] = path;
                    }
                }
                catch (Exception e) when (e is IOException || e is InvalidDataException || e is JsonException || e is UnauthorizedAccessException) { Log("Cannot read an import folder"); }
            }
        }
        private static byte[] ReadLocal(string path, int maximum)
        {
            var info = new FileInfo(path);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0 || info.Length > maximum) throw new InvalidDataException("Invalid local file");
            using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (file.Length > maximum) throw new InvalidDataException("File too large");
                byte[] data = new byte[(int)file.Length]; int done = 0;
                while (done < data.Length) { int read = file.Read(data, done, data.Length - done); if (read == 0) throw new EndOfStreamException(); done += read; }
                return data;
            }
        }
        partial void Cleanup()
        {
            session?.Dispose(); ClosePicker();
            uiPaint?.Dispose(); uiPaint = null;
            ClearPresentation();
            if (command != null) DebugConsole.Commands.Remove(command);
            cache.Clear(); requests.Clear(); bubbles.Clear(); loads.Clear();
            selectedAsset = null; selectedHash = null;
            if (loading != null) loading.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
            loading = null;
        }
    }
}
