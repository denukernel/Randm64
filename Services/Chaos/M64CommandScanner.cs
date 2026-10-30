using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Sm64DecompLevelViewer.Services.Chaos;

internal enum M64ValueKind { Pitch, Velocity, Gate, Tempo, Volume, Pan, Reverb, PitchBend, Release, Instrument }
internal sealed record M64Value(int Offset, M64ValueKind Kind, int Channel, int Bank, int Instrument, bool Shared = false, bool Large = false);

/// <summary>Conservative static walk of SM64's three script interpreters (src/audio/seqplayer.c).
/// Unknown, dynamic and self-modifying flow is refused rather than guessed.</summary>
internal sealed class M64CommandScanner
{
    private enum Script { Sequence, Channel, Layer }
    private sealed record Frame(int Address, bool Loop);
    private sealed record State(int Position, Script Script, int Channel = -1, bool Large = false,
        int Instrument = -1, int Bank = 256, string Stack = "", string Layers = "");
    private readonly byte[] _data;
    private readonly bool _modern, _shindou;
    private readonly Queue<State> _pending = new();
    private readonly HashSet<State> _visited = new();
    private readonly Dictionary<int, string> _roles = new();
    private readonly Dictionary<int, M64Value> _values = new();
    private readonly Dictionary<string, Frame[]> _stacks = new() { [""] = Array.Empty<Frame>() };
    private int _instructions;
    private int _channelDepth;
    private readonly Dictionary<int, State> _channels = new();

    private M64CommandScanner(byte[] data, string region)
    {
        _data = data;
        _modern = region is "eu" or "sh" or "cn";
        _shindou = region is "sh" or "cn";
    }

    internal static IReadOnlyList<M64Value> Scan(byte[] data, string region)
    {
        if (region is not ("us" or "jp" or "eu" or "sh" or "cn"))
            throw new InvalidDataException("Unknown M64 region. Use a us, jp, eu, sh or cn sequence folder.");
        if (data.Length == 0 || data.Length > 65535)
            throw new InvalidDataException("M64 script must contain 1–65535 bytes.");
        var scanner = new M64CommandScanner(data, region);
        // Decomp .m64 files are raw scripts, not MIDI/container files.
        scanner.Enqueue(new State(0, Script.Sequence));
        while (scanner._pending.TryDequeue(out var state)) scanner.Walk(state);
        return scanner._values.Values.OrderBy(v => v.Offset).ToArray();
    }

    private void Enqueue(State state)
    {
        if (state.Position < 0 || state.Position >= _data.Length)
            throw new InvalidDataException($"M64 pointer outside script: 0x{state.Position:X}.");
        _pending.Enqueue(state);
    }

    private void Mark(int offset, string role)
    {
        if (_roles.TryGetValue(offset, out var old) && old != role)
            throw new InvalidDataException($"Ambiguous M64 command/data at 0x{offset:X}.");
        _roles[offset] = role;
    }

    private int Read(ref int pos, string role)
    {
        if ((uint)pos >= (uint)_data.Length) throw new InvalidDataException("Truncated M64 command.");
        Mark(pos, role);
        return _data[pos++];
    }

    private int Word(ref int pos, string role) => (Read(ref pos, role) << 8) | Read(ref pos, role);
    private void Compressed(ref int pos, string role)
    {
        // High seven bits followed by *eight* low bits; never a MIDI VLQ loop.
        if ((Read(ref pos, role) & 0x80) != 0) Read(ref pos, role);
    }

    private void Value(ref int pos, State state, M64ValueKind kind, string role)
    {
        int offset = pos;
        Read(ref pos, role);
        AddValue(offset, state, kind);
    }

    private void AddValue(int offset, State state, M64ValueKind kind)
    {
        var value = new M64Value(offset, kind, state.Channel, state.Bank, state.Instrument, Large: kind == M64ValueKind.Pitch && state.Large);
        if (_values.TryGetValue(offset, out var prior) && (prior.Shared || prior != value))
        {
            if (prior.Kind != kind) throw new InvalidDataException("M64 operand has conflicting types.");
            // Shared code with different instruments/banks is not a safe pitch/instrument mutation target.
            value = value with { Channel = -1, Bank = -1, Instrument = -1, Shared = true };
        }
        _values[offset] = value;
    }

