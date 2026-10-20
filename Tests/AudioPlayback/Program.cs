using System.Buffers.Binary;
using System.IO;
using System.Text;
using Sm64DecompLevelViewer.Services;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

static byte[] Aiff(short[] samples, int channels = 1, bool loop = false, bool vadpcm = false)
{
    using var output = new MemoryStream();
    using var writer = new BinaryWriter(output);
    void U16(int value) { writer.Write((byte)(value >> 8)); writer.Write((byte)value); }
    void U32(int value) { U16(value >> 16); U16(value); }
    void Id(string value) => writer.Write(Encoding.ASCII.GetBytes(value));
    Id("FORM"); U32(0); Id("AIFF");
    Id("COMM"); U32(18); U16(channels); U32(samples.Length / channels); U16(16);
    writer.Write(new byte[] { 0x40, 0x0E, 0xAC, 0x44, 0, 0, 0, 0, 0, 0 }); // 44100 Hz
    if (loop && !vadpcm)
    {
        Id("MARK"); U32(18); U16(2);
        U16(1); U32(200); U16(0); U16(2); U32(240); U16(0);
        Id("INST"); U32(20); writer.Write(new byte[8]); U16(1); U16(1); U16(2); writer.Write(new byte[6]);
    }
    if (vadpcm)
    {
        Id("APPL"); U32(64); Id("stoc"); writer.Write((byte)11); Id("VADPCMLOOPS");
        U16(1); U16(1); U32(200); U32(240); U32(-1); writer.Write(new byte[32]);
    }
    Id("SSND"); U32(8 + samples.Length * 2); U32(0); U32(0);
    foreach (short sample in samples) U16(sample);
    writer.Flush();
    byte[] data = output.ToArray();
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(4, 4), data.Length - 8);
    return data;
}

static short[] Render(SampleSynthPlayer player, int frames)
{
    short[] output = new short[frames * 2];
    player.MixAudio(output, frames);
    return output;
}

