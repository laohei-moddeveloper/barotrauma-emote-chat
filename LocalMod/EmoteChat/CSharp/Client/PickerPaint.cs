using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EmoteChat
{
    // One 16 KiB mask for every rounded surface. Created before SpriteBatch.Begin.
    public sealed class PickerPaint : IDisposable
    {
        private readonly Texture2D mask;
        public static readonly Color Surface = new Color(24, 27, 34), Card = new Color(36, 40, 49), Muted = new Color(151, 160, 178), Ink = new Color(241, 244, 250), Accent = new Color(104, 142, 255);
        public PickerPaint(GraphicsDevice device)
        {
            mask = new Texture2D(device, 64, 64, false, SurfaceFormat.Color);
            var pixels = new Color[64 * 64];
            for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
            {
                float dx = Math.Max(16 - x - 0.5f, Math.Max(0, x + 0.5f - 48));
                float dy = Math.Max(16 - y - 0.5f, Math.Max(0, y + 0.5f - 48));
                float alpha = Math.Clamp(16.5f - (float)Math.Sqrt(dx * dx + dy * dy), 0, 1);
                pixels[y * 64 + x] = Color.White * alpha;
            }
            mask.SetData(pixels);
        }
        public void Round(SpriteBatch batch, Rectangle rect, Color color, int radius = 12)
        {
            if (rect.Width <= 0 || rect.Height <= 0) return;
            int edge = Math.Max(1, Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2));
            for (int y = 0; y < 3; y++) for (int x = 0; x < 3; x++)
            {
                int dx = x == 0 ? rect.X : x == 1 ? rect.X + edge : rect.Right - edge;
                int dy = y == 0 ? rect.Y : y == 1 ? rect.Y + edge : rect.Bottom - edge;
                int w = x == 1 ? rect.Width - 2 * edge : edge, h = y == 1 ? rect.Height - 2 * edge : edge;
                if (w > 0 && h > 0) batch.Draw(mask, new Rectangle(dx, dy, w, h), new Rectangle(x == 0 ? 0 : x == 1 ? 16 : 48, y == 0 ? 0 : y == 1 ? 16 : 48, x == 1 ? 32 : 16, y == 1 ? 32 : 16), color);
            }
        }
        public void Dispose() => mask.Dispose();
    }
}
