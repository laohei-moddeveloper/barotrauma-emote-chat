using System;

namespace EmoteChat
{
    // Pixel staging is shared by all views. GPU writes happen only before the game's Draw begins.
    public sealed class AnimationPlayback
    {
        public readonly Asset Asset;
        public readonly byte[] FramePixels;
        public int Frame { get; private set; } = -1;
        public double LastVisible = double.NegativeInfinity;
        public AnimationPlayback(Asset asset)
        {
            if (asset.Pixels == null) throw new ArgumentException("Decoded pixels required");
            Asset = asset; FramePixels = new byte[asset.Width * asset.Height * 4];
        }
        public int DesiredFrame(double now, bool animate) => animate ? Asset.Frame(now) : 0;
        public void Prepare(int frame)
        {
            if (frame < 0 || frame >= Asset.Delays.Length) throw new ArgumentOutOfRangeException(nameof(frame));
            Buffer.BlockCopy(Asset.Pixels, frame * FramePixels.Length, FramePixels, 0, FramePixels.Length);
        }
        public void Uploaded(int frame) { Frame = frame; }
    }
    public static class EmoteLayout
    {
        public static int ImageSide(float scale, int availableWidth) => Math.Max(1, Math.Min((int)(88 * scale), availableWidth));
        public static int RowHeight(int headerHeight, int imageHeight, int padding) => headerHeight + imageHeight + padding;
    }
}
