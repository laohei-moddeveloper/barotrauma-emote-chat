using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;

namespace EmoteChat
{
    // Network data is ONLY this pixel format. No GIF/PNG/JPEG decoder runs in game.
    public static class Format
    {
        public const int MaxFile = 256 * 1024, MaxSide = 128, MaxFrames = 48;
        public const int MaxPixels = MaxSide * MaxSide * MaxFrames * 4;
        public static bool IsHash(string s)
        {
            if (s == null || s.Length != 64) return false;
            foreach (char c in s) if (!(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'f')) return false;
            return true;
        }
        public static string Hash(byte[] data)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "").ToLowerInvariant();
        }
        public static string Marker(string hash) => "[ec:" + hash + "]";
        public static string ParseMarker(string text)
        {
            if (text == null || text.Length != 69 || !text.StartsWith("[ec:", StringComparison.Ordinal) || text[68] != ']') return null;
            string hash = text.Substring(4, 64);
            return IsHash(hash) ? hash : null;
        }
        // Other chat hooks may decorate a message. Only a complete, validated token
        // can identify an asset; damaged tokens are hidden but never resolved.
        public static string FindMarker(string text)
        {
            if (text == null) return null;
            int start = text.IndexOf("[ec:", StringComparison.Ordinal);
            return start >= 0 && text.Length - start >= 69 ? ParseMarker(text.Substring(start, 69)) : null;
        }
        public static bool IsTransportText(string text) => text != null && text.IndexOf("[ec:", StringComparison.Ordinal) >= 0;
        public static Asset Decode(byte[] encoded, string expectedHash, bool pixels)
        {
            if (encoded == null || encoded.Length < 16 || encoded.Length > MaxFile || !IsHash(expectedHash) || Hash(encoded) != expectedHash)
                throw new InvalidDataException("Invalid asset size/hash");
            using (var input = new MemoryStream(encoded, false))
            using (var reader = new BinaryReader(input))
            {
                if (reader.ReadUInt32() != 0x31504345) throw new InvalidDataException("Not ECP1");
                int w = reader.ReadUInt16(), h = reader.ReadUInt16(), count = reader.ReadUInt16();
                if (reader.ReadUInt16() != 0 || w < 1 || h < 1 || w > MaxSide || h > MaxSide || count < 1 || count > MaxFrames)
                    throw new InvalidDataException("Asset limits exceeded");
                var delays = new int[count];
                int duration = 0;
                for (int i = 0; i < count; i++)
                {
                    delays[i] = reader.ReadUInt16(); duration += delays[i];
                    if (delays[i] < 20 || delays[i] > 10000) throw new InvalidDataException("Invalid delay");
                }
                if (duration > 10000) throw new InvalidDataException("Animation too long");
                // Raw DEFLATE; dimensions and frame count bound allocation BEFORE decompression.
                int frameBytes = checked(w * h * 4), total = checked(frameBytes * count);
                byte[] all = pixels ? new byte[total] : new byte[Math.Min(total, 8192)];
                using (var inflater = new DeflateStream(input, CompressionMode.Decompress, true))
                {
                    int done = 0;
                    while (done < total)
                    {
                        int read = inflater.Read(all, pixels ? done : 0, Math.Min(total - done, pixels ? total - done : all.Length));
                        if (read == 0) throw new InvalidDataException("Truncated pixels");
                        done += read;
                    }
                    if (inflater.ReadByte() != -1) throw new InvalidDataException("Excess pixels");
                }
                return new Asset(expectedHash, encoded, w, h, delays, pixels ? all : null);
            }
        }
    }
    public sealed class Asset
    {
        public readonly string Hash;
        public readonly byte[] Encoded, Pixels;
        public readonly int Width, Height, Duration;
        public readonly int[] Delays;
        public int Cost => Encoded.Length + (Pixels?.Length ?? 0);
        public Asset(string hash, byte[] encoded, int w, int h, int[] delays, byte[] pixels)
        {
            Hash = hash; Encoded = encoded; Width = w; Height = h; Delays = delays; Pixels = pixels;
            foreach (int delay in delays) Duration += delay;
        }
        public int Frame(double seconds)
        {
            int position = (int)((seconds * 1000) % Duration);
            for (int i = 0; i < Delays.Length; i++) { if (position < Delays[i]) return i; position -= Delays[i]; }
            return 0;
        }
    }
}
