using System;
using System.IO;
using System.Runtime.InteropServices;
using EmoteChat;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

// Real Direct3D/WARP textures and SpriteBatch, isolated from the user's game and Steam session.
static class GraphicsTests
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr CreateWindowExW(uint exStyle, string className, string title, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr hwnd);
    static void Check(bool value, string name) { if (!value) throw new Exception(name); }
    static void Main(string[] args)
    {
        GraphicsAdapter.UseDriverType = GraphicsAdapter.DriverType.FastSoftware;
        IntPtr window = CreateWindowExW(0, "STATIC", "EmoteChat isolated pixel test", 0, 0, 0, 320, 240, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        Check(window != IntPtr.Zero, "hidden rendering target window");
        try
        {
            using var device = new GraphicsDevice(GraphicsAdapter.DefaultAdapter, GraphicsProfile.Reach,
                new PresentationParameters { DeviceWindowHandle = window, BackBufferWidth = 320, BackBufferHeight = 240, IsFullScreen = false, DepthStencilFormat = DepthFormat.None });
            using var texture = new Texture2D(device, 8, 8, false, SurfaceFormat.Color);
            using var target = new RenderTarget2D(device, 320, 240);
            using var batch = new SpriteBatch(device);
            byte[] pixels = new byte[8 * 8 * 4 * 3];
            for (int f = 0; f < 3; f++) for (int p = 0; p < 64; p++) { pixels[f * 256 + p * 4 + f] = 255; pixels[f * 256 + p * 4 + 3] = 255; }
            var playback = new AnimationPlayback(new Asset(new string('0', 64), Array.Empty<byte>(), 8, 8, new[] { 50, 50, 50 }, pixels));
            var readback = new Color[320 * 240];
            for (int f = 0; f < 3; f++)
            {
                device.SetRenderTarget(null); device.Textures[0] = null;
                int frame = playback.DesiredFrame(f * 0.05 + 0.001, true);
                playback.Prepare(frame); texture.SetData(playback.FramePixels); playback.Uploaded(frame);
                device.SetRenderTarget(target); device.Clear(Color.Black);
                batch.Begin(SpriteSortMode.Deferred, BlendState.Opaque, SamplerState.PointClamp);
                batch.Draw(texture, new Rectangle(16, 16, 80, 80), Color.White); // overhead view
                batch.Draw(texture, new Rectangle(16, 128, 88, 88), Color.White); // chat view
                batch.End(); device.SetRenderTarget(null);
                target.GetData(readback);
                Color expected = f == 0 ? Color.Red : f == 1 ? new Color(0, 255, 0) : Color.Blue;
                Check(readback[40 * 320 + 40] == expected && readback[160 * 320 + 40] == expected, "both views change to animation frame " + f);
                Check(readback[127 * 320 + 16] == Color.Black && readback[216 * 320 + 16] == Color.Black, "chat does not bleed outside its rectangle");
            }
            if (args.Length > 0)
            {
                byte[] encoded = File.ReadAllBytes(args[0]);
                var gif = Format.Decode(encoded, Format.Hash(encoded), true);
                Check(gif.Delays.Length >= 2, "imported short GIF stays animated");
                var animation = new AnimationPlayback(gif);
                using var gifTexture = new Texture2D(device, gif.Width, gif.Height, false, SurfaceFormat.Color);
                var actual = new byte[gif.Width * gif.Height * 4];
                for (int f = 0; f < gif.Delays.Length; f++)
                {
                    device.Textures[0] = null; animation.Prepare(f); gifTexture.SetData(animation.FramePixels); gifTexture.GetData(actual);
                    Check(actual.AsSpan().SequenceEqual(animation.FramePixels), "imported GIF GPU bytes match frame " + f);
                }
            }
            Console.WriteLine("PASS real WARP/SpriteBatch: animated head+chat pixel readback, rectangle bounds and imported GIF frames");
        }
        finally { DestroyWindow(window); }
    }
}
