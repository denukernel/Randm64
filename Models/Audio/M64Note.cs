using System;
using System.Collections.Generic;
using System.IO;

namespace Sm64DecompLevelViewer.Services;
    public class M64Note
    {
        public int StartTick { get; set; }
        public int DurationTicks { get; set; }
        public byte Pitch { get; set; }
        public byte Velocity { get; set; }
        public byte Instrument { get; set; }
        public byte LayerIndex { get; set; } = 0;
        public byte Gate { get; set; } = 204;
        public byte CommandType { get; set; } = 0;
        public byte ChannelVolume { get; set; } = 127;
        public byte ChannelPan { get; set; } = 64;
        public byte Reverb { get; set; } = 0;

        public M64Note Clone() => (M64Note)MemberwiseClone();

        public override string ToString()
        {
            return $"Note {Pitch} at {StartTick} (Duration: {DurationTicks})";
        }
    }
