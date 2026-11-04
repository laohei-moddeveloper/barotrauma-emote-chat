using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace EmoteChat
{
    public partial class Plugin
    {
        private enum LibraryTab { All, Recent, Received, Favorites }
        private LibraryTab libraryTab;
        private readonly List<string> receivedEmotes = new List<string>();
        private readonly Dictionary<string, string> receivedSenders = new Dictionary<string, string>();
        private HashSet<string> favorites = new HashSet<string>(StringComparer.Ordinal);
        private Task<HashSet<string>> libraryWork;
        private bool cardsDirty;
        private string libraryNotice;
        private double libraryNoticeUntil;
        private Barotrauma.GUIButton saveButton, favoriteButton;
        private void RememberReceived(string hash, string sender)
        {
            receivedEmotes.Remove(hash); receivedEmotes.Insert(0, hash);
            receivedSenders[hash] = sender;
            if (receivedEmotes.Count > 32) { receivedSenders.Remove(receivedEmotes[32]); receivedEmotes.RemoveAt(32); }
            cardsDirty = true;
        }
        private void QueuePreview(string hash)
        {
            if (localFiles.ContainsKey(hash)) QueueLoad(hash);
            // Received previews use the existing chat download/cache, never start another transfer.
        }
        private void ReadFavoriteList()
        {
            try { favorites = LocalLibraryStore.ReadFavorites(library); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is JsonException || e is InvalidDataException)
            { favorites.Clear(); Log("Cannot read favorites: " + e.Message); }
        }
        private void LibraryNotice(string text) { libraryNotice = text; libraryNoticeUntil = Now + 5; }
        private bool StoreSelection(bool toggleFavorite)
        {
            if (previewHash == null || libraryWork != null) return true;
            Asset asset = cache.Get(previewHash);
            if (asset == null) { LibraryNotice("表情尚未就绪或缓存已过期，请让对方重新发送"); return true; }
            string root = library;
            bool add = !favorites.Contains(asset.Hash);
            var next = new HashSet<string>(favorites, StringComparer.Ordinal);
            if (toggleFavorite) { if (add) next.Add(asset.Hash); else next.Remove(asset.Hash); }
            // A strong reference pins just this validated asset until the explicit write finishes.
            libraryWork = Task.Run(() =>
            {
                LocalLibraryStore.SaveAsset(root, asset.Encoded, asset.Hash);
                if (toggleFavorite) LocalLibraryStore.WriteFavorites(root, next);
                return next;
            });
            LibraryNotice(toggleFavorite ? add ? "已保存并收藏" : "已取消收藏（保留图库图片）" : "已加入自己的图库");
            return true;
        }
        private void TickLibrary()
        {
            if (libraryWork != null && libraryWork.IsCompleted)
            {
                var task = libraryWork; libraryWork = null;
                if (task.Status == TaskStatus.RanToCompletion)
                { favorites = task.Result; ReadLibrary(); libraryNoticeUntil = Now + 5; cardsDirty = true; }
                else
                {
                    _ = task.Exception;
                    LibraryNotice("保存失败：请检查图库目录权限、文件或容量");
                    Log("Local library write failed: " + task.Exception?.GetBaseException().Message);
                }
            }
            if (cardsDirty && pickerList != null) { cardsDirty = false; RebuildCards(); }
            bool ready = previewHash != null && cache.Get(previewHash) != null && libraryWork == null;
            if (sendButton != null) sendButton.Enabled = previewHash != null && localFiles.ContainsKey(previewHash);
            if (saveButton != null)
            {
                saveButton.Enabled = ready && !localFiles.ContainsKey(previewHash);
                saveButton.ToolTip = previewHash != null && localFiles.ContainsKey(previewHash) ? "已在自己的图库中" : ready ? "保存在本机，之后可以发送" : "等待表情接收完成；缓存过期时请对方重发";
            }
            if (favoriteButton != null) favoriteButton.Enabled = ready;
        }
        private void CleanupLibrary()
        {
            if (libraryWork != null) libraryWork.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
            libraryWork = null; receivedEmotes.Clear(); receivedSenders.Clear(); favorites.Clear();
        }
    }
}