string temp = Path.Combine(AppContext.BaseDirectory, "Randm64-Audio-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temp);
try
{
    var previewTrack = new M64Track { Notes =
        [new() { StartTick = 0, DurationTicks = 48, Gate = 0 }, new() { StartTick = 96, DurationTicks = 24, Gate = 0 }] };
    var inserted = new M64Note { StartTick = 24, DurationTicks = 12, Gate = 0 };
    previewTrack.Notes.Add(inserted); // Editor appends without sorting.
    Check(M64PreviewSchedule.GetNotes(previewTrack.Notes, 12, 36).Single() == inserted,
        "Live inserted note must not be hidden behind a later note");
    previewTrack.Notes.RemoveAt(0);
    Check(M64PreviewSchedule.GetNotes(previewTrack.Notes, 24, 36).Single() == inserted,
        "Deleting an earlier note must not invalidate playback position");
    previewTrack.Notes[0].StartTick = 30;
    Check(M64PreviewSchedule.GetNotes(previewTrack.Notes, 24, 36).Select(n => n.StartTick).SequenceEqual(new[] { 24, 30 }),
        "Moved notes play in chronological order even when the model is unsorted");
    Check(!M64PreviewSchedule.GetNotes(previewTrack.Notes, 12, 24).Any() &&
        M64PreviewSchedule.GetNotes(previewTrack.Notes, 24, 30).Single() == inserted,
        "Adjacent timer windows play boundary notes exactly once");
    Check(M64PreviewSchedule.GetNotes(previewTrack.Notes, 32, 33, true).Count() == 2 &&
        !M64PreviewSchedule.GetNotes(previewTrack.Notes, 32, 33).Any(),
        "Resume restores sustained notes once without retriggering them on subsequent ticks");
    inserted.StartTick = 200;
    Check(M64PreviewSchedule.GetEndTick([previewTrack]) == 212, "Live edits extend the loop endpoint");
    previewTrack.Notes.Remove(inserted);
    Check(M64PreviewSchedule.GetEndTick([previewTrack]) == 54, "Deleting the final note updates the loop endpoint");
    Check(M64PreviewSchedule.GetNotes(previewTrack.Notes, 30, 31).Count() == 1,
        "Seeking or looping backwards does not retain a stale end index");
    Console.WriteLine("PASS: live preview inserts, deletes, moves, timer boundaries, resume, seeks, and loop extent");

    string tempoFixture = Path.Combine(temp, "tempo.m64");
    File.WriteAllBytes(tempoFixture, [0xdd, 120, 0xfd, 48, 0xdd, 60, 0xfd, 48,
        0xdc, 0xf6, 0xfd, 48, 0xdd, 180, 0xfd, 48, 0xfb, 0, 0]);
    var tempoService = new M64Service();
    tempoService.LoadM64(tempoFixture);
    var tempoMap = tempoService.TempoMap;
    Check(tempoService.Tempo == 120, "Initial tempo must not be replaced by the last event");
    Check(tempoMap.GetTempoAt(47) == 120 && tempoMap.GetTempoAt(48) == 60, "Tempo change at exact sequence tick");
    Check(tempoMap.GetTempoAt(96) == 50 && tempoMap.GetTempoAt(144) == 180, "Signed relative tempo changes");
    Check(Math.Abs(tempoMap.TickToSeconds(96) - 1.5) < 1e-9, "Integrate tempo across sections");
    Check(Math.Abs(tempoMap.Advance(0, 1) - 72) < 1e-9, "Timer update crosses tempo boundary");
    Check(Math.Abs(tempoMap.GetDurationSeconds(24, 48) - 0.75) < 1e-9, "Note release crosses tempo boundary");
    for (int tick = 0; tick < 300; tick++)
        Check(Math.Abs(tempoMap.SecondsToTick(tempoMap.TickToSeconds(tick)) - tick) < 1e-8, "Tempo conversion round trip including seeks");
    File.WriteAllBytes(tempoFixture, [0xdd, 90, 0xfd, 48, 0xff, 0]);
    tempoService.LoadM64(tempoFixture);
    Check(tempoMap.Changes.Count == 1 && tempoMap.GetTempoAt(1000) == 90, "Reload clears previous tempo changes");
    Check(Math.Abs(tempoMap.Advance(0, 1) - 72) < 1e-9, "Constant-tempo playback remains unchanged");
    Console.WriteLine("PASS: tempo event parsing, signed changes, boundary timing, releases, seek, and reload");

    string saveFixture = Path.Combine(temp, "stacked.m64");
    var saveService = new M64Service();
    var saveTracks = new List<M64Track> { new() { ChannelIndex = 0, Instrument = 2, Volume = 100, Notes =
        [new() { StartTick = 0, DurationTicks = 48, Pitch = 60, Velocity = 100, Instrument = 2, Gate = 0 },
         new() { StartTick = 0, DurationTicks = 48, Pitch = 57, Velocity = 90, Instrument = 2, Gate = 0 },
         new() { StartTick = 0, DurationTicks = 48, Pitch = 0, Velocity = 80, Instrument = 2, Gate = 0 },
         new() { StartTick = 0, DurationTicks = 48, Pitch = 127, Velocity = 70, Instrument = 2, Gate = 0 },
         new() { StartTick = 48, DurationTicks = 24, Pitch = 72, Velocity = 60, Instrument = 2, Gate = 64, CommandType = 2 }] } };
    var firstSave = saveService.SaveM64(saveFixture, saveTracks);
    Check(firstSave.Success, "Export four-layer stack: " + firstSave.Error);
    var reloadService = new M64Service();
    var reloaded = reloadService.LoadM64(saveFixture);
    var reloadedNotes = reloaded.SelectMany(t => t.Notes).ToList();
    Check(reloadedNotes.Count == 5, "Every stacked note must survive save/reload");
    Check(reloadedNotes.Where(n => n.StartTick == 0).Select(n => n.Pitch).Order().SequenceEqual(new byte[] { 0, 57, 60, 127 }), "Pitches 0, original/-3, and 127 are encoded without masking");
    Check(reloadedNotes.Single(n => n.StartTick == 48).DurationTicks == 24, "Reassigned compact note gets an explicit delay");
    Check(reloadedNotes.All(n => n.Instrument == 2), "Preserve instruments");
    byte[] beforeNoOp = File.ReadAllBytes(saveFixture);
    Check(reloadService.SaveM64(saveFixture, reloaded).Success && File.ReadAllBytes(saveFixture).SequenceEqual(beforeNoOp), "Unchanged save must be byte-identical");
    var edited = reloadedNotes.Single(n => n.StartTick == 48);
    edited.Pitch = 100;
    Check(reloadService.SaveM64(saveFixture, reloaded, false).Success, "Detect edits even with a stale UI dirty flag");
    Check(new M64Service().LoadM64(saveFixture).SelectMany(t => t.Notes).Single(n => n.StartTick == 48).Pitch == 100, "Edited high note reloads correctly");
    edited.Pitch = 101;
    Check(reloadService.SaveM64(saveFixture, reloaded, false).Success, "Repeated save keeps latest edit instead of restoring .bak");
    byte[] lastGood = File.ReadAllBytes(saveFixture);
    reloaded[0].Notes.Add(new() { StartTick = 0, DurationTicks = 48, Pitch = 64, Velocity = 100 });
    var overflowSave = reloadService.SaveM64(saveFixture, reloaded);
    Check(!overflowSave.Success && File.ReadAllBytes(saveFixture).SequenceEqual(lastGood), "Five-layer overflow must fail without discarding notes or changing the file");
    Check(File.ReadAllBytes(saveFixture + ".bak").SequenceEqual(beforeNoOp), "Keep original backup");
    reloaded[0].Notes.RemoveAt(reloaded[0].Notes.Count - 1);
    byte[] externalEdit = lastGood.ToArray(); externalEdit[^1] ^= 1;
    File.WriteAllBytes(saveFixture, externalEdit);
    Check(!reloadService.SaveM64(saveFixture, reloaded).Success && File.ReadAllBytes(saveFixture).SequenceEqual(externalEdit), "Do not overwrite external edits");
    string invalidSave = Path.Combine(temp, "invalid.m64");
    var invalidTrack = new M64Track { ChannelIndex = 0, Notes = [new() { StartTick = 0, DurationTicks = 32768, Pitch = 60, Velocity = 100 }] };
    Check(!new M64Service().SaveM64(invalidSave, [invalidTrack]).Success && !File.Exists(invalidSave), "Reject overflowing note delays before writing");
    string scriptFixture = Path.Combine(temp, "scripted.m64");
    var scriptedTrack = new M64Track { ChannelIndex = 0, Notes = [new() { StartTick = 0, DurationTicks = 48, Pitch = 60, Velocity = 100, Gate = 0 }] };
    Check(new M64Service().SaveM64(scriptFixture, [scriptedTrack]).Success, "Create scripted fixture");
    byte[] scriptedBytes = File.ReadAllBytes(scriptFixture);
    int channelPointer = Array.IndexOf(scriptedBytes, (byte)0x90) + 1;
    int channelStart = BinaryPrimitives.ReadUInt16BigEndian(scriptedBytes.AsSpan(channelPointer, 2));
    int layerPointer = Array.IndexOf(scriptedBytes, (byte)0x90, channelStart) + 1;
    int layerStart = BinaryPrimitives.ReadUInt16BigEndian(scriptedBytes.AsSpan(layerPointer, 2));
    scriptedBytes[layerStart] = 0xc1; // legal short-note velocity command, retained inside a large-note layer
    File.WriteAllBytes(scriptFixture, scriptedBytes);
    var scriptService = new M64Service();
    var scriptedTracks = scriptService.LoadM64(scriptFixture);
    scriptedTracks[0].Notes[0].Pitch++;
    Check(!scriptService.SaveM64(scriptFixture, scriptedTracks).Success && File.ReadAllBytes(scriptFixture).SequenceEqual(scriptedBytes), "Refuse to flatten unsupported layer effects without touching the file");
    scriptedTracks[0].Notes[0].Pitch--;
    var additionalNote = scriptedTracks[0].Notes[0].Clone(); additionalNote.Pitch -= 3;
    scriptedTracks[0].Notes.Add(additionalNote);
    Check(scriptService.SaveM64(scriptFixture, scriptedTracks).Success, "Added note can use a free layer while preserving unsupported original commands");
    Check(File.ReadAllBytes(scriptFixture)[layerStart] == 0xc1 && new M64Service().LoadM64(scriptFixture).Sum(t => t.Notes.Count) == 2, "Preserved scripted layer plus added harmony both survive");
    string longRest = Path.Combine(temp, "long-rest.m64");
    var longRestTrack = new M64Track { ChannelIndex = 0, Notes = [new() { StartTick = 40000, DurationTicks = 48, Pitch = 64, Velocity = 100, Gate = 0 }] };
    Check(new M64Service().SaveM64(longRest, [longRestTrack]).Success && new M64Service().LoadM64(longRest)[0].Notes[0].StartTick == 40000, "Split long rests without truncating delay fields");
    string tooLarge = Path.Combine(temp, "too-large.m64");
    var hugeTrack = new M64Track { ChannelIndex = 0, Notes = Enumerable.Range(0, 9000).Select(i => new M64Note { StartTick = i, DurationTicks = 1, Pitch = 127, Velocity = 100 }).ToList() };
    Check(!new M64Service().SaveM64(tooLarge, [hugeTrack]).Success && !File.Exists(tooLarge), "Reject 16-bit pointer overflow before creating a sequence");
    Console.WriteLine("PASS: four-note stacks, 0/-3 harmony, pitch limits, compact-note timing, repeated/no-op saves, backups, and non-destructive failures");

    short[] waveform = Enumerable.Range(0, 300).Select(i => (short)(i < 200 ? 20000 : 5000)).ToArray();
    string oneShot = Path.Combine(temp, "one-shot.aiff");
    string looped = Path.Combine(temp, "looped.aiff");
    File.WriteAllBytes(oneShot, Aiff(waveform));
    File.WriteAllBytes(looped, Aiff(waveform, loop: true));
    Check(AiffLoopReader.Read(File.ReadAllBytes(looped), 300) == new AiffLoop(200, 240, -1), "AIFF marker loop bounds");
    Check(AiffLoopReader.Read(Aiff(waveform, vadpcm: true), 300) == new AiffLoop(200, 240, -1), "Nintendo loop bounds");
    Check(AiffLoopReader.Read(Aiff(waveform, loop: true), 100) == null, "Reject loop outside PCM frames");
    Check(AiffLoopReader.Read(new byte[13], 300) == null, "Reject invalid AIFF");
    Console.WriteLine("PASS: AIFF MARK/INST and Nintendo loop metadata, invalid bounds");

    using (var player = new SampleSynthPlayer(false))
    {
        player.PlayNote(0, 39, oneShot, 127, 127, 64);
        var audio = Render(player, 1200);
        Check(audio.Take(600).Any(v => v != 0), "One-shot produces audio");
        Check(audio.Skip(600).All(v => v == 0), "One-shot must never wrap to its attack");
        player.PlayNote(0, 39, looped, 127, 127, 64);
        audio = Render(player, 1200);
        Check(audio.Skip(600).All(v => v > 0 && v < 1200), "Sustain repeats only the loop region, not the attack");
        player.StopAll();
        Check(Render(player, 300).All(v => v == 0), "StopAll clears transport audio");
    }
    Console.WriteLine("PASS: one-shot termination, correct sustain loop, transport stop");

    using (var player = new SampleSynthPlayer(false))
    {
        player.PlayNote(0, 39, looped, 127, 127, 0);
        player.PlayNote(1, 39, looped, 127, 127, 127);
        var fullVolume = Render(player, 800);
        int referenceLeft = fullVolume[^2], referenceRight = fullVolume[^1];
        Check(referenceLeft > 0 && referenceRight > 0, "Live-volume test has two audible channels");
        player.SetChannelVolume(0, 64);
        var halfVolume = Render(player, 800);
        Check(Math.Abs(halfVolume[^2] - referenceLeft * 64.0 / 127) <= 2, "Slider volume affects an already-playing voice linearly");
        Check(halfVolume[^1] == referenceRight, "Volume changes are isolated to the selected channel");
        player.SetChannelVolume(0, 0);
        var muted = Render(player, 800);
        Check(muted[^2] == 0 && muted[^1] == referenceRight, "Live mute preserves the other channel");
        player.SetChannelVolume(0, 127);
        var restored = Render(player, 800);
        Check(restored[^2] == referenceLeft, "Unmute restores the same sustained voice without multiplying its previous gain");
        player.SetChannelVolume(0, 0);
        player.StopAll();
        player.PlayNote(0, 39, looped, 127, 127, 0);
        Check(Render(player, 800).All(v => v == 0), "Future notes and transport restart retain the edited volume");
        player.SetChannelVolume(0, 127);
        Check(Render(player, 800)[^2] == referenceLeft, "A voice started at zero volume can be unmuted");
        player.StopAll();
        player.ClearChannelVolumeOverrides();
        player.PlayNote(0, 39, looped, 127, 32, 0);
        Check(Math.Abs(Render(player, 800)[^2] - referenceLeft * 32.0 / 127) <= 2, "Loading a new sequence restores its original scripted note volume");
    }
    Console.WriteLine("PASS: live channel volume, mute/unmute, independent channels, future notes, and sequence reset");

    var instrument = new Sm64Instrument(looped, 44100.0 / 32000, false, null, 208, []);
    using (var player = new SampleSynthPlayer(false))
    {
        player.PlayNote(0, 39, looped, 127, 127, 64, instrument.Tuning, instrument, 0);
        var single = Render(player, 800);
        player.StopAll();
        player.PlayNote(0, 39, looped, 127, 127, 64, instrument.Tuning, instrument, 0);
        player.PlayNote(0, 39, looped, 127, 127, 64, instrument.Tuning, instrument, 1);
        var doubleVoice = Render(player, 800);
        Check(doubleVoice[^2] >= single[^2] * 2 - 1, "Same-pitch notes in different layers remain independent");
        player.StopAll();
        player.PlayNote(0, 39, looped, 127, 127, 64, instrument.Tuning, instrument, 0, 0.01);
        Check(Render(player, 5000).Skip(6000).All(v => v == 0), "Scheduled note release cannot become stuck");
    }
    Check(Sm64NoteTiming.GetDurationTicks(48, 0, 0) == 48, "Zero gate is full duration");
    Check(Sm64NoteTiming.GetDurationTicks(48, 128, 0) == 24, "Gate is silent fraction");
    Check(Sm64NoteTiming.GetDurationTicks(48, 204, 0) == 10, "Integer game gate arithmetic");
    Check(Sm64NoteTiming.GetDurationTicks(48, 255, 1) == 48, "Medium note resets gate");
    Console.WriteLine("PASS: independent layers, sample-clock release, SM64 gate semantics");

    var envelope = new Sm64Envelope([new(2, 32700), new(10, 0), new(-1, 0)], 32, 44100);
    double peak = 0, value = 0;
    for (int i = 0; i < 3000; i++) { value = envelope.Next(false); peak = Math.Max(peak, value); }
    Check(peak > 1 && value < 0.001, "Bank envelope attack/decay is honored");
    var reverb = new Sm64Reverb(32000);
    var wet = reverb.Process(0.5, 0);
    Check(wet.Left == 0, "Reverb starts dry");
    for (int i = 1; i < 0xC00; i++) Check(reverb.Process(0, 0).Left == 0, "Reverb ring delay");
    wet = reverb.Process(0, 0);
    Check(wet == (0.5, 0), "Reverb impulse returns after default room delay");
    for (int i = 1; i < 0xC00; i++) reverb.Process(0, 0);
    Check(Math.Abs(reverb.Process(0, 0).Left - 0.5 * 0x2FFF / 32768) < 1e-9, "Reverb feedback decays");
    reverb.Clear();
    for (int i = 0; i < 10000; i++) Check(reverb.Process(0, 0) == (0, 0), "No residual reverb after reset");
    Console.WriteLine("PASS: instrument envelope, reverb delay, feedback, and reset");

    string bankDir = Path.Combine(temp, "sound", "sound_banks");
    string sampleDir = Path.Combine(temp, "sound", "samples", "instruments");
    Directory.CreateDirectory(bankDir); Directory.CreateDirectory(sampleDir);
    File.Copy(looped, Path.Combine(sampleDir, "test.aiff"));
    File.WriteAllText(Path.Combine(bankDir, "test.json"), """
        {"sample_bank":"instruments","envelopes":{"env":[[2,32700],"hang"]},
         "instruments":{"inst0":{"release_rate":208,"envelope":"env","sound":"test"},
         "percussion":[{"release_rate":10,"pan":62,"envelope":"env","sound":{"sample":"test","tuning":0.5}}]},
         "instrument_list":["inst0"]}
        """);
    var bank = new Sm64SoundBank(temp);
    Check(bank.Resolve("test", 0, 39)?.Envelope.Length == 2, "Read melodic envelope");
    var drum = bank.Resolve("test", 127, 0);
    Check(drum is { IsPercussion: true, Tuning: 0.5, Pan: 62, ReleaseRate: 10 }, "Nested drum and direct semitone index");
    using (var player = new SampleSynthPlayer(false))
    {
        player.PlayNote(0, 0, looped, 127, 127, 64, 1, drum);
        var low = Render(player, 600);
        player.StopAll();
        player.PlayNote(0, 60, looped, 127, 127, 64, 1, drum);
        Check(low.SequenceEqual(Render(player, 600)), "Drum tuning must not be transposed a second time");
    }
    Console.WriteLine("PASS: soundbank schema, drum pan/envelope/tuning, fixed drum pitch");

    string regionalSamples = Path.Combine(temp, "sound", "samples", "regional");
    Directory.CreateDirectory(regionalSamples);
    File.Copy(looped, Path.Combine(regionalSamples, "sh.aiff"));
    File.Copy(looped, Path.Combine(regionalSamples, "cn.aiff"));
    File.WriteAllText(Path.Combine(bankDir, "regional.json"), """
        {"sample_bank":{"ifdef":["VERSION_SH","VERSION_CN"],"then":"regional","else":"instruments"},
         "envelopes":{"env":[[2,{"ifdef":["VERSION_CN"],"then":20000,"else":32700}],"hang"]},
         "instruments":{"inst0":{"release_rate":119,"envelope":"env",
         "sound":{"ifdef":["VERSION_SH","VERSION_CN"],
                  "then":{"sample":{"ifdef":["VERSION_CN"],"then":"cn","else":"sh"},"tuning":1.25},
                  "else":{"sample":"test","tuning":0.5}}}},"instrument_list":["inst0"]}
        """);
    var usInstrument = bank.Resolve("regional", 0, 60, "us");
    var shInstrument = bank.Resolve("regional", 0, 60, "sh");
    var cnInstrument = bank.Resolve("regional", 0, 60, "cn");
    Check(usInstrument?.SamplePath == Path.Combine(sampleDir, "test.aiff") && usInstrument.Tuning == 0.5, "US conditional sound/sample bank");
    Check(shInstrument?.SamplePath == Path.Combine(regionalSamples, "sh.aiff") && shInstrument.Tuning == 1.25, "SH nested conditional sound");
    Check(cnInstrument?.SamplePath == Path.Combine(regionalSamples, "cn.aiff") && cnInstrument.Envelope[0].Target == 20000, "CN nested sample and envelope conditional");
    Check(bank.Resolve("regional", 0, 60, "jp")?.SamplePath == usInstrument.SamplePath, "JP selects non-SH/CN branch");
    Check(bank.Resolve("regional", 0, 60, "eu")?.SamplePath == usInstrument.SamplePath, "EU selects non-SH/CN branch");
    Check(bank.Resolve("regional", 0, 60, "us")?.SamplePath == usInstrument.SamplePath, "Region cache must not reuse the previous SH/CN resolution");
    Check(Sm64SoundBank.GetVersionForSequence(Path.Combine(temp, "sound", "sequences", "sh", "music.m64")) == "sh", "Detect active sequence region");
    Console.WriteLine("PASS: regional JSON branches, nested conditions, any-match defines, and cache isolation");

    if (args.Length > 0)
    {
        string titlePath = Path.Combine(args[0], "sound", "sequences", "us", "02_menu_title_screen.m64");
        var title = new M64Service();
        title.LoadM64(titlePath);
        M64TempoChange[] expectedTitleTempos = [new(0, 172), new(1, 99), new(49, 90),
            new(97, 78), new(481, 119), new(577, 172)];
        Check(title.TempoMap.Changes.SequenceEqual(expectedTitleTempos), "Actual title/Game Over tempo timeline");
        string titleCopy = Path.Combine(temp, "title-copy.m64");
        File.Copy(titlePath, titleCopy);
        var titleSaveService = new M64Service();
        var titleTracks = titleSaveService.LoadM64(titleCopy);
        byte[] titleOriginal = File.ReadAllBytes(titleCopy);
        Check(titleSaveService.SaveM64(titleCopy, titleTracks).Success && File.ReadAllBytes(titleCopy).SequenceEqual(titleOriginal), "Complex title scripts and tempo changes survive unchanged save byte-for-byte");
        double gameOverSeconds = title.TempoMap.GetDurationSeconds(1, 576);
        Check(gameOverSeconds > 7 && gameOverSeconds < 9, "Game Over section must not run at title-screen 172 BPM");
        Console.WriteLine($"PASS: actual title/Game Over tempo changes; 576-tick section lasts {gameOverSeconds:F3}s");
        var actualBank = new Sm64SoundBank(args[0]);
        Check(actualBank.Resolve("22", 127, 0) is { IsPercussion: true, Tuning: > 0 }, "Actual grass bank drums");
        string puzzlePath = Path.Combine(args[0], "sound", "sequences", "us", "1B_event_solve_puzzle.m64");
        var puzzle = new M64Service();
        var puzzleTracks = puzzle.LoadM64(puzzlePath);
        string puzzleCopy = Path.Combine(temp, "puzzle-copy.m64");
        File.Copy(puzzlePath, puzzleCopy);
        var puzzleEditService = new M64Service();
        var puzzleEditTracks = puzzleEditService.LoadM64(puzzleCopy);
        byte[] originalPuzzle = File.ReadAllBytes(puzzleCopy);
        Check(puzzleEditService.SaveM64(puzzleCopy, puzzleEditTracks).Success && File.ReadAllBytes(puzzleCopy).SequenceEqual(originalPuzzle), "Actual puzzle no-op save is byte-identical");
        var lowerNote = puzzleEditTracks[0].Notes[0].Clone();
        lowerNote.Pitch -= 3;
        puzzleEditTracks[0].Notes.Add(lowerNote);
        var puzzleSave = puzzleEditService.SaveM64(puzzleCopy, puzzleEditTracks);
        Check(puzzleSave.Success, "Save added harmony to actual puzzle copy: " + puzzleSave.Error);
        var puzzleReload = new M64Service().LoadM64(puzzleCopy);
        Check(puzzleReload.Sum(t => t.Notes.Count) == puzzleTracks.Sum(t => t.Notes.Count) + 1, "Actual puzzle harmony survives save/reload without losing original notes");
        foreach (var originalTrack in puzzleTracks)
            foreach (var originalNote in originalTrack.Notes)
                Check(puzzleReload.Single(t => t.ChannelIndex == originalTrack.ChannelIndex).Notes.Any(n =>
                    n.StartTick == originalNote.StartTick && n.DurationTicks == originalNote.DurationTicks && n.Pitch == originalNote.Pitch &&
                    n.Instrument == originalNote.Instrument && n.Velocity == originalNote.Velocity && n.Gate == originalNote.Gate), "Every original puzzle note retains timing, pitch, instrument, velocity, and gate");
        byte[] editedPuzzleBytes = File.ReadAllBytes(puzzleCopy);
        Check(originalPuzzle.Where((value, index) => value != editedPuzzleBytes[index]).Count() <= 2, "Original puzzle scripts stay byte-identical except the relocated channel pointer");
        Check(puzzleReload[0].Notes.Any(n => n.StartTick == lowerNote.StartTick && n.Pitch == lowerNote.Pitch), "Actual added lower note survives");
        Check(File.ReadAllBytes(puzzleCopy + ".bak").SequenceEqual(originalPuzzle), "Real original remains backed up");
        Console.WriteLine("PASS: actual puzzle copy keeps original notes and gains an overlapping lower harmony");
        int puzzleNotes = 0;
        string expectedMusicBox = Path.Combine(args[0], "sound", "samples", "piranha_music_box", "00_music_box_C5.aiff");
        foreach (var note in puzzleTracks.SelectMany(track => track.Notes))
        {
            var resolved = actualBank.Resolve("14_piranha_music_box", note.Instrument, note.Pitch,
                Sm64SoundBank.GetVersionForSequence(puzzlePath));
            Check(resolved?.SamplePath == expectedMusicBox, "Every actual puzzle note must use the original music box, without MIDI fallback");
            puzzleNotes++;
        }
        Check(puzzleNotes > 0, "Actual puzzle sequence has notes");
        Check(actualBank.Resolve("14_piranha_music_box", 0, 60, "us")?.SamplePath == expectedMusicBox, "Piranha music uses the same original bank");
        Check(actualBank.Resolve("16_course_start", 0, 39, "us") != null, "Course-start conditional sound is also resolved");
        Console.WriteLine($"PASS: all {puzzleNotes} actual puzzle notes resolve to the original US music-box sample; related conditional banks resolve");
        foreach (string samplePath in Directory.EnumerateFiles(Path.Combine(args[0], "sound", "samples"), "*.aiff", SearchOption.AllDirectories))
        {
            byte[] bytes = File.ReadAllBytes(samplePath);
            byte[] wav = AiffWavTranscoder.ConvertAiffToWav(bytes);
            Check(wav.Length > 44, "Decode actual sample " + samplePath);
            int frames = (wav.Length - 44) / BitConverter.ToInt16(wav, 32);
            var loop = AiffLoopReader.Read(bytes, frames);
            if (loop.HasValue) Check(loop.Value.End <= frames, "Actual loop within PCM");
        }
        Console.WriteLine("PASS: actual SM64 sound bank and all extracted AIFF samples");
    }
    Console.WriteLine("All audio regression checks passed. No audio device opened.");
}
finally
{
    if (!Path.GetFullPath(temp).StartsWith(Path.GetFullPath(AppContext.BaseDirectory), StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("Test cleanup must remain inside the harness output directory.");
    Directory.Delete(temp, recursive: true);
}
