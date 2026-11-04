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
        private sealed class ImageRow
        {
            public GUIComponent Root, Header;
            public GUICustomComponent Image;
            public Action Layout;
            public void Detach()
            {
                Root.RectTransform.SizeChanged -= Layout;
                Image.OnDraw = null; Image.OnUpdate = null;
                Image.RectTransform.Parent = null;
                if (Header != null) Header.RectTransform.Parent = null;
            }
        }
        private void InstallPresentation()
        {
            if (PopupList == null || LobbyList == null || MessageTranslation == null) throw new NotSupportedException("Native chat containers changed");
            // Consume before Lua chat events, native translation, labels and speech bubbles.
            var hook = new HarmonyMethod(typeof(Plugin), nameof(ReceiveChatPrefix)) { priority = Priority.First };
            harmony.Patch(AccessTools.Method(typeof(GameClient), "AddChatMessage", new[] { typeof(ChatMessage) }), prefix: hook);
            Patch(typeof(ChatBox), "AddMessage", new[] { typeof(ChatMessage) }, nameof(ChatPrefix), true);
            Patch(typeof(NetLobbyScreen), "NewChatMessage", new[] { typeof(ChatMessage) }, nameof(LobbyPrefix), true);
            Patch(typeof(Character), "ShowSpeechBubble", new[] { typeof(Color), typeof(string) }, nameof(SpeechPrefix), true);
        }
        private static bool Consume(ChatMessage message, out string hash)
        {
            hash = null;
            if (Active == null || !Format.IsTransportText(message?.Text)) return false;
            hash = Format.FindMarker(message.Text);
            // Never restore transport text: downstream hooks must not render metadata.
            message.Text = "";
            MessageTranslation.SetValue(message, null);
            return true;
        }
        private static bool ReceiveChatPrefix(GameClient __instance, ChatMessage message)
        {
            if (!Consume(message, out string hash)) return true;
            var plugin = Active;
            if (hash != null && plugin.enabled)
            {
                plugin.PresentChat(__instance.ChatBox, hash, SenderLabel(message));
                if (GameMain.NetLobbyScreen != null) plugin.PresentLobby(GameMain.NetLobbyScreen, hash, SenderLabel(message));
                plugin.AddBubble(message, hash);
            }
            return false;
        }
        private static bool ChatPrefix(ChatBox __instance, ChatMessage message)
        {
            if (!Consume(message, out string hash)) return true;
            if (hash != null && Active.enabled) { Active.PresentChat(__instance, hash, SenderLabel(message)); Active.AddBubble(message, hash); }
            return false;
        }
        private static bool LobbyPrefix(NetLobbyScreen __instance, ChatMessage message)
        {
            if (!Consume(message, out string hash)) return true;
            if (hash != null && Active.enabled) Active.PresentLobby(__instance, hash, SenderLabel(message));
            return false;
        }
        private static bool SpeechPrefix(string text) => Active == null || !Format.IsTransportText(text);
        private void RequestAsset(string hash)
        {
            if (requests.Count < 32 && cache.Get(hash) == null && !requests.ContainsKey(hash))
                requests[hash] = new RequestState { Until = Now + 55, Next = 0 };
        }
        private void AddBubble(ChatMessage message, string hash)
        {
            var character = message.SenderCharacter;
            if (character == null || character.IsDead || (message.Type != ChatMessageType.Default && message.Type != ChatMessageType.Radio)) return;
            if (bubbles.Count >= 16 && !bubbles.ContainsKey(character.ID))
                bubbles.Remove(bubbles.OrderBy(p => p.Value.PendingUntil).First().Key);
            bubbles[character.ID] = new Bubble { Hash = hash, Character = character,
                Until = cache.Get(hash) == null ? 0 : Now + 6, PendingUntil = Now + 180 };
        }
        private static string SenderLabel(ChatMessage message)
        {
            // Use the name selected by native server routing, not an account ID or asset name.
            string name = message.SenderName;
            if (string.IsNullOrWhiteSpace(name)) name = message.SenderClient?.Name ?? message.SenderCharacter?.Name;
            return name == null ? "" : new string(name.Where(c => !char.IsControl(c)).Take(128).ToArray());
        }
        private static GUITextBlock SenderHeader(GUIComponent root, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            return new GUITextBlock(new RectTransform(new Point(1, GUI.IntScale(22)), root.RectTransform),
                RichString.Plain(name), textColor: Color.LightGray, font: GUIStyle.SmallFont,
                textAlignment: Alignment.Left, style: null)
            { CanBeFocused = false, AutoScaleHorizontal = true, AutoScaleVertical = true, ToolTip = name };
        }
        private void PresentChat(ChatBox box, string hash, string sender)
        {
            if (box == null) return;
            RememberReceived(hash, sender); RequestAsset(hash);
            AddImageRow((GUIListBox)ChatList.GetValue(box), hash, sender);
            if (!box.ToggleOpen)
            {
                var popups = (List<GUIComponent>)PopupList.GetValue(box);
                while (popups.Count >= 8) { popups[0].RectTransform.Parent = null; popups.RemoveAt(0); }
                var popup = new GUIFrame(new RectTransform(new Point(GUI.IntScale(104)), box.GUIFrame.RectTransform), style: null)
                { UserData = 0f, CanBeFocused = false };
                AttachImage(popup, hash, null, true, SenderHeader(popup, sender));
                popups.Add(popup);
            }
        }
        private void PresentLobby(NetLobbyScreen screen, string hash, string sender)
        {
            var list = (GUIListBox)LobbyList.GetValue(screen);
            if (list == null) return;
            RememberReceived(hash, sender); RequestAsset(hash); AddImageRow(list, hash, sender);
        }
        private void AddImageRow(GUIListBox list, string hash, string sender)
        {
            bool bottom = list.BarSize >= 1 || list.BarScroll >= 0.99f;
            while (list.Content.CountChildren >= 60) list.Content.Children.First().RectTransform.Parent = null;
            var row = new GUIFrame(new RectTransform(new Vector2(0.95f, 0), list.Content.RectTransform, Anchor.TopCenter), style: null);
            AttachImage(row, hash, list, false, SenderHeader(row, sender));
            if (bottom) list.BarScroll = 1;
        }
        private void AttachImage(GUIComponent root, string hash, GUIListBox list, bool popup, GUIComponent header = null)
        {
            var image = new GUICustomComponent(new RectTransform(new Point(88, 88), root.RectTransform),
                (batch, component) => { if (Active == this && enabled) DrawImageCard(batch, component.Rect, hash); }) { CanBeFocused = false };
            bool arranging = false;
            int previousWidth = -1; float previousScale = -1;
            Action layout = () =>
            {
                if (arranging) return;
                arranging = true;
                try
                {
                    int padding = Math.Max(1, GUI.IntScale(6));
                    int side = EmoteLayout.ImageSide(GUI.Scale, popup ? GUI.IntScale(88) : Math.Max(1, root.Rect.Width - padding * 2));
                    image.RectTransform.MinSize = image.RectTransform.MaxSize = new Point(side, side);
                    image.RectTransform.Resize(new Point(side, side), false);
                    int headerHeight = header == null ? 0 : Math.Max(1, GUI.IntScale(22));
                    int gap = header == null ? 0 : Math.Max(1, GUI.IntScale(4));
                    int width = popup ? Math.Max(side + padding * 2, header == null ? 0 : GUI.IntScale(200)) : root.Rect.Width;
                    if (header != null)
                    {
                        header.RectTransform.AbsoluteOffset = new Point(padding, padding);
                        header.RectTransform.Resize(new Point(Math.Max(1, width - padding * 2), headerHeight), false);
                    }
                    image.RectTransform.AbsoluteOffset = new Point(padding, padding + headerHeight + gap);
                    root.RectTransform.IsFixedSize = false;
                    root.RectTransform.Resize(new Point(width, side + padding * 2 + headerHeight + gap), false);
                    if (popup) root.RectTransform.IsFixedSize = true;
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
            imageRows.Add(new ImageRow { Root = root, Header = header, Image = image, Layout = layout });
            layout();
        }
        private void DrawImageCard(SpriteBatch batch, Rectangle rect, string hash)
        {
            // Transparent image-only content. Sender is a separate header; body has no filename, timestamp or transport ID.
            DrawAsset(batch, rect, hash);
        }
        private static void BubblePostfix(Character __instance, SpriteBatch spriteBatch, Camera cam)
        {
            var plugin = Active;
            if (plugin == null || !plugin.enabled || __instance.Info == null || !__instance.Enabled || __instance.InvisibleTimer > 0 || __instance.IsDead
                || !plugin.bubbles.TryGetValue(__instance.ID, out var bubble) || !ReferenceEquals(bubble.Character, __instance) || bubble.Until <= Now) return;
            int side = GUI.IntScale(80);
            Vector2 position = cam.WorldToScreen(__instance.DrawPosition) - new Vector2(side / 2, GUI.IntScale(150));
            var rect = new Rectangle((int)position.X, (int)position.Y, side, side);
            if (new Rectangle(0, 0, GameMain.GraphicsWidth, GameMain.GraphicsHeight).Intersects(rect)) plugin.DrawImageCard(spriteBatch, rect, bubble.Hash);
        }
        private void ClearPresentation()
        {
            foreach (var row in imageRows) row.Detach();
            imageRows.Clear();
        }
    }
}
