using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Sm64DecompLevelViewer.Models;
using Sm64DecompLevelViewer.Services;

namespace Sm64DecompLevelViewer
{
    public partial class LevelMeshEditorWindow : Window
    {
        public class ColVertexViewModel
        {
            public int Index { get; set; }
            public int X { get; set; }
            public int Y { get; set; }
            public int Z { get; set; }
        }

        public class ColTriangleViewModel
        {
            public int Index { get; set; }
            public int V1 { get; set; }
            public int V2 { get; set; }
            public int V3 { get; set; }
            public string SurfaceType { get; set; } = "SURFACE_DEFAULT";
            public int? SpecialParam { get; set; }
        }

        public class WaterBoxViewModel
        {
            public int Id { get; set; }
            public int X1 { get; set; }
            public int Z1 { get; set; }
            public int X2 { get; set; }
            public int Z2 { get; set; }
            public int Y { get; set; }
        }

        public class ModelVertexViewModel
        {
            public int Index { get; set; }
            public int X { get; set; }
            public int Y { get; set; }
            public int Z { get; set; }
            public int S { get; set; }
            public int T { get; set; }
            public byte NX { get; set; }
            public byte NY { get; set; }
            public byte NZ { get; set; }
            public byte Alpha { get; set; }
        }

        private static readonly List<string> CollisionSurfaces = new()
        {
            "SURFACE_DEFAULT",
            "SURFACE_BURNING",
            "SURFACE_0004",
            "SURFACE_HANGABLE",
            "SURFACE_SLOW",
            "SURFACE_DEATH_PLANE",
            "SURFACE_CLOSE_CAMERA",
            "SURFACE_WATER",
            "SURFACE_FLOWING_WATER",
            "SURFACE_INTANGIBLE",
            "SURFACE_VERY_SLIPPERY",
            "SURFACE_SLIPPERY",
            "SURFACE_NOT_SLIPPERY",
            "SURFACE_TTM_VINES",
            "SURFACE_MGR_MUSIC",
            "SURFACE_INSTANT_WARP_1B",
            "SURFACE_INSTANT_WARP_1C",
            "SURFACE_INSTANT_WARP_1D",
            "SURFACE_INSTANT_WARP_1E",
            "SURFACE_SHALLOW_QUICKSAND",
            "SURFACE_DEEP_QUICKSAND",
            "SURFACE_INSTANT_QUICKSAND",
            "SURFACE_DEEP_MOVING_QUICKSAND",
            "SURFACE_SHALLOW_MOVING_QUICKSAND",
            "SURFACE_QUICKSAND",
            "SURFACE_MOVING_QUICKSAND",
            "SURFACE_WALL_MISC",
            "SURFACE_NOISE_DEFAULT",
            "SURFACE_NOISE_SLIPPERY",
            "SURFACE_HORIZONTAL_WIND",
            "SURFACE_INSTANT_MOVING_QUICKSAND",
            "SURFACE_ICE",
            "SURFACE_LOOK_UP_WARP",
            "SURFACE_HARD",
            "SURFACE_WARP",
            "SURFACE_TIMER_START",
            "SURFACE_TIMER_END",
            "SURFACE_HARD_SLIPPERY",
            "SURFACE_HARD_VERY_SLIPPERY",
            "SURFACE_HARD_NOT_SLIPPERY",
            "SURFACE_VERTICAL_WIND",
            "SURFACE_BOSS_FIGHT_CAMERA",
            "SURFACE_CAMERA_FREE_ROAM",
            "SURFACE_THI3_WALLKICK",
            "SURFACE_CAMERA_8_DIR",
            "SURFACE_CAMERA_MIDDLE",
            "SURFACE_CAMERA_ROTATE_RIGHT",
            "SURFACE_CAMERA_ROTATE_LEFT",
            "SURFACE_CAMERA_BOUNDARY",
            "SURFACE_NOISE_VERY_SLIPPERY_73",
            "SURFACE_NOISE_VERY_SLIPPERY_74",
            "SURFACE_NOISE_VERY_SLIPPERY",
            "SURFACE_NO_CAM_COLLISION",
            "SURFACE_NO_CAM_COLLISION_77",
            "SURFACE_NO_CAM_COL_VERY_SLIPPERY",
            "SURFACE_NO_CAM_COL_SLIPPERY",
            "SURFACE_SWITCH",
            "SURFACE_VANISH_CAP_WALLS",
            "SURFACE_PAINTING_WOBBLE_A6",
            "SURFACE_PAINTING_WOBBLE_A7",
            "SURFACE_PAINTING_WOBBLE_A8",
            "SURFACE_PAINTING_WOBBLE_A9",
            "SURFACE_PAINTING_WOBBLE_AA",
            "SURFACE_PAINTING_WOBBLE_AB",
            "SURFACE_PAINTING_WOBBLE_AC",
            "SURFACE_PAINTING_WOBBLE_AD",
            "SURFACE_PAINTING_WOBBLE_AE",
            "SURFACE_PAINTING_WOBBLE_AF",
            "SURFACE_PAINTING_WOBBLE_B0",
            "SURFACE_PAINTING_WOBBLE_B1",
            "SURFACE_PAINTING_WOBBLE_B2",
            "SURFACE_PAINTING_WOBBLE_B3",
            "SURFACE_PAINTING_WOBBLE_B4",
            "SURFACE_PAINTING_WOBBLE_B5",
            "SURFACE_PAINTING_WOBBLE_B6",
            "SURFACE_PAINTING_WOBBLE_B7",
            "SURFACE_PAINTING_WOBBLE_B8",
            "SURFACE_PAINTING_WOBBLE_B9",
            "SURFACE_PAINTING_WOBBLE_BA",
            "SURFACE_PAINTING_WOBBLE_BB",
            "SURFACE_PAINTING_WOBBLE_BC",
            "SURFACE_PAINTING_WOBBLE_BD",
            "SURFACE_PAINTING_WOBBLE_BE",
            "SURFACE_PAINTING_WOBBLE_BF",
            "SURFACE_PAINTING_WOBBLE_C0",
            "SURFACE_PAINTING_WOBBLE_C1",
            "SURFACE_PAINTING_WOBBLE_C2",
            "SURFACE_PAINTING_WOBBLE_C3",
            "SURFACE_PAINTING_WOBBLE_C4",
            "SURFACE_PAINTING_WOBBLE_C5",
            "SURFACE_PAINTING_WOBBLE_C6",
            "SURFACE_PAINTING_WOBBLE_C7",
            "SURFACE_PAINTING_WOBBLE_C8",
            "SURFACE_PAINTING_WOBBLE_C9",
            "SURFACE_PAINTING_WOBBLE_CA",
            "SURFACE_PAINTING_WOBBLE_CB",
            "SURFACE_PAINTING_WOBBLE_CC",
            "SURFACE_PAINTING_WOBBLE_CD",
            "SURFACE_PAINTING_WOBBLE_CE",
            "SURFACE_PAINTING_WOBBLE_CF",
            "SURFACE_PAINTING_WOBBLE_D0",
            "SURFACE_PAINTING_WOBBLE_D1",
            "SURFACE_PAINTING_WOBBLE_D2",
            "SURFACE_PAINTING_WARP_D3",
            "SURFACE_PAINTING_WARP_D4",
            "SURFACE_PAINTING_WARP_D5",
            "SURFACE_PAINTING_WARP_D6",
            "SURFACE_PAINTING_WARP_D7",
            "SURFACE_PAINTING_WARP_D8",
            "SURFACE_PAINTING_WARP_D9",
            "SURFACE_PAINTING_WARP_DA",
            "SURFACE_PAINTING_WARP_DB",
            "SURFACE_PAINTING_WARP_DC",
            "SURFACE_PAINTING_WARP_DD",
            "SURFACE_PAINTING_WARP_DE",
            "SURFACE_PAINTING_WARP_DF",
            "SURFACE_PAINTING_WARP_E0",
            "SURFACE_PAINTING_WARP_E1",
            "SURFACE_PAINTING_WARP_E2",
            "SURFACE_PAINTING_WARP_E3",
            "SURFACE_PAINTING_WARP_E4",
            "SURFACE_PAINTING_WARP_E5",
            "SURFACE_PAINTING_WARP_E6",
            "SURFACE_PAINTING_WARP_E7",
            "SURFACE_PAINTING_WARP_E8",
            "SURFACE_PAINTING_WARP_E9",
            "SURFACE_PAINTING_WARP_EA",
            "SURFACE_PAINTING_WARP_EB",
            "SURFACE_PAINTING_WARP_EC",
            "SURFACE_PAINTING_WARP_ED",
            "SURFACE_PAINTING_WARP_EE",
            "SURFACE_PAINTING_WARP_EF",
            "SURFACE_PAINTING_WARP_F0",
            "SURFACE_PAINTING_WARP_F1",
            "SURFACE_PAINTING_WARP_F2",
            "SURFACE_PAINTING_WARP_F3",
            "SURFACE_TTC_PAINTING_1",
            "SURFACE_TTC_PAINTING_2",
            "SURFACE_TTC_PAINTING_3",
            "SURFACE_PAINTING_WARP_F7",
            "SURFACE_PAINTING_WARP_F8",
            "SURFACE_PAINTING_WARP_F9",
            "SURFACE_PAINTING_WARP_FA",
            "SURFACE_PAINTING_WARP_FB",
            "SURFACE_PAINTING_WARP_FC",
            "SURFACE_WOBBLING_WARP",
            "SURFACE_TRAPDOOR"
        };

