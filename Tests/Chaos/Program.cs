using Sm64DecompLevelViewer.Services.Chaos;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
static void Reject(Action action, string message)
{
    try { action(); } catch (InvalidDataException) { return; }
    throw new Exception(message);
}
static byte[] Fixture(bool shindou = false)
{
    var bytes = new List<byte>();
    var labels = new Dictionary<string, int>();
    var pointers = new List<(int Offset, string Label)>();
    void Emit(params byte[] values) => bytes.AddRange(values);
    void Label(string label) => labels[label] = bytes.Count;
    void Pointer(byte cmd, string label) { Emit(cmd); pointers.Add((bytes.Count, label)); Emit(0, 0); }
    Emit(0xdd, 100, 0xdb, 80, shindou ? (byte)0xf1 : (byte)0xf2, 8);
    Pointer(0x90, "channel0"); Pointer(0x91, "channel1"); Pointer(0x92, "channel2");
    Label("wait"); Emit(0xfd, 0x81, 0xff); Pointer(0x90, "continuation"); Pointer(0xfb, "wait");
    Label("channel0"); Emit(0xc4, 0xdf, 100); Pointer(0xda, "envelope"); Emit(0xde, 0x80, 0,
        0xdd, 64, 0xd4, 40, 0xd3, 128, 0xd9, 32);
    Pointer(shindou ? (byte)0x88 : (byte)0x90, "large");
    Emit(0xc1, 0, 0xfd, 10, 0xff); // Instrument is applied after the layer pointer, before yield.
    Label("channel2"); Emit(0xc3, 0xc1, 0); Pointer(shindou ? (byte)0x88 : (byte)0x90, "short"); Emit(0xfd, 10, 0xff);
    Label("channel1"); Emit(0xc4, 0xc1, 1); Pointer(shindou ? (byte)0x88 : (byte)0x90, "other"); Emit(0xfd, 10, 0xff);
    Label("continuation"); Pointer(shindou ? (byte)0x88 : (byte)0x90, "retained"); Emit(0xfd, 10, 0xff);
    Label("large"); Emit(0xca, 64, 0xc7, 0x80, 36, 24);
    Emit(0x24, 0x81, 0xff, 100, 128, 0x64, 24, 70, 0xa4, 60, 80, 0xff);
    Label("short"); Emit(0xc1, 70, 0xc9, 80, 0x18, 0x81, 0xff, 0x58, 0x98, 0xff);
    Label("other"); Emit(0x28, 24, 100, 128, 0xff);
    Label("retained"); Emit(0x26, 24, 100, 128, 0xff);
    Label("envelope"); Emit(0, 1, 0x7f, 0xff, 0xff, 0xff, 0, 0);
    foreach (var pointer in pointers)
    {
        bytes[pointer.Offset] = (byte)(labels[pointer.Label] >> 8);
        bytes[pointer.Offset + 1] = (byte)labels[pointer.Label];
    }
    return bytes.ToArray();
}

int runs = 0;
void Exercise(byte[] source, string region)
{
    var values = M64CommandScanner.Scan(source, region);
    var mutable = values.Select(v => v.Offset).ToHashSet();
    foreach (int mode in Enumerable.Range(0, 30).Where(m => m != 27))
        for (int seed = 0; seed < 8; seed++)
        {
            var output = new M64Corruptor(new Random(seed)).Mutate(source, region, [mode], 100);
            Check(output.Length == source.Length, "Corruption resized script");
            for (int i = 0; i < source.Length; i++)
                Check(mutable.Contains(i) || output[i] == source[i], "Control/delay/table byte changed");
            foreach (var value in values.Where(v => v.Kind == M64ValueKind.Pitch))
                Check((source[value.Offset] & 0xc0) == (output[value.Offset] & 0xc0), "Note encoding changed");
            runs++;
        }
    var modes = Enumerable.Range(0, 30).Where(m => m != 27).ToArray();
    var combined = new M64Corruptor(new Random(42)).Mutate(source, region, modes, 100);
    Check(combined.SequenceEqual(new M64Corruptor(new Random(42)).Mutate(source, region, modes, 100)), "Seed not reproducible");
    new M64Corruptor(new Random(42)).Mutate(source, region, [], 100, true);
    Check(source.SequenceEqual(new M64Corruptor(new Random(42)).Mutate(source, region, modes, 0)), "Zero intensity changed script");
}

byte[] fixture = Fixture();
var scanned = M64CommandScanner.Scan(fixture, "us");
Check(scanned.Count(v => v.Kind == M64ValueKind.Pitch) == 8, "Mixed short/large notes or retained channel format desynchronized");
Check(scanned.Where(v => v.Kind == M64ValueKind.Pitch && v.Channel == 0).All(v => v.Instrument == 0), "Layer missed post-pointer instrument or retained channel state");
Check(scanned.Count(v => v.Kind == M64ValueKind.Tempo) == 1, "Tempo not found");
Check(!scanned.Any(v => v.Offset == 5), "Reserve-notes operand misclassified as volume");
Check(scanned.Count(v => v.Kind == M64ValueKind.Volume) == 2, "Master/channel volumes not found");
byte[] perChannel = new M64Corruptor(new Random(7)).Mutate(fixture, "us", [18], 100);
foreach (var group in scanned.Where(v => v.Kind == M64ValueKind.Pitch).GroupBy(v => v.Channel))
    Check(group.Select(v => (perChannel[v.Offset] & 63) - (fixture[v.Offset] & 63)).Distinct().Count() == 1,
        "Transpose-per-channel used note index instead of the real channel");