    private string Push(State state, int address, bool loop)
    {
        var frames = _stacks[state.Stack];
        if (frames.Length >= 4) throw new InvalidDataException("M64 script stack exceeds the game's four entries.");
        string key = state.Stack + (loop ? "L" : "C") + address + ";";
        _stacks[key] = frames.Append(new Frame(address, loop)).ToArray();
        return key;
    }

    private string Pop(string stack) => stack[..(stack.TrimEnd(';').LastIndexOf(';') + 1)];

    private void Walk(State state)
    {
        // Channels retain format/instrument/bank when restarted by the sequence.
        var visited = state.Script == Script.Channel ? new HashSet<State>() : _visited;
        while (visited.Add(state))
        {
            if (++_instructions > 200000) throw new InvalidDataException("M64 scan limit reached; script left unchanged.");
            int pos = state.Position;
            string role = state.Script + ":" + pos;
            int cmd = Read(ref pos, role);
            var frames = _stacks[state.Stack];
            if (state.Script == Script.Channel)
            {
                _channels[state.Channel] = state;
                // Layers run after the channel yields, not at the setlayer opcode.
                if (cmd is 0xfd or 0xfe || (_shindou && cmd < 0x10) || (!_shindou && _modern && (cmd & 0xf0) == 0x60))
                    FlushLayers(state);
                if (!_modern && cmd == 0xf3) { FlushLayers(state); return; }
            }
            if (cmd == 0xff)
            {
                if (frames.Length == 0) return;
                if (frames[^1].Loop) throw new InvalidDataException("M64 ends inside an unfinished loop.");
                state = state with { Position = frames[^1].Address, Stack = Pop(state.Stack) };
                continue;
            }
            if (cmd == 0xfc)
            {
                int target = Word(ref pos, role);
                state = state with { Position = target, Stack = Push(state, pos, false) };
                if (target < 0 || target >= _data.Length) throw new InvalidDataException("M64 call target is outside script.");
                continue;
            }
            if (cmd == 0xfb || (state.Script != Script.Layer && cmd is 0xfa or 0xf9 or 0xf5))
            {
                int target = Word(ref pos, role);
                if (cmd == 0xfb)
                {
                    if (target >= _data.Length) throw new InvalidDataException("M64 jump target is outside script.");
                    state = state with { Position = target }; continue;
                }
                Enqueue(state with { Position = target });
            }
            else if (cmd == 0xf8)
            {
                Read(ref pos, role);
                state = state with { Stack = Push(state, pos, true) };
            }
            else if (cmd == 0xf7)
            {
                if (frames.Length == 0 || !frames[^1].Loop) throw new InvalidDataException("M64 loop end has no loop start.");
                // Explore another iteration AND the exit with the current interpreter state.
                Enqueue(state with { Position = frames[^1].Address });
                state = state with { Stack = Pop(state.Stack) };
            }
            else if (cmd == 0xf6 && state.Script == Script.Channel)
            {
                if (frames.Length == 0) throw new InvalidDataException("M64 stack underflow.");
                state = state with { Stack = Pop(state.Stack) };
            }
            else if ((_modern && state.Script != Script.Layer && cmd is 0xf4 or 0xf3 or 0xf2)
                || (_modern && state.Script == Script.Layer && cmd == 0xf4))
            {
                int displacement = (sbyte)Read(ref pos, role);
                Enqueue(state with { Position = pos + displacement });
                if (cmd == 0xf4) return;
            }
            else if (cmd == 0xfd && state.Script != Script.Layer) Compressed(ref pos, role);
            else if (cmd == 0xfe && state.Script != Script.Layer) { }
            else if (state.Script == Script.Layer) Layer(cmd, ref pos, ref state, role);
            else if (state.Script == Script.Channel) Channel(cmd, ref pos, ref state, role);
            else Sequence(cmd, ref pos, state, role);
            if (pos >= _data.Length) throw new InvalidDataException("M64 script runs past its end.");
            state = state with { Position = pos };
        }
    }

