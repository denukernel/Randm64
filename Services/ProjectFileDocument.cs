using System;
using System.IO;
using System.Linq;
using System.Text;

namespace Sm64DecompLevelViewer.Services;

/// <summary>A source snapshot with conflict checks and WSL-compatible staged writes.</summary>
public sealed class ProjectFileDocument
{
    public string Path { get; }
    public string Text { get; private set; }
    public string NewLine { get; }
    private readonly Encoding _encoding;
    private readonly byte[] _preamble;
    private byte[] _snapshot;
    private ProjectFileDocument(string path, byte[] bytes)
    {
        Path = System.IO.Path.GetFullPath(path);
        _snapshot = bytes;
        if (bytes.AsSpan().StartsWith(new byte[] { 255, 254 })) _encoding = new UnicodeEncoding(false, true, true);
        else if (bytes.AsSpan().StartsWith(new byte[] { 254, 255 })) _encoding = new UnicodeEncoding(true, true, true);
        else _encoding = new UTF8Encoding(bytes.AsSpan().StartsWith(new byte[] { 239, 187, 191 }), true);
        _preamble = _encoding.GetPreamble();
        Text = _encoding.GetString(bytes, _preamble.Length, bytes.Length - _preamble.Length);
        if (Text.Contains('\0')) throw new InvalidDataException("binary files cannot be opened as source text");
        NewLine = Text.Contains("\r\n") ? "\r\n" : "\n";
    }
    public static ProjectFileDocument Open(string path) => new(path, File.ReadAllBytes(path));
    internal byte[] Snapshot => (byte[])_snapshot.Clone();
    public byte[] EncodeText(string text) => _preamble.Concat(_encoding.GetBytes(Normalize(text).Replace("\n", NewLine))).ToArray();
    public void Save(string text)
    {
        // An unchanged editor buffer must retain the original bytes, including mixed line endings.
        if (Normalize(text) == Normalize(Text))
        {
            if (!File.ReadAllBytes(Path).SequenceEqual(_snapshot)) throw new IOException("the file changed outside this editor; reload before saving");
            return;
        }
        text = Normalize(text).Replace("\n", NewLine);
        byte[] bytes = _preamble.Concat(_encoding.GetBytes(text)).ToArray();
        Commit(Path, _snapshot, bytes);
        _snapshot = bytes; Text = text;
    }
    private static string Normalize(string text) => text.Replace("\r\n", "\n");
    public static void Commit(string path, byte[] expected, byte[] output, string backupSuffix = ".bak")
    {
        string staged = path + ".randm64-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(staged, output);
            if (!File.ReadAllBytes(path).SequenceEqual(expected)) throw new IOException("the file changed while the edit was staged: " + path);
            if (!File.Exists(path + backupSuffix))
            {
                using var backup = new FileStream(path + backupSuffix, FileMode.CreateNew, FileAccess.Write);
                backup.Write(expected);
            }
            File.Move(staged, path, overwrite: true);
        }
        finally { if (File.Exists(staged)) File.Delete(staged); }
    }
}