        private readonly string _projectRoot;
        private readonly string _levelPath;
        private readonly Action _reloadCallback;
        private readonly Action<OpenTK.Mathematics.Vector3?, bool> _selectPointCallback;

        private readonly CollisionParser _collisionParser = new();
        private readonly ModelParser _modelParser = new();
        private readonly LevelMeshService _meshService = new();

        private Dictionary<string, string> _collisionFiles = new();
        private Dictionary<string, List<string>> _modelFiles = new();

        private CollisionMesh? _currentColMesh;
        private Dictionary<string, List<ModelVertex>> _currentModelVertexArrays = new();

        public ObservableCollection<ColVertexViewModel> ColVertices { get; } = new();
        public ObservableCollection<ColTriangleViewModel> ColTriangles { get; } = new();
        public ObservableCollection<ModelVertexViewModel> VisualVertices { get; } = new();
        public ObservableCollection<WaterBoxViewModel> WaterBoxes { get; } = new();

        private bool _isUpdatingUi = false;

        public LevelMeshEditorWindow(string levelPath, string initialArea, string projectRoot, Action reloadCallback, Action<OpenTK.Mathematics.Vector3?, bool>? selectPointCallback = null)
        {
            InitializeComponent();

            _levelPath = levelPath;
            _projectRoot = projectRoot;
            _reloadCallback = reloadCallback;
            _selectPointCallback = selectPointCallback ?? ((p, f) => {});

            this.Closing += (s, e) => _selectPointCallback?.Invoke(null, false);

            ColSurfaceColumn.ItemsSource = CollisionSurfaces;

            ColVertexGrid.ItemsSource = ColVertices;
            ColTriangleGrid.ItemsSource = ColTriangles;
            VisualVertexGrid.ItemsSource = VisualVertices;
            WaterBoxGrid.ItemsSource = WaterBoxes;

            LoadAreas(initialArea);
        }

