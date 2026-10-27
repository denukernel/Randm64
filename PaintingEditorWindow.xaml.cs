using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using Sm64DecompLevelViewer.Services;

namespace Sm64DecompLevelViewer
{
    public partial class PaintingEditorWindow : Window
    {
        public class PaintingViewModel
        {
            public string Name { get; set; } = string.Empty;
            public string Id { get; set; } = "0x0000";
            public string PosX { get; set; } = "0.0f";
            public string PosY { get; set; } = "0.0f";
            public string PosZ { get; set; } = "0.0f";
            public string Pitch { get; set; } = "0.0f";
            public string Yaw { get; set; } = "0.0f";
            public string Size { get; set; } = "614.0f";

            public string OriginalBlock { get; set; } = string.Empty;
        }

        private string? _paintingPath;
        private readonly string _levelPath;
        private string _saved = "";
        private bool _selecting;
        private readonly PaintingService _paintingService = new();
        private readonly Action _reloadCallback;

        public ObservableCollection<PaintingViewModel> Paintings { get; } = new();

        public PaintingEditorWindow(string levelPath, Action reloadCallback)
        {
            InitializeComponent();

            _reloadCallback = reloadCallback;
            _levelPath = levelPath;
            _paintingPath = _paintingService.FindPaintingFile(levelPath);

            PaintingGrid.ItemsSource = Paintings;

            Loaded += (_, _) =>
            {
                try
                {
                    var files = Directory.EnumerateFiles(_levelPath, "*painting*.inc.c", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).Select(p => Path.GetRelativePath(_levelPath, p)).ToArray();
                    _selecting = true; PaintingFileCombo.ItemsSource = files; PaintingFileCombo.SelectedItem = _paintingPath == null ? null : Path.GetRelativePath(_levelPath, _paintingPath); _selecting = false;
                    if (files.Length == 0) StatusBarText.Text = "No painting sources found in this level";
                }
                catch (Exception ex) { StatusBarText.Text = ex.Message; }
            };
            Closing += (_, e) => e.Cancel = !ConfirmLeave();

            LoadPaintings();
        }

        private void LoadPaintings()
        {
            if (string.IsNullOrEmpty(_paintingPath) || !File.Exists(_paintingPath))
            {
                return;
            }

            var list = _paintingService.LoadPaintings(_paintingPath);
            Paintings.Clear();

            foreach (var item in list)
            {
                Paintings.Add(new PaintingViewModel
                {
                    Name = item.Name,
                    Id = item.Id,
                    PosX = item.PosX,
                    PosY = item.PosY,
                    PosZ = item.PosZ,
                    Pitch = item.Pitch,
                    Yaw = item.Yaw,
                    Size = item.Size,
                    OriginalBlock = item.OriginalBlock
                });
            }

            _saved = Signature();
            StatusBarText.Text = $"Loaded {Paintings.Count} painting structures from: {Path.GetFileName(_paintingPath)}";
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e) => Save();

        private bool Save()
        {
            if (string.IsNullOrEmpty(_paintingPath)) return true;

            PaintingGrid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Cell, true);
            PaintingGrid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Row, true);
            var items = Paintings.Select(p => new PaintingData
            {
                Name = p.Name,
                Id = p.Id,
                PosX = p.PosX,
                PosY = p.PosY,
                PosZ = p.PosZ,
                Pitch = p.Pitch,
                Yaw = p.Yaw,
                Size = p.Size,
                OriginalBlock = p.OriginalBlock
            }).ToList();

            if (_paintingService.SavePaintings(_paintingPath, items))
            {
                LoadPaintings();
                _reloadCallback?.Invoke();
                return true;
            }
            else
            {
                MessageBox.Show(_paintingService.LastError ?? "Painting changes were not saved", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private string Signature() => string.Join("\n", Paintings.Select(p => $"{p.Name}|{p.Id}|{p.Pitch}|{p.Yaw}|{p.PosX}|{p.PosY}|{p.PosZ}|{p.Size}"));
        private bool ConfirmLeave()
        {
            PaintingGrid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Cell, true);
            PaintingGrid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Row, true);
            if (Signature() == _saved) return true;
            var choice = MessageBox.Show("Save painting changes?", "Unsaved paintings", MessageBoxButton.YesNoCancel);
            return choice == MessageBoxResult.No || choice == MessageBoxResult.Yes && Save();
        }
        private void PaintingFile_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_selecting || PaintingFileCombo.SelectedItem is not string relative) return;
            if (!ConfirmLeave()) { _selecting = true; PaintingFileCombo.SelectedItem = _paintingPath == null ? null : Path.GetRelativePath(_levelPath, _paintingPath); _selecting = false; return; }
            _paintingPath = Path.Combine(_levelPath, relative);
            try { LoadPaintings(); } catch (Exception ex) { StatusBarText.Text = ex.Message; }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
        private void Reload_Click(object sender, RoutedEventArgs e)
        {
            PaintingGrid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Cell, true);
            PaintingGrid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Row, true);
            if (Signature() != _saved && MessageBox.Show("Discard unsaved painting edits and reload?", "Reload paintings", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            try { LoadPaintings(); } catch (Exception ex) { StatusBarText.Text = ex.Message; }
        }
    }
}
