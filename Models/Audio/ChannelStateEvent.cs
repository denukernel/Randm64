using System;
using System.Collections.Generic;
using System.IO;

namespace Sm64DecompLevelViewer.Services;
    public class ChannelStateEvent
    {
        public int Tick { get; set; }
        public byte Volume { get; set; } = 127;
        public byte Pan { get; set; } = 64;
        public byte Reverb { get; set; } = 0;
    }