Exercise(fixture, "us"); Exercise(Fixture(true), "sh"); Exercise(Fixture(true), "cn");
byte[] longer = new M64Corruptor(new Random(1)).Mutate(fixture, "us", [11], 100);
byte[] shorter = new M64Corruptor(new Random(1)).Mutate(fixture, "us", [12], 100);
foreach (var value in scanned.Where(v => v.Kind == M64ValueKind.Gate))
{
    Check(longer[value.Offset] <= fixture[value.Offset], "Lengthener raised silent gate fraction");
    Check(shorter[value.Offset] >= 240, "Shortener did not raise silent gate fraction");
}
Reject(() => new M64Corruptor(new Random()).Mutate(fixture, "us", [27], 100), "Pointer mode accepted");
Reject(() => new M64Corruptor(new Random()).Mutate(fixture, "us", [], 100), "Empty mode selection accepted");
Reject(() => M64CommandScanner.Scan([0x90, 0xff, 0xff], "us"), "Out-of-range pointer accepted");
Reject(() => M64CommandScanner.Scan([0xfd, 0x81], "us"), "Truncated compressed delay accepted");
Reject(() => M64CommandScanner.Scan([0xc7, 0, 0, 0, 0xff], "us"), "Self-modifying flow accepted");
Reject(() => M64CommandScanner.Scan([0xfc, 0, 0], "us"), "Recursive stack overflow accepted");
Reject(() => M64CommandScanner.Scan(fixture, "unknown"), "Unknown region guessed");
// A conditional branch into an operand must fail even when both destinations are in bounds.
Reject(() => M64CommandScanner.Scan([0xfa, 0, 4, 0xdd, 0xff, 0xff], "us"), "Overlapping code/data accepted");

bool ioOnly = args.Length == 2 && args[0] == "--io-root";
string testBase = ioOnly ? args[1] : AppContext.BaseDirectory;
string root = Path.Combine(testBase, "chaos-tests-" + Guid.NewGuid().ToString("N"));
string us = Path.Combine(root, "us"); Directory.CreateDirectory(us);
try
{
    string path = Path.Combine(us, "fixture.m64");
    File.WriteAllBytes(path, fixture);
    var corruptor = new M64Corruptor(new Random(9));
    corruptor.CorruptFile(path, [11], 100, false, false);
    Check(File.ReadAllBytes(path + ".bak").SequenceEqual(fixture), "Original backup lost");
    byte[] saved = File.ReadAllBytes(path);
    Reject(() => corruptor.CorruptFile(path, [27], 100, false, false), "Pointer mode committed");
    Check(File.ReadAllBytes(path).SequenceEqual(saved), "Rejected corruption changed target");
    corruptor.CorruptFile(path, [12], 100, false, true);
    Check(File.ReadAllBytes(path + ".bak").SequenceEqual(fixture), "Accumulation overwrote first backup");
    corruptor.CorruptFile(path, [11], 100, false, false);
    Check(File.ReadAllBytes(path).SequenceEqual(saved), "Non-accumulating repeat did not use original baseline");
    File.WriteAllBytes(path + ".bak", [0xfd, 0x81]);
    Reject(() => corruptor.CorruptFile(path, [11], 100, false, false), "Invalid backup accepted");
    Check(File.ReadAllBytes(path).SequenceEqual(saved), "Invalid backup was restored over live file");
    Check(!Directory.EnumerateFiles(us, "*.tmp").Any(), "Staged files leaked");
}
finally
{
    if (Path.GetFullPath(root).StartsWith(Path.GetFullPath(testBase).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        Directory.Delete(root, true);
}

if (args.Length > 0 && !ioOnly)
{
    string sequenceRoot = Path.Combine(args[0], "sound", "sequences");
    int accepted = 0, refused = 0;
    foreach (string path in Directory.EnumerateFiles(sequenceRoot, "*.m64", SearchOption.AllDirectories).Order())
    {
        string region = Path.GetFileName(Path.GetDirectoryName(path))!;
        byte[] source = File.ReadAllBytes(path);
        try { M64CommandScanner.Scan(source, region); }
        catch (InvalidDataException ex)
        {
            Console.WriteLine($"REFUSED {region}/{Path.GetFileName(path)}: {ex.Message}"); refused++; continue;
        }
        Exercise(source, region); accepted++;
    }
    Check(accepted >= 4, "Too few real SM64 sequences exercised");
    Console.WriteLine($"Real sequences: {accepted} accepted, {refused} conservatively refused; original assets read only.");
}
Console.WriteLine($"Chaos regression checks passed ({runs} seeded mode runs).");
