namespace Sm64DecompLevelViewer.Services.Audio;
        internal class ActivePlayingNote
        {
            public M64Note? Source { get; set; }
            public M64Note? Snapshot { get; set; }
            public int VoiceId { get; set; }
            public byte Channel { get; set; }
            public byte Pitch { get; set; }
            public double EndTick { get; set; }
            public bool UsesSamples { get; set; }
        }
