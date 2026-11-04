using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Barotrauma;
using Barotrauma.Networking;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EmoteChat
{
    public partial class Plugin
    {
        private static readonly FieldInfo PopupList = AccessTools.Field(typeof(ChatBox), "popupMessages");
        private static readonly FieldInfo LobbyList = AccessTools.Field(typeof(NetLobbyScreen), "chatBox");
        private static readonly FieldInfo MessageTranslation = AccessTools.Field(typeof(ChatMessage), "translatedText");
        private readonly List<ImageRow> imageRows = new List<ImageRow>();
        private sealed class ChatState
        {
            public string Hash, Text, Translation;
            public GUIComponent PreviousRow, PreviousPopup;
        }
        private sealed class ImageRow
        {
            public GUIComponent Root, Original;
            public GUICustomComponent Image;
            public Action Layout;
            public void Detach()
            {
                Root.RectTransform.SizeChanged -= Layout;
                Image.OnDraw = null; Image.OnUpdate = null;
                Image.RectTransform.Parent = null;
                if (Original != null) Original.RectTransform.Parent = Root.RectTransform;
            }
        }
        private void InstallPresentation()
        {
            if (PopupList == null || LobbyList == null || MessageTranslation == null) throw new NotSupportedException("Native chat containers changed");
            Patch(typeof(ChatBox), "AddMessage", new[] { typeof(ChatMessage) }, nameof(ChatPrefix), true);
            Patch(typeof(ChatBox), "AddMessage", new[] { typeof(ChatMessage) }, nameof(ChatPostfix), false);
            harmony.Patch(AccessTools.Method(typeof(ChatBox), "AddMessage", new[] { typeof(ChatMessage) }), finalizer: new HarmonyMethod(typeof(Plugin), nameof(ChatFinalizer)));
            Patch(typeof(NetLobbyScreen), "NewChatMessage", new[] { typeof(ChatMessage) }, nameof(LobbyPrefix), true);
            Patch(typeof(NetLobbyScreen), "NewChatMessage", new[] { typeof(ChatMessage) }, nameof(LobbyPostfix), false);
            harmony.Patch(AccessTools.Method(typeof(NetLobbyScreen), "NewChatMessage", new[] { typeof(ChatMessage) }), finalizer: new HarmonyMethod(typeof(Plugin), nameof(ChatFinalizer)));
            Patch(typeof(Character), "ShowSpeechBubble", new[] { typeof(Color), typeof(string) }, nameof(SpeechPrefix), true);
        }
        private static IEnumerable<GUIComponent> Popups(ChatBox box) => (IEnumerable<GUIComponent>)PopupList.GetValue(box);
        private static void ChatPrefix(ChatBox __instance, ChatMessage message, bool __runOriginal, out ChatState __state)
        {
            __state = null;
            string hash = Format.ParseMarker(message.Text);
            if (Active == null || hash == null || !__runOriginal) return;
            __state = new ChatState { Hash = hash, Text = message.Text, Translation = (string)MessageTranslation.GetValue(message),
                PreviousRow = ((GUIListBox)ChatList.GetValue(__instance)).Content.Children.LastOrDefault(),
                PreviousPopup = Popups(__instance).LastOrDefault() };
            // Sanitize BEFORE the native row, wrapped label and collapsed popup measure text.
            message.Text = "";
            MessageTranslation.SetValue(message, null);
        }
        private static Exception ChatFinalizer(Exception __exception, ChatMessage message, ChatState __state)
        { RestoreText(message, __state); return __exception; }
        private static void RestoreText(ChatMessage message, ChatState state)
        { if (state != null) { message.Text = state.Text; MessageTranslation.SetValue(message, state.Translation); } }
        private static bool SpeechPrefix(string text) => Active == null || Format.ParseMarker(text) == null;
        private void RequestAsset(string hash)
        {
            if (requests.Count < 32 && cache.Get(hash) == null && !requests.ContainsKey(hash))
                requests[hash] = new RequestState { Until = Now + 55, Next = 0 };
        }
        private static void ChatPostfix(ChatBox __instance, ChatMessage message, ChatState __state, bool __runOriginal)
        {
            if (__state == null) return;
            RestoreText(message, __state);
            var plugin = Active;
            if (!__runOriginal || plugin == null || !plugin.enabled) return;
            string hash = __state.Hash;
            plugin.RequestAsset(hash);
            var list = (GUIListBox)ChatList.GetValue(__instance);
            var row = list.Content.Children.LastOrDefault();
            if (row != null && !ReferenceEquals(row, __state.PreviousRow))
                plugin.AttachImage(row, row.Children.OfType<GUITextBlock>().LastOrDefault(), hash, list, false);
            var popup = Popups(__instance).LastOrDefault();
            if (popup != null && !ReferenceEquals(popup, __state.PreviousPopup))
            {
                var content = popup.Children.OfType<GUILayoutGroup>().FirstOrDefault();
                if (content != null) plugin.AttachImage(content, content.Children.OfType<GUITextBlock>().LastOrDefault(), hash, null, true);
            }
            var character = message.SenderCharacter;
            if (character != null && !character.IsDead && (message.Type == ChatMessageType.Default || message.Type == ChatMessageType.Radio))
            {
                if (plugin.bubbles.Count >= 16 && !plugin.bubbles.ContainsKey(character.ID))
                    plugin.bubbles.Remove(plugin.bubbles.OrderBy(p => p.Value.PendingUntil).First().Key);
                plugin.bubbles[character.ID] = new Bubble { Hash = hash, Character = character,
                    Until = plugin.cache.Get(hash) == null ? 0 : Now + 6, PendingUntil = Now + 180 };
            }
        }
        private static void LobbyPrefix(NetLobbyScreen __instance, ChatMessage message, bool __runOriginal, out ChatState __state)
        {
            __state = null;
            string hash = Format.ParseMarker(message.Text);
            if (Active == null || hash == null || !__runOriginal) return;
            __state = new ChatState { Hash = hash, Text = message.Text, Translation = (string)MessageTranslation.GetValue(message),
                PreviousRow = ((GUIListBox)LobbyList.GetValue(__instance)).Content.Children.LastOrDefault() };
            message.Text = "";
            MessageTranslation.SetValue(message, null);
        }
        private static void LobbyPostfix(NetLobbyScreen __instance, ChatMessage message, ChatState __state, bool __runOriginal)
        {
            if (__state == null) return;
            RestoreText(message, __state);
            var plugin = Active;
            if (!__runOriginal || plugin == null || !plugin.enabled) return;
            plugin.RequestAsset(__state.Hash);
            var list = (GUIListBox)LobbyList.GetValue(__instance);
            var header = list.Content.Children.LastOrDefault() as GUITextBlock;
            if (header == null || ReferenceEquals(header, __state.PreviousRow)) return;
            header.RectTransform.Parent = null;
            var row = new GUIFrame(new RectTransform(new Vector2(1, 0), list.Content.RectTransform), style: null);
            header.RectTransform.Parent = row.RectTransform;
            header.RectTransform.AbsoluteOffset = Point.Zero;
            header.RectTransform.RelativeOffset = Vector2.Zero;
            header.RectTransform.ScreenSpaceOffset = Point.Zero;
            plugin.AttachImage(row, null, __state.Hash, list, false);
        }
        private void AttachImage(GUIComponent root, GUITextBlock original, string hash, GUIListBox list, bool popup)
        {
            if (original != null) root.RemoveChild(original);
            var image = new GUICustomComponent(new RectTransform(new Point(88, 88), root.RectTransform),
                (batch, component) => { if (Active == this && enabled) DrawAsset(batch, component.Rect, hash); }) { CanBeFocused = false };
            bool arranging = false;
            int previousWidth = -1; float previousScale = -1;
            Action layout = () =>
            {
                if (arranging) return;
                arranging = true;
                try
                {
                    int padding = Math.Max(1, GUI.IntScale(10));
                    int side = EmoteLayout.ImageSide(GUI.Scale, popup ? GUI.IntScale(88) : Math.Max(1, root.Rect.Width - padding * 2));
                    image.RectTransform.MinSize = image.RectTransform.MaxSize = new Point(side, side);
                    image.RectTransform.Resize(new Point(side, side), false);
                    int headerHeight = root.Children.Where(c => !ReferenceEquals(c, image)).Sum(c => c.Rect.Height);
                    if (popup)
                    {
                        var container = root.RectTransform.Parent.GUIComponent;
                        Vector2 fraction = root.RectTransform.RelativeSize;
                        int width = Math.Max(GUI.IntScale(110), root.Children.OfType<GUITextBlock>().Select(t => (int)t.TextSize.X + padding * 2).DefaultIfEmpty(0).Max());
                        width = Math.Min(width, GUI.IntScale(320));
                        int contentWidth = Math.Max(side, (int)(width * fraction.X));
                        // Resizing the popup recursively would stretch the measured sender label and
                        // then push the image below the popup. Resize content explicitly, preserving heights.
                        container.RectTransform.IsFixedSize = false;
                        foreach (var child in root.Children.Where(c => !ReferenceEquals(c, image)))
                        {
                            child.RectTransform.Resize(new Point(contentWidth, child.Rect.Height), false);
                            (child as GUITextBlock)?.CalculateHeightFromText();
                        }
                        headerHeight = root.Children.Where(c => !ReferenceEquals(c, image)).Sum(c => c.Rect.Height);
                        int contentHeight = headerHeight + side + padding;
                        container.RectTransform.Resize(new Point(width, (int)Math.Ceiling(contentHeight / Math.Max(0.1f, fraction.Y))), false);
                        root.RectTransform.Resize(new Point(contentWidth, contentHeight), false);
                        container.RectTransform.IsFixedSize = true;
                        (root as GUILayoutGroup)?.Recalculate();
                    }
                    else
                    {
                        image.RectTransform.AbsoluteOffset = new Point(padding, headerHeight);
                        root.RectTransform.Resize(new Point(root.Rect.Width, EmoteLayout.RowHeight(headerHeight, side, padding)), false);
                    }
                    previousWidth = root.Rect.Width; previousScale = GUI.Scale;
                    list?.RecalculateChildren(); list?.UpdateScrollBarSize();
                }
                finally { arranging = false; }
            };
            root.RectTransform.SizeChanged += layout;
            image.OnUpdate = (dt, component) => { if (previousWidth != root.Rect.Width || previousScale != GUI.Scale) layout(); };
            for (int i = imageRows.Count - 1; i >= 0; i--)
                if (imageRows[i].Root.RectTransform.Parent == null) { imageRows[i].Detach(); imageRows.RemoveAt(i); }
            if (imageRows.Count >= 192) { imageRows[0].Detach(); imageRows.RemoveAt(0); }
            imageRows.Add(new ImageRow { Root = root, Original = original, Image = image, Layout = layout });
            layout();
        }
        private static void BubblePostfix(Character __instance, SpriteBatch spriteBatch, Camera cam)
        {
            var plugin = Active;
            if (plugin == null || !plugin.enabled || __instance.Info == null || !__instance.Enabled || __instance.InvisibleTimer > 0 || __instance.IsDead
                || !plugin.bubbles.TryGetValue(__instance.ID, out var bubble) || !ReferenceEquals(bubble.Character, __instance) || bubble.Until <= Now) return;
            int side = GUI.IntScale(80);
            Vector2 position = cam.WorldToScreen(__instance.DrawPosition) - new Vector2(side / 2, GUI.IntScale(150));
            var rect = new Rectangle((int)position.X, (int)position.Y, side, side);
            if (!new Rectangle(0, 0, GameMain.GraphicsWidth, GameMain.GraphicsHeight).Intersects(rect)) return;
            GUI.DrawRectangle(spriteBatch, rect, Color.Black * 0.7f, true);
            plugin.DrawAsset(spriteBatch, rect, bubble.Hash);
        }
        private void ClearPresentation()
        {
            foreach (var row in imageRows) row.Detach();
            imageRows.Clear();
        }
    }
}
