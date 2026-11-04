using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace EmoteChat
{
    // Invoked only by explicit local Save/Favorite actions, on one background worker.
    public static class LocalLibraryStore
    {
        public const int MaxItems = 128;
        private static string CheckedRoot(string root, bool create)
        {
            string path = Path.GetFullPath(root);
            for (var dir = new DirectoryInfo(path); dir != null; dir = dir.Parent)
                if (dir.Exists && (dir.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked library folders are not supported");
            if (create) Directory.CreateDirectory(path);
            return path;
        }
        private static void CheckFile(string path, int limit)
        {
            var file = new FileInfo(path);
            if ((file.Attributes & FileAttributes.ReparsePoint) != 0 || file.Length > limit) throw new InvalidDataException("Invalid local library file");
        }
        private static byte[] ReadChecked(string path, int maximum)
        {
            CheckFile(path, maximum);
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (stream.Length > maximum) throw new InvalidDataException("Local library file exceeds limit");
                byte[] data = new byte[(int)stream.Length];
                int read = 0;
                while (read < data.Length)
                {
                    int count = stream.Read(data, read, data.Length - read);
                    if (count == 0) throw new InvalidDataException("Local library file changed while reading");
                    read += count;
                }
                if (stream.ReadByte() != -1) throw new InvalidDataException("Local library file grew while reading");
                return data;
            }
        }
        public static void SaveAsset(string root, byte[] encoded, string hash)
        {
            // Checks the hash, dimensions and bounded decompression without full pixel allocation.
            Format.Decode(encoded, hash, false);
            root = CheckedRoot(root, true);
            string target = Path.Combine(root, hash + ".ecp");
            if (File.Exists(target))
            {
                CheckFile(target, Format.MaxFile);
                if (Format.Hash(ReadChecked(target, Format.MaxFile)) != hash) throw new InvalidDataException("Existing library asset is damaged");
                return;
            }
            if (Directory.EnumerateFiles(root, "*.ecp").Take(MaxItems).Count() >= MaxItems) throw new IOException("Library is full (128 emotes); remove unused files first");
            AtomicWrite(root, target, encoded, false);
        }
        public static HashSet<string> ReadFavorites(string root)
        {
            root = CheckedRoot(root, false);
            string path = Path.Combine(root, "favorites.json");
            var result = new HashSet<string>(StringComparer.Ordinal);
            if (!File.Exists(path)) return result;
            CheckFile(path, 16384);
            var hashes = JsonSerializer.Deserialize<string[]>(ReadChecked(path, 16384));
            if (hashes == null || hashes.Length > MaxItems) throw new InvalidDataException("Invalid favorites list");
            foreach (string hash in hashes)
                if (Format.IsHash(hash) && File.Exists(Path.Combine(root, hash + ".ecp"))) result.Add(hash);
            return result;
        }
        public static void WriteFavorites(string root, IEnumerable<string> hashes)
        {
            string[] values = hashes.Distinct(StringComparer.Ordinal).Take(MaxItems + 1).OrderBy(h => h, StringComparer.Ordinal).ToArray();
            if (values.Length > MaxItems || values.Any(h => !Format.IsHash(h))) throw new InvalidDataException("Invalid favorites list");
            root = CheckedRoot(root, true);
            foreach (string hash in values) CheckFile(Path.Combine(root, hash + ".ecp"), Format.MaxFile);
            string target = Path.Combine(root, "favorites.json");
            if (File.Exists(target)) CheckFile(target, 16384);
            AtomicWrite(root, target, JsonSerializer.SerializeToUtf8Bytes(values), true);
        }
        private static void AtomicWrite(string root, string target, byte[] data, bool replace)
        {
            string temp = Path.Combine(root, ".emote-" + Guid.NewGuid().ToString("N") + ".tmp");
            bool created = false;
            try
            {
                using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { created = true; file.Write(data, 0, data.Length); file.Flush(true); }
                File.Move(temp, target, replace);
            }
            finally { if (created && File.Exists(temp)) File.Delete(temp); }
        }
    }
}
