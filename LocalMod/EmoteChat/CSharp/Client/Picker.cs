using System;
using System.Collections.Generic;
using System.Linq;
using Barotrauma;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EmoteChat
{
    public partial class Plugin
    {
        private PickerPaint uiPaint;
        private readonly Dictionary<string, string> localNames = new Dictionary<string, string>();
        private readonly List<string> recent = new List<string>();
        private string previewHash, search = "";
        private bool recentOnly;
        private int pickerWidth, pickerHeight;
        private float pickerScale, uiScale;
        private int PickerPixels(int value) => Math.Max(1, (int)(value * uiScale));
        private GUIListBox pickerList;
        private GUITextBlock previewLabel;
        private GUIButton sendButton;
        private GUITextBox pickerSearch;
        private void ClosePicker()
        {
            pickerSearch?.Deselect(); pickerSearch = null;
            if (picker != null) { GUIMessageBox.MessageBoxes.Remove(picker); picker.RectTransform.Parent = null; }
            picker = null; pickerList = null; status = null; previewLabel = null; sendButton = null;
        }
        private string LocalLabel(string hash)
        {
            string name = localNames.TryGetValue(hash, out var label) && label != null ? label : "表情";
            return new string(name.Where(c => !char.IsControl(c) && c != '‖' && c != '<' && c != '>').Take(20).ToArray());
        }
        private GUITextBlock Label(Rectangle rect, GUIComponent parent, string text, Color color, bool small = true)
        {
            return new GUITextBlock(new RectTransform(rect.Size, parent.RectTransform) { AbsoluteOffset = rect.Location }, text,
                font: small ? GUIStyle.SmallFont : GUIStyle.Font, textColor: color, style: null) { CanBeFocused = false, AutoScaleHorizontal = true, AutoScaleVertical = true };
        }
        private GUIButton Pill(Rectangle rect, GUIComponent parent, string label, Func<bool> action, Func<bool> selected = null, bool primary = false)
        {
            var button = new GUIButton(new RectTransform(rect.Size, parent.RectTransform) { AbsoluteOffset = rect.Location }, style: null, color: Color.Transparent);
            new GUICustomComponent(new RectTransform(Vector2.One, button.RectTransform), (sb, c) =>
            {
                bool hover = button.State == GUIComponent.ComponentState.Hover || button.State == GUIComponent.ComponentState.Pressed;
                Color color = primary ? PickerPaint.Accent : selected?.Invoke() == true ? new Color(53, 66, 98) : hover ? new Color(57, 63, 77) : PickerPaint.Card;
                uiPaint?.Round(sb, c.Rect, button.Enabled ? color : color * 0.35f, PickerPixels(10));
            }) { CanBeFocused = false };
            new GUITextBlock(new RectTransform(Vector2.One, button.RectTransform), label, textColor: PickerPaint.Ink, font: GUIStyle.SmallFont, textAlignment: Alignment.Center, style: null) { CanBeFocused = false, AutoScaleHorizontal = true, AutoScaleVertical = true };
            button.OnClicked = (_, __) => action();
            return button;
        }
        private void OpenPicker()
        {
            if (picker != null) { ClosePicker(); return; }
            ReadLibrary();
            if (previewHash != null && !localFiles.ContainsKey(previewHash)) previewHash = null;
            pickerWidth = GameMain.GraphicsWidth; pickerHeight = GameMain.GraphicsHeight; pickerScale = GUI.Scale;
            uiScale = Math.Max(0.6f, Math.Min(GUI.Scale, Math.Min(pickerWidth / 1000f, pickerHeight / 700f)));
            int S(int n) => PickerPixels(n);
            int width = Math.Min(S(920), pickerWidth - S(40)), height = Math.Min(S(620), pickerHeight - S(40));
            width = Math.Max(300, width); height = Math.Max(300, height);
            picker = new GUIFrame(new RectTransform(Vector2.One, GUI.Canvas), style: null, color: Color.Transparent);
            new GUICustomComponent(new RectTransform(Vector2.One, picker.RectTransform), (sb, c) => GUI.DrawRectangle(sb, c.Rect, Color.Black * 0.55f, true)) { CanBeFocused = false };
            var panel = new GUIFrame(new RectTransform(new Point(width, height), picker.RectTransform, Anchor.Center), style: null, color: Color.Transparent);
            new GUICustomComponent(new RectTransform(Vector2.One, panel.RectTransform), (sb, c) =>
            {
                var shadow = c.Rect; shadow.Offset(0, S(8)); shadow.Inflate(S(5), S(5));
                uiPaint?.Round(sb, shadow, Color.Black * 0.25f, S(24));
                uiPaint?.Round(sb, c.Rect, PickerPaint.Surface, S(20));
            }) { CanBeFocused = false };
            int margin = S(24), top = S(110), footer = S(74), gap = S(16);
            Label(new Rectangle(margin, S(20), width - S(150), S(32)), panel, "表情包  /  Emotes", PickerPaint.Ink, false);
            Label(new Rectangle(margin, S(54), width - S(150), S(24)), panel, "把想说的，变成一张图。", PickerPaint.Muted);
            if (width >= S(680)) Pill(new Rectangle(width - S(208), S(22), S(124), S(32)), panel, "接收表情", () =>
            {
                enabled = !enabled; selectedHash = null; selectedAsset = null; loads.Clear();
                session?.Dispose(); session = null; cache.Clear(); requests.Clear(); bubbles.Clear();
                if (enabled) NewSession();
                return true;
            }, () => enabled);
            Pill(new Rectangle(width - S(68), S(22), S(40), S(32)), panel, "×", () => { ClosePicker(); return true; });
            Pill(new Rectangle(margin, top, S(78), S(34)), panel, "全部", () => { recentOnly = false; RebuildCards(); return true; }, () => !recentOnly);
            Pill(new Rectangle(margin + S(86), top, S(78), S(34)), panel, "最近", () => { recentOnly = true; RebuildCards(); return true; }, () => recentOnly);
            bool detail = width >= S(680);
            int previewWidth = detail ? S(210) : 0;
            int gridWidth = width - margin * 2 - (detail ? previewWidth + gap : 0);
            int searchX = margin + S(180), searchWidth = gridWidth - S(180);
            if (searchWidth > S(90))
            {
                var field = new GUIFrame(new RectTransform(new Point(searchWidth, S(34)), panel.RectTransform) { AbsoluteOffset = new Point(searchX, top) }, style: null);
                new GUICustomComponent(new RectTransform(Vector2.One, field.RectTransform), (sb, c) => uiPaint?.Round(sb, c.Rect, PickerPaint.Card, S(10))) { CanBeFocused = false };
                var input = new GUITextBox(new RectTransform(new Vector2(0.9f, 1), field.RectTransform, Anchor.Center), search, textColor: PickerPaint.Ink, font: GUIStyle.SmallFont, style: null, createPenIcon: false) { ToolTip = "搜索本地表情名称" };
                input.TextBlock.AutoScaleHorizontal = true; input.TextBlock.AutoScaleVertical = true;
                pickerSearch = input;
                var placeholder = Label(new Rectangle(S(8), 0, searchWidth - S(16), S(34)), field, "搜索你的表情…", PickerPaint.Muted);
                placeholder.Visible = search.Length == 0;
                input.OnTextChanged += (_, text) => { search = text.Length > 80 ? text.Substring(0, 80) : text; placeholder.Visible = text.Length == 0; RebuildCards(); return true; };
            }
            pickerList = new GUIListBox(new RectTransform(new Point(gridWidth, height - top - S(48) - footer), panel.RectTransform) { AbsoluteOffset = new Point(margin, top + S(48)) }, style: null) { AutoHideScrollBar = false, ScrollBarVisible = false, ResizeContentToMakeSpaceForScrollBar = false };
            new GUICustomComponent(new RectTransform(new Point(4, pickerList.Rect.Height), panel.RectTransform) { AbsoluteOffset = new Point(margin + gridWidth - 4, top + S(48)) }, (sb, c) =>
            {
                if (pickerList != null && pickerList.BarSize < 1)
                {
                    int thumbHeight = Math.Max(S(24), (int)(c.Rect.Height * pickerList.BarSize));
                    uiPaint?.Round(sb, new Rectangle(c.Rect.X, c.Rect.Y + (int)((c.Rect.Height - thumbHeight) * pickerList.BarScroll), c.Rect.Width, thumbHeight), PickerPaint.Muted * 0.45f, 2);
                }
            }) { CanBeFocused = false };
            if (detail)
            {
                int x = width - margin - previewWidth;
                var detailPanel = new GUIFrame(new RectTransform(new Point(previewWidth, height - top - footer), panel.RectTransform) { AbsoluteOffset = new Point(x, top) }, style: null);
                new GUICustomComponent(new RectTransform(Vector2.One, detailPanel.RectTransform), (sb, c) => uiPaint?.Round(sb, c.Rect, new Color(29, 33, 41), S(16))) { CanBeFocused = false };
                Label(new Rectangle(S(18), S(14), previewWidth - S(36), S(24)), detailPanel, "预览", PickerPaint.Muted);
                new GUICustomComponent(new RectTransform(new Point(previewWidth - S(36)), detailPanel.RectTransform) { AbsoluteOffset = new Point(S(18), S(58)) }, (sb, c) =>
                { if (previewHash != null) { QueueLoad(previewHash); DrawAsset(sb, c.Rect, previewHash); } else DrawPending(sb, c.Rect); }) { CanBeFocused = false };
                previewLabel = Label(new Rectangle(S(18), previewWidth + S(44), previewWidth - S(36), S(30)), detailPanel, previewHash == null ? "选择一张表情" : LocalLabel(previewHash), PickerPaint.Ink);
            }
            int bottom = height - S(54);
            status = Label(new Rectangle(margin, bottom - S(6), Math.Max(40, width - S(width >= S(680) ? 380 : 250)), S(28)), panel, "选择一张表情", PickerPaint.Muted);
            if (width >= S(680)) Pill(new Rectangle(width - S(338), bottom, S(104), S(32)), panel, "动画开关", () => { animate = !animate; return true; }, () => animate);
            Pill(new Rectangle(width - S(222), bottom, S(88), S(32)), panel, "刷新", () => { ReadLibrary(); RebuildCards(); return true; });
            sendButton = Pill(new Rectangle(width - S(122), bottom, S(98), S(32)), panel, "发送", () => { if (previewHash != null) { Select(previewHash); } return true; }, primary: true);
            sendButton.Enabled = previewHash != null;
            GUIMessageBox.MessageBoxes.Add(picker);
            RebuildCards();
        }
        private void RebuildCards()
        {
            if (pickerList == null) return;
            pickerList.Content.ClearChildren();
            var keys = (recentOnly ? recent.Where(localFiles.ContainsKey) : localFiles.Keys.AsEnumerable()).Where(h => search.Length == 0 || LocalLabel(h).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
            int gap = Math.Max(4, PickerPixels(10));
            int available = Math.Max(1, pickerList.Content.Rect.Width - gap);
            int columns = Math.Max(2, available / Math.Max(1, PickerPixels(116)));
            int side = Math.Max(32, (available - (columns - 1) * gap) / columns);
            GUIFrame row = null;
            for (int i = 0; i < keys.Length; i++)
            {
                string hash = keys[i];
                if (i % columns == 0) row = new GUIFrame(new RectTransform(new Vector2(1, 0), pickerList.Content.RectTransform) { MinSize = new Point(0, side + gap), MaxSize = new Point(int.MaxValue, side + gap) }, style: null);
                var button = new GUIButton(new RectTransform(new Point(side), row.RectTransform) { AbsoluteOffset = new Point((i % columns) * (side + gap), 0) }, style: null, color: Color.Transparent) { ToolTip = LocalLabel(hash) };
                new GUICustomComponent(new RectTransform(Vector2.One, button.RectTransform), (sb, c) =>
                {
                    bool chosen = previewHash == hash;
                    uiPaint?.Round(sb, c.Rect, chosen ? PickerPaint.Accent : button.State == GUIComponent.ComponentState.Hover ? new Color(58, 64, 78) : PickerPaint.Card, PickerPixels(14));
                    Rectangle inner = c.Rect; inner.Inflate(-PickerPixels(chosen ? 3 : 1), -PickerPixels(chosen ? 3 : 1));
                    uiPaint?.Round(sb, inner, PickerPaint.Card, PickerPixels(12));
                    inner.Inflate(-PickerPixels(12), -PickerPixels(12));
                    QueueLoad(hash); DrawAsset(sb, inner, hash);
                }) { CanBeFocused = false };
                button.OnClicked = (_, __) => { previewHash = hash; if (previewLabel != null) previewLabel.Text = LocalLabel(hash); if (sendButton != null) sendButton.Enabled = true; return true; };
            }
            if (keys.Length == 0) Label(new Rectangle(gap, gap, available - gap, PickerPixels(64)), pickerList.Content, recentOnly ? "还没有最近发送的表情" : search.Length > 0 ? "没有找到匹配的表情" : "先用导入器添加表情，再点「刷新」", PickerPaint.Muted);
            pickerList.RecalculateChildren(); pickerList.UpdateScrollBarSize();
        }
    }
}
