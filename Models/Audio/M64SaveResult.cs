using System;
using System.Collections.Generic;
using System.IO;

namespace Sm64DecompLevelViewer.Services;
    public class M64SaveResult
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public int FileSize { get; set; }
        public int OriginalSize { get; set; }
        public List<string> SavedChannels { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
        public List<string> ChannelMoves { get; set; } = new();
        public Dictionary<byte, byte> ChannelSources { get; set; } = new();
    }
