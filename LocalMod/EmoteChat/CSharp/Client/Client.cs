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
        private GUIMessageBox picker;
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
                picker?.Close(); picker = null; status = null;
            };
            session.Error = reason => { selectedHash = null; selectedAsset = null; Log(reason); };
        }
        partial void Tick()
        {
            if (picker?.Closed == true) { picker = null; status = null; }
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
        private void OpenPicker()
        {
            if (picker != null) { picker.Close(); picker = null; status = null; return; }
            picker = new GUIMessageBox("表情 · 0.1.1", "点击发送 · 使用当前聊天频道 · F8 打开或关闭", new LocalizedString[] { "关闭" }, relativeSize: new Vector2(0.55f, 0.8f));
            var panel = picker;
            panel.Buttons[0].OnClicked = (button, userdata) => { panel.Close(); if (picker == panel) { picker = null; status = null; } return true; };
            status = new GUITextBlock(new RectTransform(new Vector2(1f, 0.05f), panel.Content.RectTransform), "选择一张表情发送", font: GUIStyle.SmallFont, textAlignment: Alignment.Center);
            var settings = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.07f), panel.Content.RectTransform), isHorizontal: true) { RelativeSpacing = 0.02f };
            var toggle = new GUIButton(new RectTransform(new Vector2(0.49f, 1f), settings.RectTransform), enabled ? "表情：开" : "表情：关");
            toggle.OnClicked = (button, userdata) =>
            {
                enabled = !enabled; selectedHash = null; loads.Clear();
                session?.Dispose(); session = null; cache.Clear(); requests.Clear(); bubbles.Clear();
                if (enabled) NewSession();
                button.Text = enabled ? "表情：开" : "表情：关"; return true;
            };
            var motion = new GUIButton(new RectTransform(new Vector2(0.49f, 1f), settings.RectTransform), animate ? "动画：开" : "动画：关");
            motion.OnClicked = (button, userdata) => { animate = !animate; button.Text = animate ? "动画：开" : "动画：关"; return true; };
            var list = new GUIListBox(new RectTransform(new Vector2(1f, 0.62f), panel.Content.RectTransform));
            new GUITextBlock(new RectTransform(new Vector2(1f, 0.08f), panel.Content.RectTransform), "导入器可放在任意磁盘。导入完成后重新打开面板。", font: GUIStyle.SmallFont, wrap: true) { ToolTip = library };
            var names = new Dictionary<string, string>();
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
                        if (found != null) foreach (var pair in found) if (!names.ContainsKey(pair.Key)) names[pair.Key] = pair.Value;
                    }
                    foreach (string path in Directory.EnumerateFiles(folder, "*.ecp").Take(128))
                    {
                        string hash = Path.GetFileNameWithoutExtension(path);
                        if (Format.IsHash(hash) && localFiles.Count < 128 && !localFiles.ContainsKey(hash)) localFiles[hash] = path;
                    }
                }
                catch (Exception e) when (e is IOException || e is InvalidDataException || e is JsonException || e is UnauthorizedAccessException) { Log("Cannot read an import folder"); }
            }
            int index = 0;
            GUILayoutGroup row = null;
            string[] paths = localFiles.Values.ToArray();
            foreach (string path in paths)
            {
                string hash = Path.GetFileNameWithoutExtension(path);
                if (!Format.IsHash(hash)) continue;
                if (index % 3 == 0)
                    row = new GUILayoutGroup(new RectTransform(new Vector2(1, 0), list.Content.RectTransform) { MinSize = new Point(0, 120), MaxSize = new Point(int.MaxValue, 120) }, isHorizontal: true) { RelativeSpacing = 0.02f };
                string label = names.TryGetValue(hash, out var name) && name != null ? new string(name.Where(c => !char.IsControl(c) && c != '‖' && c != '<' && c != '>').Take(16).ToArray()) : "表情 " + (index + 1);
                var button = new GUIButton(new RectTransform(new Vector2(0.32f, 1), row.RectTransform), label, textAlignment: Alignment.BottomCenter) { ToolTip = "点击发送" };
                new GUICustomComponent(new RectTransform(new Point(80, 80), button.RectTransform, Anchor.TopCenter) { AbsoluteOffset = new Point(0, 6) }, (sb, component) =>
                { QueueLoad(hash); DrawAsset(sb, component.Rect, hash); });
                button.OnClicked = (sender, userdata) => { Select(hash); return true; };
                index++;
            }
            if (index == 0) Log("No imported emotes. Import folder: " + library);
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
            session?.Dispose(); picker?.Close(); picker = null;
            ClearPresentation();
            if (command != null) DebugConsole.Commands.Remove(command);
            cache.Clear(); requests.Clear(); bubbles.Clear(); loads.Clear();
            selectedAsset = null; selectedHash = null;
            if (loading != null) loading.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
            loading = null;
        }
    }
}
