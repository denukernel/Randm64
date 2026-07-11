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

        private readonly string? _paintingPath;
        private readonly PaintingService _paintingService = new();
        private readonly Action _reloadCallback;

        public ObservableCollection<PaintingViewModel> Paintings { get; } = new();

        public PaintingEditorWindow(string levelPath, Action reloadCallback)
        {
            InitializeComponent();

            _reloadCallback = reloadCallback;
            _paintingPath = _paintingService.FindPaintingFile(levelPath);

            PaintingGrid.ItemsSource = Paintings;

            this.Loaded += (s, e) =>
            {
                if (string.IsNullOrEmpty(_paintingPath) || !File.Exists(_paintingPath))
                {
                    MessageBox.Show("No painting.inc.c file found in this level's folders.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                    Close();
                }
            };

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

            StatusBarText.Text = $"Loaded {Paintings.Count} painting structures from: {Path.GetFileName(_paintingPath)}";
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_paintingPath)) return;

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
                MessageBox.Show("Painting structure changes saved successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                LoadPaintings();
                _reloadCallback?.Invoke();
            }
            else
            {
                MessageBox.Show("Failed to save changes. Make sure file is not open elsewhere.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
