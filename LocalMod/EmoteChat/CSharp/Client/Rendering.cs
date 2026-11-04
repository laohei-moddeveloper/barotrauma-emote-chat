using System;
using System.Collections.Generic;
using Barotrauma;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EmoteChat
{
    public partial class Plugin
    {
        private sealed class Picture : IDisposable
        {
            public readonly AnimationPlayback Playback;
            public Texture2D Texture;
            public Picture(Asset asset) { Playback = new AnimationPlayback(asset); }
            public void Dispose() => Texture?.Dispose();
        }
        private readonly List<Picture> renderQueue = new List<Picture>();
        private int renderCursor;
        private void RemovePicture(string hash)
        {
            if (!textures.TryGetValue(hash, out var picture)) return;
            picture.Dispose(); textures.Remove(hash); renderQueue.Remove(picture);
        }
        private static void RenderPrefix()
        {
            var plugin = Active;
            if (plugin == null || !plugin.enabled || plugin.renderQueue.Count == 0) return;
            var device = GameMain.GraphicsDeviceManager?.GraphicsDevice;
            if (device == null) return;
            double now = Now;
            int uploaded = 0, count = plugin.renderQueue.Count;
            // Round robin across visible assets; never let the first two animations starve the rest.
            for (int scanned = 0; scanned < count && uploaded < 2; scanned++)
            {
                plugin.renderCursor %= count;
                var picture = plugin.renderQueue[plugin.renderCursor++];
                var playback = picture.Playback;
                if (now - playback.LastVisible > 0.25) continue;
                int frame = playback.DesiredFrame(now, plugin.animate);
                if (picture.Texture != null && playback.Frame == frame) continue;
                playback.Prepare(frame);
                if (picture.Texture == null)
                    picture.Texture = new Texture2D(device, playback.Asset.Width, playback.Asset.Height, false, SurfaceFormat.Color);
                if (ReferenceEquals(device.Textures[0], picture.Texture)) device.Textures[0] = null;
                // Always upload a single frame at offset zero, before any SpriteBatch.Begin.
                picture.Texture.SetData(playback.FramePixels);
                playback.Uploaded(frame); uploaded++;
            }
        }
        private void DrawAsset(SpriteBatch batch, Rectangle rect, string hash)
        {
            var asset = cache.Get(hash);
            if (asset?.Pixels == null) { DrawPending(batch, rect); return; }
            if (!textures.TryGetValue(hash, out var picture))
            {
                picture = new Picture(asset); textures.Add(hash, picture); renderQueue.Add(picture);
            }
            picture.Playback.LastVisible = Now;
            if (picture.Texture == null) { DrawPending(batch, rect); return; }
            float scale = Math.Min((float)rect.Width / asset.Width, (float)rect.Height / asset.Height);
            int w = (int)(asset.Width * scale), h = (int)(asset.Height * scale);
            batch.Draw(picture.Texture, new Rectangle(rect.X + (rect.Width - w) / 2, rect.Y + (rect.Height - h) / 2, w, h), Color.White);
        }
        private static void DrawPending(SpriteBatch batch, Rectangle rect)
        {
            int dot = Math.Max(1, Math.Min(4, rect.Width / 8));
            for (int i = 0; i < 3; i++)
                GUI.DrawRectangle(batch, new Rectangle(rect.Center.X - 4 * dot + i * 3 * dot, rect.Center.Y, dot, dot), Color.LightGray, true);
        }
    }
}
