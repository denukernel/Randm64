using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Sm64DecompLevelViewer.Services.Chaos;

/// <summary>Seeded, value-only music mutations. Script layout, delays, tables and pointers stay intact.</summary>
internal sealed class M64Corruptor
{
    private readonly Random _random;
    private readonly Func<byte[], string, double, Random, M64MutationSettings, int[], byte[]>? _harmony;
    internal M64Corruptor(Random random, Func<byte[], string, double, Random, M64MutationSettings, int[], byte[]>? harmony = null)
    { _random = random; _harmony = harmony; }

    internal byte[] Mutate(byte[] source, string region, IEnumerable<int> selectedModes, double intensity, bool alienate = false, M64MutationSettings? settings = null, List<string>? diagnostics = null)
    {
        if (!double.IsFinite(intensity) || intensity < 0 || intensity > 100)
            throw new ArgumentOutOfRangeException(nameof(intensity));
        var modes = selectedModes.Distinct().ToArray();
        if (!alienate && modes.Length == 0)
            throw new InvalidDataException("Select at least one M64 corruption mode.");
        if (modes.Any(m => m < 0 || m > 39 || m == 27))
            throw new InvalidDataException("Sequence Pointer Slider is disabled: moving script pointers can crash SM64's audio thread. Deselect it in this preset.");
        if (!alienate && modes.Any(m => m is 38 or 39) && _harmony == null)
            throw new InvalidDataException("The harmony encoder is unavailable.");
        settings ??= new M64MutationSettings();
        settings.Validate();
        var allValues = M64CommandScanner.Scan(source, region);
        bool restricted = settings.Channels.Count != 16;
        if ((restricted || settings.ProtectPercussion || !settings.IncludeSequenceControls) && allValues.Any(v => v.Shared))
            throw new InvalidDataException("shared M64 operands have ambiguous ownership; targeted mutation was rejected");
        var percussionChannels = allValues.Where(v => v.Instrument == 127 && v.Channel >= 0).Select(v => v.Channel).ToHashSet();
        var values = allValues.Where(v =>
            (v.Channel < 0 ? settings.IncludeSequenceControls : settings.Channels.Contains(v.Channel)) &&
            !(settings.ProtectPercussion && (v.Instrument == 127 || percussionChannels.Contains(v.Channel)))).ToArray();
        byte[] data = (byte[])source.Clone();
        if (intensity == 0)
        {
            diagnostics?.Add($"{region}: zero intensity; 0 operands changed");
            return data;
        }
        double rate = intensity / 100;
        var pitches = values.Where(v => v.Kind == M64ValueKind.Pitch && v.Instrument is >= 0 and < 127 && v.Bank >= 0).ToArray();
        // Keep bank context: an ID valid in a different bank need not be valid here.
        var programs = allValues.Where(v => v.Kind == M64ValueKind.Instrument && v.Bank >= 0 && source[v.Offset] < 127)
            .GroupBy(v => v.Bank).ToDictionary(g => g.Key, g => g.Select(v => source[v.Offset]).Distinct().ToArray());
        bool Choose() => _random.NextDouble() < rate;
        void Set(M64Value value, int number) => data[value.Offset] = (byte)Math.Clamp(number, 0, 255);
        void Pitch(M64Value value, int number) => Set(value, (data[value.Offset] & 0xc0) | Math.Clamp(number, 0, 63));
        IEnumerable<M64Value> Kind(params M64ValueKind[] kinds) => values.Where(v => kinds.Contains(v.Kind));
        void Apply(IEnumerable<M64Value> targets, Func<M64Value, int> transform)
        {
            foreach (var value in targets) if (Choose()) Set(value, transform(value));
        }
        if (alienate)
        {
            foreach (var value in values)
            {
                if (!Choose()) continue;
                switch (value.Kind)
                {
                    case M64ValueKind.Pitch:
                        if (pitches.Contains(value)) Pitch(value, _random.Next(64));
                        break;
                    case M64ValueKind.Instrument: SwapInstrument(value); break;
                    case M64ValueKind.Tempo: Set(value, _random.Next(48, 241)); break;
                    case M64ValueKind.Gate: Set(value, _random.Next(256)); break;
                    case M64ValueKind.Release: Set(value, _random.Next(1, 129)); break;
                    case M64ValueKind.PitchBend: Set(value, _random.Next(256)); break;
                    default: Set(value, _random.Next(10, 128)); break;
                }
            }
        }
        else
        {
            // Stable mode ordering makes a seed reproducible; final melody replacements run last.
            foreach (int mode in modes.OrderBy(m => m is 26 or 29 ? m + 30 : m))
            {
                switch (mode)
                {
                    case 0:
                        int shift = _random.Next(-12, 13);
                        foreach (var v in pitches) if (Choose()) Pitch(v, (data[v.Offset] & 63) + shift);
                        break;
                    case 1:
                        Apply(Kind(M64ValueKind.Tempo), v => Math.Clamp(data[v.Offset] + _random.Next(-60, 61), 48, 240)); break;
                    case 2:
                        foreach (var v in Kind(M64ValueKind.Instrument)) if (Choose()) SwapInstrument(v);
                        break;
                    case 3: Apply(Kind(M64ValueKind.Gate), _ => _random.Next(256)); break;
                    case 4: Apply(Kind(M64ValueKind.Velocity), _ => _random.Next(10, 128)); break;
                    case 5:
                    case 24:
                        foreach (var group in pitches.GroupBy(v => v.Channel))
                        {
                            var targets = group.Where(_ => Choose()).ToArray();
                            var notes = targets.Select(v => data[v.Offset] & 63).ToArray();
                            if (mode == 24) Array.Reverse(notes); else _random.Shuffle(notes);
                            for (int i = 0; i < targets.Length; i++) Pitch(targets[i], notes[i]);
                        }
                        break;
                    case 6:
                        foreach (var v in pitches) if (Choose()) Pitch(v, (data[v.Offset] & 63) + (_random.Next(2) == 0 ? -12 : 12));
                        break;
                    case 7: Apply(Kind(M64ValueKind.Volume).Where(v => v.Channel >= 0), _ => 0); break;
                    case 8: Apply(Kind(M64ValueKind.Pan), _ => new[] { 0, 64, 127 }[_random.Next(3)]); break;
                    case 9: Apply(Kind(M64ValueKind.Reverb), _ => 127); break;
                    case 10: Apply(Kind(M64ValueKind.PitchBend), _ => _random.Next(256)); break;
                    // Gate is the SILENT fraction. Lower means longer; 255 means almost silent.
                    case 11: Apply(Kind(M64ValueKind.Gate), v => (int)(data[v.Offset] / (1 + 3 * rate))); break;
                    case 12: Apply(Kind(M64ValueKind.Gate), _ => _random.Next(240, 256)); break;
                    case 13:
                    case 14:
                        int[] scale = mode == 13 ? new[] { 0, 2, 4, 5, 7, 9, 11 } : new[] { 0, 2, 3, 5, 7, 8, 10 };
                        foreach (var v in pitches) if (Choose())
                        {
                            int p = data[v.Offset] & 63;
                            Pitch(v, Enumerable.Range(0, 64).Where(n => scale.Contains(n % 12)).MinBy(n => Math.Abs(n - p)));
                        }
                        break;
                    case 15:
                        foreach (var v in pitches) if (Choose()) Pitch(v, _random.Next(64));
                        break;
                    case 16: Apply(Kind(M64ValueKind.Velocity), v => Math.Clamp(127 - data[v.Offset], 0, 127)); break;
                    case 17: Apply(Kind(M64ValueKind.Volume), v => Math.Clamp(data[v.Offset] + (_random.Next(2) == 0 ? -30 : 30), 10, 127)); break;
                    case 18:
                        foreach (var group in pitches.Where(v => v.Channel >= 0).GroupBy(v => v.Channel))
                        {
                            int transpose = _random.Next(-12, 13);
                            foreach (var v in group) if (Choose()) Pitch(v, (data[v.Offset] & 63) + transpose);
                        }
                        break;
                    case 19:
                    case 25:
                        foreach (var group in pitches.GroupBy(v => v.Channel))
                            foreach (var v in group.Where((_, i) => i % 2 == 1))
                                if (Choose()) Pitch(v, (data[v.Offset] & 63) + (mode == 19 ? 12 : 3));
                        break;
                    case 20:
                        Apply(Kind(M64ValueKind.Velocity).Where(v => v.Instrument == 127), _ => _random.Next(64, 128)); break;
                    case 21:
                        int drift = 0;
                        Apply(Kind(M64ValueKind.Tempo), v => { drift = Math.Clamp(drift + _random.Next(-8, 9), -40, 40); return Math.Clamp(data[v.Offset] + drift, 48, 240); });
                        break;
                    case 22: Apply(Kind(M64ValueKind.Volume, M64ValueKind.Velocity), _ => 127); break;
                    case 23:
                        Apply(Kind(M64ValueKind.Velocity), v => _random.Next(2) == 0 ? 0 : data[v.Offset]);
                        Apply(Kind(M64ValueKind.Gate), _ => 0); break;
                    case 26:
                        foreach (var group in pitches.Where(v => v.Channel >= 0).GroupBy(v => v.Channel))
                        {
                            int target = data[group.First().Offset] & 63;
                            foreach (var v in group) if (Choose()) Pitch(v, target);
                        }
                        break;
                    case 28:
                        Apply(Kind(M64ValueKind.Gate), _ => 0);
                        // Keep the bank's ADSR/release rate; 255 does not mean infinite release.
                        break;
                    case 30:
                        foreach (var kind in new[] { M64ValueKind.Pitch, M64ValueKind.Velocity })
                        foreach (var group in (kind == M64ValueKind.Pitch ? pitches : Kind(kind)).GroupBy(v => v.Channel))
                        {
                            var targets = group.Where(_ => Choose()).ToArray();
                            var reversed = targets.Select(v => kind == M64ValueKind.Pitch ? data[v.Offset] & 63 : data[v.Offset]).Reverse().ToArray();
                            for (int i = 0; i < targets.Length; i++)
                                if (kind == M64ValueKind.Pitch) Pitch(targets[i], reversed[i]); else Set(targets[i], reversed[i]);
                        }
                        break;
                    case 31:
                        foreach (var group in pitches.GroupBy(v => v.Channel))
                        {
                            int root = data[group.First().Offset] & 63;
                            foreach (var v in group) if (Choose()) Pitch(v, 2 * root - (data[v.Offset] & 63));
                        }
                        break;
                    case 32:
                        foreach (var group in pitches.GroupBy(v => v.Channel))
                            foreach (var v in group.Where((_, i) => i % 2 == 1))
                                if (Choose()) Pitch(v, (data[v.Offset] & 63) + 7);
                        break;
                    case 33:
                        foreach (var group in pitches.GroupBy(v => v.Channel))
                        {
                            int index = 0;
                            foreach (var v in group) { if (Choose()) Pitch(v, (data[v.Offset] & 63) + index % 12); index++; }
                        }
                        break;
                    case 34:
                        int[] pentatonic = { 0, 2, 4, 7, 9 };
                        foreach (var v in pitches) if (Choose())
                        {
                            int p = data[v.Offset] & 63;
                            Pitch(v, Enumerable.Range(0, 64).Where(n => pentatonic.Contains(n % 12)).MinBy(n => Math.Abs(n - p)));
                        }
                        break;
                    case 35: Apply(Kind(M64ValueKind.Pan), v => 127 - Math.Min(127, (int)data[v.Offset])); break;
                    case 36: Apply(Kind(M64ValueKind.Tempo), v => Math.Clamp(data[v.Offset] * 2, 48, 240)); break;
                    case 37: Apply(Kind(M64ValueKind.Tempo), v => Math.Clamp(data[v.Offset] / 2, 48, 240)); break;
                    case 29:
                        int[] melody = { 0, 0, 1, 1, -2, -2, 1, 1, 0, 12, 0, 12, 1, 13, 1, 13, -2, 10, -2, 10, 1, 13, 1, 13 };
                        foreach (var group in pitches.GroupBy(v => v.Channel))
                        {
                            int root = (data[group.First().Offset] & 63) / 12 * 12, index = 0;
                            foreach (var v in group) { if (Choose()) Pitch(v, root + melody[index % melody.Length]); index++; }
                        }
                        break;
                }
            }
        }
        // Random low velocities are especially quiet because SM64 squares them.
        // Keep intentional muting/erasing, but protect other dynamics from attenuation.
        if (settings.PreventVolumeLoss)
            foreach (var v in values.Where(v => v.Kind is M64ValueKind.Volume or M64ValueKind.Velocity))
            {
                bool intentionalSilence = !alienate && data[v.Offset] == 0 &&
                    ((v.Kind == M64ValueKind.Volume && v.Channel >= 0 && modes.Contains(7)) ||
                     (v.Kind == M64ValueKind.Velocity && modes.Contains(23)));
                if (!intentionalSilence) data[v.Offset] = Math.Max(source[v.Offset], data[v.Offset]);
            }
        if (settings.MaxPitchDisplacement is int limit)
            foreach (var v in values.Where(v => v.Kind == M64ValueKind.Pitch))
            {
                int original = source[v.Offset] & 63;
                Pitch(v, Math.Clamp(data[v.Offset] & 63, Math.Max(0, original - limit), Math.Min(63, original + limit)));
            }
        diagnostics?.Add($"{region}: {allValues.Count(v => source[v.Offset] != data[v.Offset])} operands changed; {values.Length} eligible operands");
        foreach (int mode in modes)
            if (!values.Any(v => AppliesTo(mode, v))) diagnostics?.Add($"M64 mode {mode}: no applicable targets");
        // A changed operand must never alter instruction decoding or reachability.
        var check = M64CommandScanner.Scan(data, region);
        if (!allValues.Select(v => (v.Offset, v.Kind)).SequenceEqual(check.Select(v => (v.Offset, v.Kind))))
            throw new InvalidDataException("Mutation changed M64 script layout; output rejected.");
        if (!alienate && modes.Any(m => m is 38 or 39))
            data = _harmony!(data, region, intensity, _random, settings,
                modes.Where(m => m is 38 or 39).Order().Select(m => m == 38 ? 3 : -3).ToArray());
        return data;

        void SwapInstrument(M64Value value)
        {
            if (value.Bank >= 0 && source[value.Offset] < 127 && programs.TryGetValue(value.Bank, out var available))
                Set(value, available[_random.Next(available.Length)]);
        }
    }