        private void LoadAreas(string initialArea)
        {
            _isUpdatingUi = true;

            _collisionFiles = _collisionParser.FindCollisionFiles(_levelPath);
            _modelFiles = _modelParser.FindModelFiles(_levelPath);

            AreaComboBox.Items.Clear();
            foreach (var area in _collisionFiles.Keys.OrderBy(k => k))
            {
                AreaComboBox.Items.Add(area);
            }

            if (AreaComboBox.Items.Count > 0)
            {
                if (!string.IsNullOrEmpty(initialArea) && AreaComboBox.Items.Contains(initialArea))
                {
                    AreaComboBox.SelectedItem = initialArea;
                }
                else
                {
                    AreaComboBox.SelectedIndex = 0;
                }
            }

            _isUpdatingUi = false;
            LoadSelectedArea();
        }

        private void AreaComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingUi) return;
            LoadSelectedArea();
        }

        private void LoadSelectedArea()
        {
            if (AreaComboBox.SelectedItem is not string selectedArea) return;

            _isUpdatingUi = true;

            // Load Collision Mesh
            if (_collisionFiles.TryGetValue(selectedArea, out string? colPath) && File.Exists(colPath))
            {
                _currentColMesh = _collisionParser.ParseCollisionFile(colPath, selectedArea, "Level");

                ColVertices.Clear();
                ColTriangles.Clear();

                if (_currentColMesh != null)
                {
                    for (int i = 0; i < _currentColMesh.Vertices.Count; i++)
                    {
                        var v = _currentColMesh.Vertices[i];
                        ColVertices.Add(new ColVertexViewModel { Index = i, X = v.X, Y = v.Y, Z = v.Z });
                    }

                    for (int i = 0; i < _currentColMesh.Triangles.Count; i++)
                    {
                        var t = _currentColMesh.Triangles[i];
                        ColTriangles.Add(new ColTriangleViewModel { Index = i, V1 = t.V1, V2 = t.V2, V3 = t.V3, SurfaceType = t.SurfaceType, SpecialParam = t.SpecialParam });
                    }

                    WaterBoxes.Clear();
                    if (_currentColMesh.WaterBoxes != null)
                    {
                        foreach (var wb in _currentColMesh.WaterBoxes)
                        {
                            WaterBoxes.Add(new WaterBoxViewModel { Id = wb.Id, X1 = wb.X1, Z1 = wb.Z1, X2 = wb.X2, Z2 = wb.Z2, Y = wb.Y });
                        }
                    }
                }
            }

            // Load Visual Mesh Files
            VisualModelComboBox.Items.Clear();
            VisualArrayComboBox.Items.Clear();
            VisualVertices.Clear();
            _currentModelVertexArrays.Clear();

            if (_modelFiles.TryGetValue(selectedArea, out List<string>? modelPaths) && modelPaths.Count > 0)
            {
                VisualModelLabel.Visibility = Visibility.Visible;
                VisualModelComboBox.Visibility = Visibility.Visible;

                foreach (var path in modelPaths)
                {
                    VisualModelComboBox.Items.Add(Path.GetFileName(path));
                }
                VisualModelComboBox.SelectedIndex = 0;
            }
            else
            {
                VisualModelLabel.Visibility = Visibility.Collapsed;
                VisualModelComboBox.Visibility = Visibility.Collapsed;
                VisualArrayLabel.Visibility = Visibility.Collapsed;
                VisualArrayComboBox.Visibility = Visibility.Collapsed;
            }

            _isUpdatingUi = false;
        }

        private void VisualModelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingUi) return;
            LoadSelectedModelFile();
        }

        private void LoadSelectedModelFile()
        {
            if (AreaComboBox.SelectedItem is not string selectedArea ||
                VisualModelComboBox.SelectedItem is not string selectedFileName) return;

            if (!_modelFiles.TryGetValue(selectedArea, out List<string>? paths)) return;

            string? modelPath = paths.FirstOrDefault(p => Path.GetFileName(p) == selectedFileName);
            if (modelPath == null || !File.Exists(modelPath)) return;

            _isUpdatingUi = true;

            VisualArrayComboBox.Items.Clear();
            VisualVertices.Clear();

            string content = File.ReadAllText(modelPath);
            _currentModelVertexArrays = _modelParser.ParseVertexArrays(content);

            foreach (var arrayName in _currentModelVertexArrays.Keys.OrderBy(k => k))
            {
                VisualArrayComboBox.Items.Add(arrayName);
            }

            if (VisualArrayComboBox.Items.Count > 0)
            {
                VisualArrayLabel.Visibility = Visibility.Visible;
                VisualArrayComboBox.Visibility = Visibility.Visible;
                VisualArrayComboBox.SelectedIndex = 0;
            }
            else
            {
                VisualArrayLabel.Visibility = Visibility.Collapsed;
                VisualArrayComboBox.Visibility = Visibility.Collapsed;
            }

            _isUpdatingUi = false;
            LoadSelectedVertexArray();
        }

        private void VisualArrayComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingUi) return;
            LoadSelectedVertexArray();
        }

        private void LoadSelectedVertexArray()
        {
            if (VisualArrayComboBox.SelectedItem is not string arrayName) return;

            _isUpdatingUi = true;
            VisualVertices.Clear();

            if (_currentModelVertexArrays.TryGetValue(arrayName, out List<ModelVertex>? vertices))
            {
                for (int i = 0; i < vertices.Count; i++)
                {
                    var v = vertices[i];
                    VisualVertices.Add(new ModelVertexViewModel
                    {
                        Index = i,
                        X = v.X,
                        Y = v.Y,
                        Z = v.Z,
                        S = v.S,
                        T = v.T,
                        NX = v.NX,
                        NY = v.NY,
                        NZ = v.NZ,
                        Alpha = v.Alpha
                    });
                }
            }

            _isUpdatingUi = false;
        }

        private void MeshTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Sync selected target in Bulk operations depending on selected Tab
            if (BulkTargetComboBox == null) return;
            if (MeshTabControl.SelectedIndex == 0 || MeshTabControl.SelectedIndex == 1)
            {
                BulkTargetComboBox.SelectedIndex = 0; // Collision Mesh
            }
            else if (MeshTabControl.SelectedIndex == 2)
            {
                BulkTargetComboBox.SelectedIndex = 1; // Visual Mesh (Active)
            }
        }

        private void AddColVertex_Click(object sender, RoutedEventArgs e)
        {
            ColVertices.Add(new ColVertexViewModel
            {
                Index = ColVertices.Count,
                X = 0,
                Y = 0,
                Z = 0
            });
            ColVertexGrid.ScrollIntoView(ColVertices.Last());
            StatusBarText.Text = "Collision Vertex added.";
        }

        private void DeleteColVertex_Click(object sender, RoutedEventArgs e)
        {
            if (ColVertexGrid.SelectedItem is not ColVertexViewModel selected)
            {
                MessageBox.Show("Please select a collision vertex to delete.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int deletedIndex = selected.Index;
            ColVertices.Remove(selected);

            // Re-index remaining vertices
            for (int i = 0; i < ColVertices.Count; i++)
            {
                ColVertices[i].Index = i;
            }

            // Clean up and shift indices of triangles
            for (int i = ColTriangles.Count - 1; i >= 0; i--)
            {
                var tri = ColTriangles[i];
                if (tri.V1 == deletedIndex || tri.V2 == deletedIndex || tri.V3 == deletedIndex)
                {
                    ColTriangles.RemoveAt(i);
                }
                else
                {
                    if (tri.V1 > deletedIndex) tri.V1--;
                    if (tri.V2 > deletedIndex) tri.V2--;
                    if (tri.V3 > deletedIndex) tri.V3--;
                }
            }

            // Re-index triangles
            for (int i = 0; i < ColTriangles.Count; i++)
            {
                ColTriangles[i].Index = i;
            }

            ColVertexGrid.Items.Refresh();
            ColTriangleGrid.Items.Refresh();
            StatusBarText.Text = $"Deleted Vertex {deletedIndex} and updated triangle references.";
        }

        private void AddColTriangle_Click(object sender, RoutedEventArgs e)
        {
            ColTriangles.Add(new ColTriangleViewModel
            {
                Index = ColTriangles.Count,
                V1 = 0,
                V2 = 0,
                V3 = 0,
                SurfaceType = "SURFACE_DEFAULT"
            });
            ColTriangleGrid.ScrollIntoView(ColTriangles.Last());
            StatusBarText.Text = "Collision Triangle added.";
        }

        private void DeleteColTriangle_Click(object sender, RoutedEventArgs e)
        {
            if (ColTriangleGrid.SelectedItem is not ColTriangleViewModel selected)
            {
                MessageBox.Show("Please select a collision triangle to delete.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ColTriangles.Remove(selected);
            for (int i = 0; i < ColTriangles.Count; i++)
            {
                ColTriangles[i].Index = i;
            }

            ColTriangleGrid.Items.Refresh();
            StatusBarText.Text = "Collision Triangle deleted.";
        }

        private void ApplyTranslation_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(OffsetXText.Text, out int dx) ||
                !int.TryParse(OffsetYText.Text, out int dy) ||
                !int.TryParse(OffsetZText.Text, out int dz))
            {
                MessageBox.Show("Please enter valid integers for translation offsets.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            int target = BulkTargetComboBox.SelectedIndex;

            if (target == 0) // Collision
            {
                foreach (var v in ColVertices)
                {
                    v.X += dx;
                    v.Y += dy;
                    v.Z += dz;
                }
                ColVertexGrid.Items.Refresh();
                StatusBarText.Text = $"Translated collision mesh by ({dx}, {dy}, {dz}).";
            }
            else if (target == 1) // Visual Active
            {
                foreach (var v in VisualVertices)
                {
                    v.X += dx;
                    v.Y += dy;
                    v.Z += dz;
                }
                VisualVertexGrid.Items.Refresh();
                StatusBarText.Text = $"Translated active visual vertex array by ({dx}, {dy}, {dz}).";
            }
            else if (target == 2) // Visual All Arrays
            {
                foreach (var key in _currentModelVertexArrays.Keys)
                {
                    var vertices = _currentModelVertexArrays[key];
                    foreach (var v in vertices)
                    {
                        v.X += dx;
                        v.Y += dy;
                        v.Z += dz;
                    }
                }
                LoadSelectedVertexArray(); // reload display
                StatusBarText.Text = $"Translated all visual vertex arrays by ({dx}, {dy}, {dz}).";
            }
        }

        private void ApplyScale_Click(object sender, RoutedEventArgs e)
        {
            if (!double.TryParse(ScaleXText.Text, out double sx) ||
                !double.TryParse(ScaleYText.Text, out double sy) ||
                !double.TryParse(ScaleZText.Text, out double sz))
            {
                MessageBox.Show("Please enter valid decimal factors for scale.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            int target = BulkTargetComboBox.SelectedIndex;

            if (target == 0) // Collision
            {
                foreach (var v in ColVertices)
                {
                    v.X = (int)Math.Round(v.X * sx);
                    v.Y = (int)Math.Round(v.Y * sy);
                    v.Z = (int)Math.Round(v.Z * sz);
                }
                ColVertexGrid.Items.Refresh();
                StatusBarText.Text = $"Scaled collision mesh by ({sx}, {sy}, {sz}).";
            }
            else if (target == 1) // Visual Active
            {
                foreach (var v in VisualVertices)
                {
                    v.X = (int)Math.Round(v.X * sx);
                    v.Y = (int)Math.Round(v.Y * sy);
                    v.Z = (int)Math.Round(v.Z * sz);
                }
                VisualVertexGrid.Items.Refresh();
                StatusBarText.Text = $"Scaled active visual vertex array by ({sx}, {sy}, {sz}).";
            }
            else if (target == 2) // Visual All Arrays
            {
                foreach (var key in _currentModelVertexArrays.Keys)
                {
                    var vertices = _currentModelVertexArrays[key];
                    foreach (var v in vertices)
                    {
                        v.X = (int)Math.Round(v.X * sx);
                        v.Y = (int)Math.Round(v.Y * sy);
                        v.Z = (int)Math.Round(v.Z * sz);
                    }
                }
                LoadSelectedVertexArray(); // reload display
                StatusBarText.Text = $"Scaled all visual vertex arrays by ({sx}, {sy}, {sz}).";
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (AreaComboBox.SelectedItem is not string selectedArea) return;

            bool collisionSaved = false;
            bool visualSaved = false;

            // 1. Save Collision
            if (_currentColMesh != null && _collisionFiles.TryGetValue(selectedArea, out string? colPath))
            {
                _currentColMesh.Vertices = ColVertices.Select(v => new CollisionVertex(v.X, v.Y, v.Z)).ToList();
                _currentColMesh.Triangles = ColTriangles.Select(t => new CollisionTriangle(t.V1, t.V2, t.V3, t.SurfaceType, t.SpecialParam)).ToList();
                _currentColMesh.WaterBoxes = WaterBoxes.Select(w => new WaterBox(w.Id, w.X1, w.Z1, w.X2, w.Z2, w.Y)).ToList();

                collisionSaved = _meshService.SaveCollisionMesh(colPath, _currentColMesh);
            }

            // 2. Save Visual
            if (VisualArrayComboBox.SelectedItem is string arrayName &&
                VisualModelComboBox.SelectedItem is string modelFileName &&
                _modelFiles.TryGetValue(selectedArea, out List<string>? modelPaths))
            {
                string? modelPath = modelPaths.FirstOrDefault(p => Path.GetFileName(p) == modelFileName);
                if (modelPath != null)
                {
                    // Map active array edited values
                    var activeList = VisualVertices.Select(v => new ModelVertex(v.X, v.Y, v.Z, v.S, v.T, v.NX, v.NY, v.NZ, v.Alpha)).ToList();
                    _currentModelVertexArrays[arrayName] = activeList;

                    // If "All Arrays" target is active, we write all arrays back to file. Otherwise just the active one.
                    if (BulkTargetComboBox.SelectedIndex == 2)
                    {
                        bool allOk = true;
                        foreach (var kvp in _currentModelVertexArrays)
                        {
                            if (!_meshService.SaveVisualMesh(modelPath, kvp.Key, kvp.Value)) allOk = false;
                        }
                        visualSaved = allOk;
                    }
                    else
                    {
                        visualSaved = _meshService.SaveVisualMesh(modelPath, arrayName, activeList);
                    }
                }
            }

            if (collisionSaved || visualSaved)
            {
                MessageBox.Show("Mesh changes compiled and saved successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                _reloadCallback?.Invoke();
            }
            else
            {
                MessageBox.Show("Failed to save changes. Please check log files or terminal output.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void ColVertexGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingUi) return;
            if (ColVertexGrid.SelectedItem is ColVertexViewModel selected)
            {
                var pt = new OpenTK.Mathematics.Vector3(selected.X, selected.Y, selected.Z);
                _selectPointCallback?.Invoke(pt, true);
            }
        }

        private void ColTriangleGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingUi) return;
            if (ColTriangleGrid.SelectedItem is ColTriangleViewModel selected)
            {
                if (selected.V1 < ColVertices.Count && selected.V2 < ColVertices.Count && selected.V3 < ColVertices.Count)
                {
                    var v1 = ColVertices[selected.V1];
                    var v2 = ColVertices[selected.V2];
                    var v3 = ColVertices[selected.V3];
                    var center = new OpenTK.Mathematics.Vector3(
                        (v1.X + v2.X + v3.X) / 3f,
                        (v1.Y + v2.Y + v3.Y) / 3f,
                        (v1.Z + v2.Z + v3.Z) / 3f
                    );
                    _selectPointCallback?.Invoke(center, true);
                }
            }
        }

        private void VisualVertexGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingUi) return;
            if (VisualVertexGrid.SelectedItem is ModelVertexViewModel selected)
            {
                var pt = new OpenTK.Mathematics.Vector3(selected.X, selected.Y, selected.Z);
                _selectPointCallback?.Invoke(pt, true);
            }
        }

        private void AddWaterBox_Click(object sender, RoutedEventArgs e)
        {
            int nextId = WaterBoxes.Count > 0 ? WaterBoxes.Max(w => w.Id) + 1 : 0;
            var newWb = new WaterBoxViewModel
            {
                Id = nextId,
                X1 = -1000,
                Z1 = -1000,
                X2 = 1000,
                Z2 = 1000,
                Y = 0
            };
            WaterBoxes.Add(newWb);
            WaterBoxGrid.SelectedItem = newWb;
            WaterBoxGrid.ScrollIntoView(newWb);
            StatusBarText.Text = $"Water Box added with ID {nextId}.";
        }

        private void DeleteWaterBox_Click(object sender, RoutedEventArgs e)
        {
            if (WaterBoxGrid.SelectedItem is WaterBoxViewModel selected)
            {
                WaterBoxes.Remove(selected);
                StatusBarText.Text = $"Water Box with ID {selected.Id} deleted.";
            }
            else
            {
                MessageBox.Show("Please select a water box to delete.", "Select Water Box", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void WaterBoxGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingUi) return;
            if (WaterBoxGrid.SelectedItem is WaterBoxViewModel selected)
            {
                var pt = new OpenTK.Mathematics.Vector3((selected.X1 + selected.X2) / 2f, selected.Y, (selected.Z1 + selected.Z2) / 2f);
                _selectPointCallback?.Invoke(pt, true);
            }
        }

        public void SelectCollisionTriangle(int index)
        {
            if (index < 0 || index >= ColTriangles.Count) return;

            // Switch to Collision Triangles tab
            MeshTabControl.SelectedIndex = 1;

            // Select and scroll into view
            var item = ColTriangles[index];
            ColTriangleGrid.SelectedItem = item;
            ColTriangleGrid.ScrollIntoView(item);
        }

        public void SelectVisualVertex(int index)
        {
            if (index < 0 || index >= VisualVertices.Count) return;

            // Switch to Visual Vertices tab
            MeshTabControl.SelectedIndex = 2;

            // Select and scroll into view
            var item = VisualVertices[index];
            VisualVertexGrid.SelectedItem = item;
            VisualVertexGrid.ScrollIntoView(item);
        }
    }
}
