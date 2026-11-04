using System;
using System.IO;

namespace EmoteChat
{
    public static class LibraryLocation
    {
        // Independent of game drive, working directory, portable EXE and Steam subscription directory.
        public static string UserLibrary => Path.Combine(OperatingSystem.IsWindows()
            ? Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Barotrauma", "EmoteChat", "Library");
    }
}