    private void Sequence(int cmd, ref int pos, State state, string role)
    {
        if ((cmd & 0xf0) == 0x90)
            StartChannel(cmd & 15, Word(ref pos, role));
        else if (cmd == 0xdd) Value(ref pos, state, M64ValueKind.Tempo, role);
        else if (cmd == 0xdb) Value(ref pos, state, M64ValueKind.Volume, role);
        else if (cmd is 0xd7 or 0xd6)
        {
            int mask = Word(ref pos, role);
            for (int channel = 0; channel < 16; channel++)
                if ((mask & (1 << channel)) != 0) _channels.Remove(channel);
        }
        else if (cmd == 0xc7) throw new InvalidDataException("Self-modifying sequence is unsupported for corruption.");
        else
        {
            int count = cmd switch
            {
                0xf1 => _modern ? 1 : 0, 0xf0 when _modern => 0, 0xf2 when !_modern => 1,
                0xdc or 0xd5 or 0xdf or 0xde or 0xd3 or 0xd0 or 0xcc or 0xc9 or 0xc8 => 1,
                0xda => _modern ? 3 : 1, 0xd9 => _modern ? 1 : 0,
                0xd7 or 0xd6 => 2, 0xd4 or 0xd1 or 0xd2 => 0,
                _ when cmd < 0xc0 && (cmd & 0xf0) is 0x00 or 0x10 or 0x20 or 0x40 or 0x50 or 0x60 or 0x70 or 0x80 or 0xa0 => 0,
                _ => throw Unsupported(cmd, state)
            };
            if (cmd is 0xd1 or 0xd2)
            {
                int table = Word(ref pos, role);
                Skip(ref table, 16, "short-note-table");
            }
            else Skip(ref pos, count, role);
        }
    }

    private void Channel(int cmd, ref int pos, ref State state, string role)
    {
        if ((!_shindou && cmd is >= 0x90 and <= 0x93) || (_shindou && cmd is >= 0x88 and <= 0x8b))
        {
            int target = Word(ref pos, role), index = cmd & 3;
            var layers = ParseLayers(state.Layers);
            layers[index] = target;
            state = state with { Layers = string.Join(";", layers.OrderBy(p => p.Key).Select(p => $"{p.Key}={p.Value}")) };
        }
        else if ((!_shindou && (cmd & 0xf0) == 0x10) || (_shindou && (cmd & 0xf0) == 0x20))
            StartChannel(cmd & 15, Word(ref pos, role));
        else if (cmd == 0xc1)
        {
            int instrument = _data.ElementAtOrDefault(pos);
            Value(ref pos, state, M64ValueKind.Instrument, role);
            state = state with { Instrument = instrument };
        }
        else if (cmd == 0xc6) state = state with { Bank = Read(ref pos, role) };
        else if (cmd == 0xda) ProtectEnvelope(Word(ref pos, role));
        else if (cmd == 0xc3) state = state with { Large = false };
        else if (cmd == 0xc4) state = state with { Large = true };
        else if ((!_shindou && cmd is >= 0xa0 and <= 0xa3) || (_shindou && cmd is >= 0x90 and <= 0x93))
        {
            var layers = ParseLayers(state.Layers);
            layers.Remove(cmd & 3);
            state = state with { Layers = string.Join(";", layers.OrderBy(p => p.Key).Select(p => $"{p.Key}={p.Value}")) };
        }
        else if (cmd is 0xdf or 0xdd or 0xd4 or 0xd3 or 0xd9)
            Value(ref pos, state, cmd switch { 0xdf => M64ValueKind.Volume, 0xdd => M64ValueKind.Pan,
                0xd4 => M64ValueKind.Reverb, 0xd3 => M64ValueKind.PitchBend, _ => M64ValueKind.Release }, role);
        else if (cmd is 0xc5 or 0xc7 or 0xcb or 0xe4 or 0xeb or 0xe7 or 0xe8
            || (!_shindou && (cmd & 0xf0) == 0xb0) || (_shindou && cmd is >= 0x98 and <= 0xbf))
            throw new InvalidDataException($"Dynamic/self-modifying channel command 0x{cmd:X2} is unsupported for corruption.");
        else
        {
            int count = cmd switch
            {
                0xf1 => _modern ? 1 : 0, 0xf0 when _modern => 0, 0xf2 when !_modern => 1,
                0xe0 or 0xdc or 0xdb or 0xd8 or 0xd7 or 0xd6 or 0xd2 or 0xd1 or 0xd0
                    or 0xcc or 0xc9 or 0xc8 or 0xca or 0xe3 => 1,
                0xe5 or 0xe6 or 0xe9 when _modern => 1,
                0xc2 or 0xde => 2,
                0xe1 or 0xe2 => 3, 0xea => 0, 0xec when _modern => 0,
                _ when cmd < 0xc0 && (cmd & 0xf0) is 0x30 or 0x40 => 1,
                _ when cmd < 0xc0 && (cmd & 0xf0) is 0x00 or 0x20 or 0x50 or 0x60 or 0x70 or 0x80 or 0xa0 => 0,
                _ => throw Unsupported(cmd, state)
            };
            Skip(ref pos, count, role);
        }
    }

