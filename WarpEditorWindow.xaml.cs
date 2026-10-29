using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Sm64DecompLevelViewer.Services;

namespace Sm64DecompLevelViewer
{
    public partial class WarpEditorWindow : Window
    {
        private readonly string _scriptPath;
        private readonly Action? _reload;
        private WarpScriptDocument? _document;
        private string _saved = "";
        public ObservableCollection<WarpScriptDocument.Node> Warps { get; } = new();
        public WarpEditorWindow(string levelPath, Action? reloadCallback, string? initialArea = null)
        {
            InitializeComponent(); _scriptPath = Path.Combine(levelPath, "script.c"); _reload = reloadCallback;
            MacroTypeColumn.ItemsSource = new[] { "WARP_NODE", "PAINTING_WARP_NODE" };
            DestLevelColumn.ItemsSource = Array.Empty<string>();
            FlagsColumn.ItemsSource = new[] { "WARP_NO_CHECKPOINT", "WARP_CHECKPOINT" };
            WarpGrid.ItemsSource = Warps;
            Loaded += (_, _) =>
            {
                try
                {
                    Reload();
                    string area = Regex.Match(initialArea ?? "", @"\d+").Value;
                    if (SourceAreaCombo.Items.Contains(area)) SourceAreaCombo.SelectedItem = area;
                }
                catch (Exception ex) { MessageBox.Show(ex.Message, "Warp load failed"); Close(); }
            };
            Closing += (_, e) =>
            {
                try
                {
                    FlushGrid();
                    if (Signature() == _saved) return;
                    var result = MessageBox.Show("Save warp changes before closing?", "Unsaved warps", MessageBoxButton.YesNoCancel);
                    e.Cancel = result == MessageBoxResult.Cancel || result == MessageBoxResult.Yes && !Save();
                }
                catch(Exception ex) { e.Cancel=true; StatusBarText.Text=ex.Message; }
            };
        }
        private void FlushGrid()
        {
            if (!WarpGrid.CommitEdit(DataGridEditingUnit.Cell, true) || !WarpGrid.CommitEdit(DataGridEditingUnit.Row, true))
                throw new InvalidDataException("Finish or cancel the invalid grid edit before continuing");
        }
        private string Signature() => string.Join("\n", Warps.Select(w => $"{w.Offset}|{w.SourceArea}|{w.MacroType}|{w.Id}|{w.DestLevel}|{w.DestArea}|{w.DestNode}|{w.Flags}"));
        private void Reload()
        {
            string? selected = SourceAreaCombo.SelectedItem as string;
            _document = new WarpScriptDocument(_scriptPath); Warps.Clear();
            foreach (var node in _document.Nodes) Warps.Add(node);
            string root=Directory.GetParent(Directory.GetParent(Path.GetDirectoryName(_scriptPath)!)!.FullName)!.FullName;
            DestLevelColumn.ItemsSource=WarpConnectionChecks.Levels(root).Keys.Concat(Warps.Select(node=>node.DestLevel)).Distinct().OrderBy(value=>value).ToArray();
            SourceAreaCombo.ItemsSource = _document.Areas;
            SourceAreaCombo.SelectedItem = selected != null && _document.Areas.Contains(selected) ? selected : _document.Areas.FirstOrDefault();
            _saved = Signature(); StatusBarText.Text = $"{Warps.Count} nodes across {_document.Areas.Length} source areas; matching warp objects must be placed separately";
        }
        private void AreaFilter_Changed(object sender, RoutedEventArgs e)
        {
            if (_document == null || ShowAllAreas == null) return;
            try
            {
                FlushGrid();
                CollectionViewSource.GetDefaultView(Warps).Filter=item=>ShowAllAreas.IsChecked==true || item is WarpScriptDocument.Node node && node.SourceArea==SourceAreaCombo.SelectedItem as string;
            }
            catch(Exception ex) { StatusBarText.Text=ex.Message; }
        }
        private void DuplicateWarp_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                FlushGrid();
                if (WarpGrid.SelectedItem is not WarpScriptDocument.Node source) return;
                var copy=new WarpScriptDocument.Node { SourceArea=source.SourceArea,Id=WarpScriptDocument.NextId(Warps,source.SourceArea),MacroType=source.MacroType,DestLevel=source.DestLevel,DestArea=source.DestArea,DestNode=source.DestNode,Flags=source.Flags };
                Warps.Add(copy); WarpGrid.SelectedItem=copy; WarpGrid.ScrollIntoView(copy);
            }
            catch(Exception ex) { StatusBarText.Text=ex.Message; }
        }
        private void ValidateWarps_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                FlushGrid();
                var problems=WarpConnectionChecks.Inspect(Path.GetDirectoryName(_scriptPath)!,Warps.ToList());
                MessageBox.Show(problems.Length==0 ? "All destination nodes were found in the current source" : string.Join("\n",problems),"Warp connection report");
            }
            catch(Exception ex) { StatusBarText.Text=ex.Message; }
        }
        private void AddWarp_Click(object sender, RoutedEventArgs e)
        {
            if (SourceAreaCombo.SelectedItem is not string area) { StatusBarText.Text = "Select a source area first"; return; }
            string id;
            try { id = WarpScriptDocument.NextId(Warps, area); } catch (Exception ex) { StatusBarText.Text = ex.Message; return; }
            string levelDirectory=Path.GetDirectoryName(_scriptPath)!;
            string root=Directory.GetParent(Directory.GetParent(levelDirectory)!.FullName)!.FullName;
            string? currentLevel=WarpConnectionChecks.Levels(root).FirstOrDefault(pair=>pair.Value==Path.GetFileName(levelDirectory)).Key;
            var node = new WarpScriptDocument.Node { Id = id, DestNode = Warps.FirstOrDefault(existing=>existing.SourceArea==area)?.Id ?? id, SourceArea = area,
                DestLevel=currentLevel ?? Warps.FirstOrDefault()?.DestLevel ?? "LEVEL_CASTLE",DestArea=area };
            Warps.Add(node); WarpGrid.SelectedItem = node; WarpGrid.ScrollIntoView(node);
        }
        private void DeleteWarp_Click(object sender, RoutedEventArgs e) { if (WarpGrid.SelectedItem is WarpScriptDocument.Node node) Warps.Remove(node); }
        private bool Save()
        {
            try
            {
                FlushGrid();
                if (_document == null) throw new InvalidOperationException("Load a level script before saving");
                _document.Save(Warps.ToList()); Reload();
                StatusBarText.Text = "Saved; source areas, unrelated macros and original backup preserved";
                try { _reload?.Invoke(); } catch (Exception ex) { StatusBarText.Text = "Saved; level refresh failed: " + ex.Message; }
                return true;
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Warps were not saved", MessageBoxButton.OK, MessageBoxImage.Error); return false; }
        }
        private void SaveButton_Click(object sender, RoutedEventArgs e) => Save();
        private void Reload_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                FlushGrid();
                if (Signature() != _saved && MessageBox.Show("Discard unsaved warp edits and reload?", "Reload warps", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                Reload();
            }
            catch (Exception ex) { StatusBarText.Text = ex.Message; }
        }
        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    }
}
