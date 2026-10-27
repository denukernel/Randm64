using System;
using System.Collections.Generic;
using System.IO;

namespace Sm64DecompLevelViewer.Services;
    public class M64Track
    {
        public byte ChannelIndex { get; set; }
        public byte Instrument { get; set; } = 0;
        public byte Volume { get; set; } = 127;
        public byte Bank { get; set; } = 0;
        public List<M64Note> Notes { get; set; } = new();
        public List<M64PitchChange> PitchChanges { get; set; } = new();

        public double GetPitchScale(double tick) =>
            PitchChanges.LastOrDefault(change => change.Tick <= tick, new M64PitchChange(0, 1)).Scale;

        public IReadOnlyList<M64PitchFrame> GetPitchFrames(double startTick, double endTick, M64TempoMap tempoMap, int sampleRate)
        {
            var frames = new List<M64PitchFrame> { new(0, GetPitchScale(startTick)) };
            foreach (var change in PitchChanges.Where(change => change.Tick > startTick && change.Tick < endTick))
                frames.Add(new((long)Math.Round(tempoMap.GetDurationSeconds(startTick, change.Tick - startTick) * sampleRate), change.Scale));
            return frames;
        }
    }