    internal static bool AppliesTo(int mode, M64Value v) => mode switch
    {
        0 or 5 or 6 or 13 or 14 or 15 or 18 or 19 or 24 or 25 or 26 or 29 or 31 or 32 or 33 or 34 => v.Kind == M64ValueKind.Pitch && v.Instrument is >= 0 and < 127 && v.Bank >= 0,
        1 or 21 or 36 or 37 => v.Kind == M64ValueKind.Tempo,
        2 => v.Kind == M64ValueKind.Instrument && v.Bank >= 0 && v.Instrument != 127,
        3 or 11 or 12 or 28 => v.Kind == M64ValueKind.Gate,
        4 or 16 => v.Kind == M64ValueKind.Velocity,
        7 => v.Kind == M64ValueKind.Volume && v.Channel >= 0,
        8 or 35 => v.Kind == M64ValueKind.Pan,
        9 => v.Kind == M64ValueKind.Reverb,
        10 => v.Kind == M64ValueKind.PitchBend,
        17 => v.Kind == M64ValueKind.Volume,
        20 => v.Kind == M64ValueKind.Velocity && v.Instrument == 127,
        22 => v.Kind is M64ValueKind.Volume or M64ValueKind.Velocity,
        23 => v.Kind is M64ValueKind.Velocity or M64ValueKind.Gate,
        30 => (v.Kind == M64ValueKind.Pitch && v.Instrument is >= 0 and < 127 && v.Bank >= 0) || v.Kind == M64ValueKind.Velocity,
        38 or 39 => v.Kind == M64ValueKind.Pitch && v.Instrument >= 0,
        _ => false
    };

    internal void CorruptFile(string path, IEnumerable<int> modes, double intensity, bool alienate, bool accumulate, M64MutationSettings? settings = null, List<string>? diagnostics = null)
    {
        byte[] current = File.ReadAllBytes(path);
        string backup = path + ".bak";
        byte[] source = !accumulate && File.Exists(backup) ? File.ReadAllBytes(backup) : current;
        Audio.M64Integrity.RequireNonZero(path, current);
        Audio.M64Integrity.RequireNonZero(File.Exists(backup) && !accumulate ? backup : path, source);
        string region = Path.GetFileName(Path.GetDirectoryName(path))!.ToLowerInvariant();
        byte[] output = Mutate(source, region, modes, intensity, alienate, settings, diagnostics);
        Audio.M64Integrity.RequireNonZero(path, output);
        string staged = path + ".chaos-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(staged, output);
            if (!File.ReadAllBytes(path).SequenceEqual(current))
                throw new IOException("M64 changed during corruption; output was not applied.");
            if (!File.Exists(backup)) File.WriteAllBytes(backup, current);
            // Same-directory overwrite rename also works on the WSL UNC provider.
            File.Move(staged, path, overwrite: true);
        }
        finally { if (File.Exists(staged)) File.Delete(staged); }
    }
}
