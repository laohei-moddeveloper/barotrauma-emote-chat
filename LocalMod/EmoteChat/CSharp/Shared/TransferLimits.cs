namespace EmoteChat
{
    // Shared by sender, receiver and reassembly; changing only one silently discards valid chunks.
    public static class TransferLimits
    {
        public const int ChunkBytes = 768, WindowChunks = 32, PacketsPerTick = 8;
        public const int PeerBytesPerSecond = 128 * 1024, PeerBurstBytes = 8 * 1024;
        public const int GlobalBytesPerSecond = 128 * 1024, GlobalBurstBytes = 16 * 1024;
        public const int PeerReceiveBytesPerSecond = 192 * 1024, PeerPacketsPerSecond = 384;
        public const int GlobalReceiveBytesPerSecond = 256 * 1024, GlobalPacketsPerSecond = 1024;
    }
}
