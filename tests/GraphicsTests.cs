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
    static void PickerSnapshot(GraphicsDevice device, SpriteBatch batch)
    {
        using var painter = new PickerPaint(device);
        using var target = new RenderTarget2D(device, 1120, 820);
        using var labelsFile = File.OpenRead(".test-fixtures/ui-labels.png");
        using var labels = Texture2D.FromStream(device, labelsFile);
        using var artFile = File.OpenRead(".test-fixtures/ui-emotes.png");
        using var art = Texture2D.FromStream(device, artFile);
        device.SetRenderTarget(target); device.Clear(new Color(11, 16, 24));
        // Barotrauma's fork uses straight alpha (including Color.operator*).
        // Use the same blend as the in-game UI; premultiplied blend adds RGB in transparent corners.
        batch.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, SamplerState.LinearClamp);
        Rectangle R(int x, int y, int w, int h) => new Rectangle(100+x, 100+y, w, h);
        void Round(int x, int y, int w, int h, Color c, int radius=12) => painter.Round(batch, R(x,y,w,h), c, radius);
        void Text(int row, int x, int y, float opacity=1) => batch.Draw(labels, R(x,y,720,48), new Rectangle(0,row*48,720,48), Color.White*opacity);
        Round(-4,8,928,624,Color.Black*0.3f,24);
        Round(0,0,920,620,PickerPaint.Surface,20);
        Text(0,24,16); Text(1,24,54,0.8f);
        Round(712,22,124,32,new Color(53,66,98),10); Text(11,730,22);
        Round(852,22,40,32,PickerPaint.Card,10); Text(12,865,22);
        Round(24,110,78,34,new Color(53,66,98),10); Text(2,45,111);
        Round(110,110,78,34,PickerPaint.Card,10); Text(3,131,111);
        Round(204,110,466,34,PickerPaint.Card,10); Text(4,220,110,0.55f);
        Round(686,110,210,436,new Color(29,33,41),16); Text(5,704,124,0.65f);
        for(int i=0;i<15;i++)
        {
            int x=24+(i%5)*129,y=158+(i/5)*129;
            Round(x,y,119,119,i==0?PickerPaint.Accent:PickerPaint.Card,14);
            if(i==0) Round(x+3,y+3,113,113,PickerPaint.Card,12);
            batch.Draw(art,R(x+18,y+18,83,83),new Rectangle((i%8)*128,0,128,128),Color.White);
        }
        batch.Draw(art,R(704,174,174,174),new Rectangle(0,0,128,128),Color.White);
        Text(6,704,364); Text(7,24,560,0.65f);
        Round(582,566,104,32,PickerPaint.Card,10);Text(8,596,566);
        Round(698,566,88,32,PickerPaint.Card,10);Text(9,724,566);
        Round(798,566,98,32,PickerPaint.Accent,10);Text(10,829,566);
        batch.End(); device.SetRenderTarget(null);
        var colors=new Color[1120*820]; target.GetData(colors);
        Check(colors[101*1120+101] == new Color(11,16,24), "transparent rounded corner exactly preserves backdrop with native UI blend");
        Check(colors[190*1120+500] == PickerPaint.Surface, "panel center uses custom surface");
        Directory.CreateDirectory("dist");
        using var output=File.Create("dist/EmoteChat-0.2.0-ui-preview.png"); target.SaveAsPng(output,1120,820);
        Console.WriteLine("PASS production rounded painter: offscreen GPU UI fixture (synthetic art; not an in-game screenshot)");
    }
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
            PickerSnapshot(device, batch);
            Console.WriteLine("PASS real WARP/SpriteBatch: animated head+chat pixel readback, rectangle bounds and imported GIF frames");
        }
        finally { DestroyWindow(window); }
    }
}
