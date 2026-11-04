using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using EmoteChat;

static class LibraryTests
{
    public static void Run(Action<bool, string> check, byte[] encoded)
    {
        string root = Path.Combine(Path.GetTempPath(), "emote-library-test-" + Guid.NewGuid().ToString("N"));
        string outside = root + "-outside", linked = root + "-link";
        string hash = Format.Hash(encoded), path = Path.Combine(root, hash + ".ecp");
        bool rejected(Action action) { try { action(); return false; } catch (Exception e) when (e is IOException || e is InvalidDataException || e is UnauthorizedAccessException) { return true; } }
        try
        {
            check(LocalLibraryStore.ReadFavorites(root).Count == 0 && !Directory.Exists(root), "reading preferences never creates an unsolicited library");
            LocalLibraryStore.SaveAsset(root, encoded, hash);
            check(File.ReadAllBytes(path).SequenceEqual(encoded), "explicit save persists exact validated animation bytes");
            DateTime original = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(path, original);
            LocalLibraryStore.SaveAsset(root, encoded, hash);
            check(File.GetLastWriteTimeUtc(path) == original, "duplicate save reuses existing resource without overwriting it");
            LocalLibraryStore.WriteFavorites(root, new[] { hash, hash });
            check(LocalLibraryStore.ReadFavorites(root).SetEquals(new[] { hash }), "favorites survive reload and deduplicate");
            LocalLibraryStore.WriteFavorites(root, Array.Empty<string>());
            check(LocalLibraryStore.ReadFavorites(root).Count == 0 && File.Exists(path), "unfavorite preserves saved emote");
            check(rejected(() => LocalLibraryStore.SaveAsset(root, encoded, "../outside")), "resource identity cannot select arbitrary paths");
            var bad = (byte[])encoded.Clone(); bad[0] ^= 1;
            check(rejected(() => LocalLibraryStore.SaveAsset(root, bad, hash)), "corrupted received bytes never enter the library");
            check(rejected(() => LocalLibraryStore.WriteFavorites(root, new[] { new string('f', 64) })), "cannot favorite missing assets");
            File.WriteAllText(Path.Combine(root, "favorites.json"), "[invalid-json]");
            bool invalid = false; try { LocalLibraryStore.ReadFavorites(root); } catch (System.Text.Json.JsonException) { invalid = true; }
            check(invalid, "damaged preferences fail without changing saved assets");
            File.WriteAllBytes(Path.Combine(root, "favorites.json"), new byte[16385]);
            check(rejected(() => LocalLibraryStore.ReadFavorites(root)), "preference file allocation is bounded");
            File.Delete(Path.Combine(root, "favorites.json"));
            check(!Directory.EnumerateFiles(root, "*.tmp").Any(), "atomic writes leave no temporary files");
            for (int i = 1; i < LocalLibraryStore.MaxItems; i++) File.WriteAllBytes(Path.Combine(root, i.ToString("x64") + ".ecp"), encoded);
            var other = (byte[])encoded.Clone(); other[12] = 101;
            check(rejected(() => LocalLibraryStore.SaveAsset(root, other, Format.Hash(other))), "explicit received saves obey 128 item disk budget");
            Directory.CreateDirectory(outside);
            try
            {
                Directory.CreateSymbolicLink(linked, outside);
                check(rejected(() => LocalLibraryStore.SaveAsset(linked, encoded, hash)) && !Directory.EnumerateFiles(outside).Any(), "library folder link cannot redirect a save");
                File.Delete(path);
                string protectedFile = Path.Combine(outside, "keep.ecp"); File.WriteAllText(protectedFile, "keep");
                File.CreateSymbolicLink(path, protectedFile);
                check(rejected(() => LocalLibraryStore.SaveAsset(root, encoded, hash)) && File.ReadAllText(protectedFile) == "keep", "linked target cannot overwrite another file");
            }
            catch (Exception e) when (e is PlatformNotSupportedException || e is UnauthorizedAccessException)
            { Console.WriteLine("Symbolic-link filesystem tests unavailable on this host"); }
        }
        finally
        {
            if (Directory.Exists(linked)) Directory.Delete(linked);
            if (Directory.Exists(root)) Directory.Delete(root, true);
            if (Directory.Exists(outside)) Directory.Delete(outside, true);
        }
    }
}