    private void StartChannel(int channel, int target)
    {
        if (++_channelDepth > 16) throw new InvalidDataException("Recursive channel starts are unsupported.");
        try
        {
            var previous = _channels.GetValueOrDefault(channel) ?? new State(target, Script.Channel, channel);
            Walk(previous with { Position = target, Stack = "", Layers = "" });
        }
        finally { _channelDepth--; }
    }

    private static Dictionary<int, int> ParseLayers(string layers) => layers.Length == 0 ? new() :
        layers.Split(';').Select(p => p.Split('=')).ToDictionary(p => int.Parse(p[0]), p => int.Parse(p[1]));

    private void FlushLayers(State state)
    {
        foreach (int target in ParseLayers(state.Layers).Values)
            Enqueue(state with { Position = target, Script = Script.Layer, Stack = "", Layers = "" });
    }

    private void Layer(int cmd, ref int pos, ref State state, string role)
    {
        if (cmd < 0xc0)
        {
            AddValue(pos - 1, state, M64ValueKind.Pitch);
            if ((cmd & 0xc0) == 0 || (state.Large && (cmd & 0xc0) == 0x40)) Compressed(ref pos, role);
            if (state.Large)
            {
                Value(ref pos, state, M64ValueKind.Velocity, role);
                if ((cmd & 0xc0) != 0x40) Value(ref pos, state, M64ValueKind.Gate, role);
            }
        }
        else if (cmd is 0xc0 or 0xc3) Compressed(ref pos, role);
        else if (cmd == 0xc6)
        {
            int instrument = _data.ElementAtOrDefault(pos);
            Value(ref pos, state, M64ValueKind.Instrument, role);
            state = state with { Instrument = instrument == 0xff ? state.Instrument : instrument };
        }
        else if (cmd == 0xc1) Value(ref pos, state, M64ValueKind.Velocity, role);
        else if (cmd == 0xca) Value(ref pos, state, M64ValueKind.Pan, role);
        else if (cmd == 0xc9) Value(ref pos, state, M64ValueKind.Gate, role);
        else if (cmd == 0xc7)
        {
            int mode = Read(ref pos, role);
            Read(ref pos, role);
            if ((mode & 0x80) != 0) Read(ref pos, role); else Compressed(ref pos, role);
        }
        else if (cmd == 0xcb && _modern)
        {
            ProtectEnvelope(Word(ref pos, role));
            Read(ref pos, role); // Keep the envelope-specific release setting.
        }
        else
        {
            int count = cmd switch { 0xc2 => 1, 0xc4 or 0xc5 or 0xc8 => 0, 0xcc when _modern => 0,
                _ when cmd is >= 0xd0 and <= 0xef => 0, _ => throw Unsupported(cmd, state) };
            Skip(ref pos, count, role);
        }
    }

    private void ProtectEnvelope(int start)
    {
        int pos = start;
        var visited = new HashSet<int>();
        while (visited.Add(pos))
        {
            if (visited.Count > 256) throw new InvalidDataException("M64 envelope scan limit reached.");
            int delay = (short)Word(ref pos, "envelope"), argument = (short)Word(ref pos, "envelope");
            if (delay == -2) pos = start + argument * 4;
            else if (delay <= 0) return;
        }
    }

    private void Skip(ref int pos, int count, string role) { for (int i = 0; i < count; i++) Read(ref pos, role); }
    private static InvalidDataException Unsupported(int cmd, State state) =>
        new($"Unsupported {state.Script} command 0x{cmd:X2} at 0x{state.Position:X}; file left unchanged.");
}
