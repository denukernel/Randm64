using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Sm64DecompLevelViewer.Services;

namespace Sm64DecompLevelViewer;

public partial class TextEditorWindow : Window
{
    private readonly string _root;
    private readonly List<string> _files = new();
    private ProjectFileDocument? _document;
    private DialogGlyphPreview? _glyphPreview;
    private string _buffer = ""; private DialogTextEntry? _entry; private int _page;
    private string? _editError;
    private bool _loading, _dirty, _selecting;
    public TextEditorWindow(string root)
    {
        InitializeComponent(); _root = Path.GetFullPath(root);
        _glyphPreview = new DialogGlyphPreview(_root); GlyphHost.Children.Add(_glyphPreview);
        Loaded += async (_, _) => await LoadFilesAsync();
        Closing += (_, e) => e.Cancel = !ConfirmLeave();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.S && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { Save(); e.Handled = true; } };
    }
    private async Task LoadFilesAsync()
    {
        try
        {
            var files = await Task.Run(() =>
            {
                var result = new List<string>();
                var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".c", ".h", ".s", ".inc", ".json", ".yaml", ".yml", ".txt", ".md", ".py", ".sh", ".mk", ".ld" };
                var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false };
                foreach (string folder in new[] { "text" })
                    if (Directory.Exists(Path.Combine(_root, folder)))
                        foreach (string file in Directory.EnumerateFiles(Path.Combine(_root, folder), "*", options))
                            if (Path.GetFileName(file).Contains("dialog", StringComparison.OrdinalIgnoreCase) && extensions.Contains(Path.GetExtension(file)) && new FileInfo(file).Length <= 2 * 1024 * 1024)
                                result.Add(Path.GetRelativePath(_root, file));
                return result.Distinct().OrderBy(p => p.StartsWith("text" + Path.DirectorySeparatorChar) ? 0 : p.Contains("menu", StringComparison.OrdinalIgnoreCase) ? 1 : 2).ThenBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
            });
            if (!IsLoaded) return;
            _files.AddRange(files); ApplyFilter(); if (Files.Items.Count > 0) Files.SelectedIndex = 0; Status.Text = $"{_files.Count} text files; dialog files";
        }
        catch (Exception ex) { Status.Text = "File scan failed: " + ex.Message; }
    }
    private void Filter_Changed(object sender, TextChangedEventArgs e) { if (Files != null) ApplyFilter(); }
    private void ApplyFilter()
    {
        _selecting = true;
        Files.ItemsSource = _files.Where(p => p.Contains(FileSearch.Text, StringComparison.OrdinalIgnoreCase)).ToList();
        if (_document != null) Files.SelectedItem = Path.GetRelativePath(_root, _document.Path);
        _selecting = false;
    }
    private void File_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_selecting || Files.SelectedItem is not string relative) return;
        if (!ConfirmLeave()) { _selecting = true; Files.SelectedItem = _document == null ? null : Path.GetRelativePath(_root, _document.Path); _selecting = false; return; }
        Open(relative);
    }
    private void Open(string relative)
    {
        try
        {
            var document = ProjectFileDocument.Open(Path.Combine(_root, relative));
            _loading = true; _document = document; Editor.IsUndoEnabled = false; _editError = null; _entry = null; _buffer = document.Text; Dialogs.ItemsSource = DialogTextDocument.Parse(_buffer); Editor.Text = ""; Editor.IsUndoEnabled = true; _dirty = false; _loading = false;
            if (Dialogs.Items.Count > 0) Dialogs.SelectedIndex = 0; FileLabel.Text = relative; Status.Text = "Loaded " + relative;
        }
        catch (Exception ex) { _loading = false; Status.Text = "Open failed: " + ex.Message; }
    }
    private bool ConfirmLeave()
    {
        if (!_dirty) return true;
        var choice = MessageBox.Show(this, "Save changes to the current file?", "Unsaved text", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        return choice == MessageBoxResult.No || choice == MessageBoxResult.Yes && Save();
    }
    private bool Save()
    {
        if (_editError != null) { Status.Text = _editError; return false; }
        if (_document == null) return true;
        try { _document.Save(_buffer); _dirty = false; FileLabel.Text = Path.GetRelativePath(_root, _document.Path); Status.Text = "Saved; original backup retained beside the file"; return true; }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Text was not saved", MessageBoxButton.OK, MessageBoxImage.Error); return false; }
    }
    private void Save_Click(object sender, RoutedEventArgs e) => Save();
    private void Reload_Click(object sender, RoutedEventArgs e) { if (_document != null && ConfirmLeave()) Open(Path.GetRelativePath(_root, _document.Path)); }
    private void Dialog_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (_editError != null) { _loading = true; Dialogs.SelectedItem = _entry; _loading = false; Status.Text = _editError; return; }
        _entry = Dialogs.SelectedItem as DialogTextEntry;
        _loading = true;
        Editor.Text = _entry?.Text ?? "";
        Editor.IsReadOnly = _entry == null || !_entry.Editable;
        Editor.IsUndoEnabled = false; Editor.IsUndoEnabled = true;
        _loading = false; _page = 0; UpdatePreview();
        Status.Text = _entry == null ? "no dialogs found" : _entry.Editable ? "editing " + _entry.Id + (_entry.Macros.Count > 0 ? " | keep macro markers unchanged: " + string.Join("; ", _entry.Macros.Select((m, i) => $"[[macro:{i}]] = {m.Trim()}")) : "") : "macro expression: read-only preview; literal portions shown";
    }
    private void Editor_Changed(object sender, TextChangedEventArgs e)
    {
        if (_loading || _document == null || _entry == null || !_entry.Editable) return;
        string expression;
        try { expression = DialogTextDocument.Encode(_entry, Editor.Text); _editError = null; }
        catch (InvalidOperationException ex) { _editError = ex.Message; _dirty = true; Status.Text = ex.Message; UpdatePreview(); return; }
        _buffer = _buffer.Remove(_entry.Start, _entry.Length).Insert(_entry.Start, expression);
        var entries = DialogTextDocument.Parse(_buffer);
        int index = Dialogs.SelectedIndex;
        _loading = true; Dialogs.ItemsSource = entries; Dialogs.SelectedIndex = index; _loading = false;
        _entry = Dialogs.SelectedItem as DialogTextEntry;
        _dirty = _buffer.Replace("\r\n", "\n") != _document.Text.Replace("\r\n", "\n");
        FileLabel.Text = Path.GetRelativePath(_root, _document.Path) + (_dirty ? " *" : "");
        UpdatePreview();
    }
    private void Editor_SelectionChanged(object sender, RoutedEventArgs e) { }
    private void PreviousPage_Click(object sender, RoutedEventArgs e) { _page--; UpdatePreview(); }
    private void NextPage_Click(object sender, RoutedEventArgs e) { _page++; UpdatePreview(); }
    private void UpdatePreview()
    {
        if (DialogPreview == null) return;
        int lines = _entry?.Lines ?? 4;
        string[] text = Editor.Text.Replace("\r\n", "\n").Split('\n');
        int pages = Math.Max(1, (text.Length + lines - 1) / lines);
        _page = Math.Clamp(_page, 0, pages - 1);
        DialogPreview.Text = string.Join("\n", text.Skip(_page * lines).Take(lines));
        if (_glyphPreview != null) { _glyphPreview.Text = DialogPreview.Text; _glyphPreview.InvalidateVisual(); DialogPreview.Visibility = Visibility.Hidden; }
        DialogBox.Height = lines * 16 + 16;
        PageLabel.Text = $"Page {_page + 1} / {pages}";
        PageArrow.Visibility = _page + 1 < pages ? Visibility.Visible : Visibility.Hidden;
    }    private void Find_Click(object sender, RoutedEventArgs e)
    {
        if (FindText.Text.Length == 0) return;
        int start = Math.Min(Editor.Text.Length, Editor.SelectionStart + Editor.SelectionLength);
        int index = Editor.Text.IndexOf(FindText.Text, start, StringComparison.OrdinalIgnoreCase);
        if (index < 0) index = Editor.Text.IndexOf(FindText.Text, StringComparison.OrdinalIgnoreCase);
        if (index < 0) { Status.Text = "No matching text"; return; }
        Editor.Focus(); Editor.Select(index, FindText.Text.Length); Editor.ScrollToLine(Editor.GetLineIndexFromCharacterIndex(index));
    }
}





