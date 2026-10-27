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
        public event Action? EmbeddedCloseRequested;
        public event Action<bool,string?>? PlacementPickingChanged;
        private ModelImportPreviewWindow? placementPreview;
        public bool HasPlacementPreview => placementPreview!=null;
        public bool SaveTableChanges()
        {
            StatusBarText.Text = "Saving mesh tables...";
            SaveButton_Click(this, new RoutedEventArgs());
            return StatusBarText.Text.StartsWith("Geometry, collision and rooms saved", StringComparison.Ordinal);
        }
        public void PlaceBox(string size, bool collision)
        {
            CreateSize.Text = size;
            CreateCollision.IsChecked = collision;
            AddPrimitive_Click(new Button { Tag = "box-preview" }, new RoutedEventArgs());
        }
        private FrameworkElement? embeddedView;
        public void PlaceOnTerrain(OpenTK.Mathematics.Vector3 point)
        {
            if(placementPreview==null) return;
            placementPreview.PlaceOnTerrain(point);
            _selectPointCallback?.Invoke(point,false);
        }
        public void ResumePlacement() => placementPreview?.ResumePlacement();
        private async Task<bool> ShowPlacementAsync(ModelImportPreviewWindow preview,string? excluded=null)
        {
            placementPreview=preview;
            var view=embeddedView ?? Content as FrameworkElement;
            if(view!=null) view.IsEnabled=false;
            preview.PickInLevelRequested+=()=>StatusBarText.Text="Shift-click terrain in the level viewport. Press Escape to return to the preview.";
            PlacementPickingChanged?.Invoke(true,excluded);
            try { return await preview.ShowForPlacementAsync(preview.Owner); }
            finally
            {
                placementPreview=null;
                PlacementPickingChanged?.Invoke(false,null);
                if(view!=null) view.IsEnabled=true;
            }
        }
        private readonly Func<OpenTK.Mathematics.Vector3>? _placementOrigin;

        private async void AssembleImported_Click(object sender,RoutedEventArgs e)
        {
            try
            {
                if(ImportedModelsCombo.SelectedItem is not ImportedModelEntry entry || AreaComboBox.SelectedItem is not string area) return;
                var original=ImportedModelPlacement.Read(entry,_projectRoot);
                var centered=ImportedModelPlacement.AtBase(original);
                var context=PlacementContext(area,entry.Symbol);
                var excluded=ImportedModelPlacement.CollisionTriangles(original,context.Ground,entry);
                var preview=new ModelImportPreviewWindow(centered.Local,entry.Symbol,centered.Pivot,1,context.Terrain,context.Ground,excluded,false)
                { Owner=Application.Current?.Windows.OfType<Window>().FirstOrDefault(w=>w.IsActive),Title="Assemble imported model",ConfirmationText="Apply placement" };
                if(!await ShowPlacementAsync(preview,entry.Symbol) || preview.Result==null) return;
                string geo=Path.Combine(_levelPath,"areas",System.Text.RegularExpressions.Regex.Match(area,@"\d+").Value,"geo.inc.c");
                ImportedModelPlacement.Save(entry,geo,_collisionFiles[area],original,preview.Result,_projectRoot);
                LoadSelectedArea(); _reloadCallback?.Invoke();
                _selectPointCallback?.Invoke(preview.Result.Vertices.Aggregate(OpenTK.Mathematics.Vector3.ComponentMin),true);
                StatusBarText.Text="Imported model placement and collision saved.";
            }
            catch(Exception ex) { MessageBox.Show(ex.Message,"Assemble model",MessageBoxButton.OK,MessageBoxImage.Error); }
        }
        public FrameworkElement CreateEmbeddedView()
        {
            var view = (FrameworkElement)Content;
            embeddedView=view;
            Content = null;
            view.Resources.MergedDictionaries.Add(Resources);
            AreaComboBox.IsEnabled = false;
            return view;
        }
        private async void AddPrimitive_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var center = new OpenTK.Mathematics.Vector3(float.Parse(CreateX.Text),float.Parse(CreateY.Text),float.Parse(CreateZ.Text));
                string area = AreaComboBox.SelectedItem as string ?? throw new Exception("Select an area first.");
                EditableGeometry geometry;
                if ((sender as Button)?.Tag as string == "model")
                {
                    var dialog = new Microsoft.Win32.OpenFileDialog { Filter = ModelImportService.FileFilter };
                    if (dialog.ShowDialog() != true) return;
                    float scale=float.Parse(ImportScale.Text);
                    ((Button)sender).IsEnabled=false;
                    StatusBarText.Text="Importing model and materials…";
                    geometry = await System.Threading.Tasks.Task.Run(()=>ModelImportService.Load(dialog.FileName));
                    if(!IsLoaded && Application.Current?.Dispatcher.HasShutdownStarted==true) return;
                    geometry=ImportedModelPlacement.AtBase(geometry).Local;
                    if(center==OpenTK.Mathematics.Vector3.Zero && _placementOrigin!=null) center=_placementOrigin();
                    var context=PlacementContext(area);
                    var preview=new ModelImportPreviewWindow(geometry,dialog.FileName,center,scale,context.Terrain,context.Ground);
                    preview.Owner=Application.Current?.Windows.OfType<Window>().FirstOrDefault(w=>w.IsActive);
                    if(!await ShowPlacementAsync(preview) || preview.Result==null)
                    { StatusBarText.Text="Model import canceled."; return; }
                    geometry=preview.Result;
                }
                else if ((sender as Button)?.Tag as string == "box-preview")
                {
                    geometry = LevelMeshCreation.Primitive(OpenTK.Mathematics.Vector3.Zero, float.Parse(CreateSize.Text), true);
                    var context = PlacementContext(area);
                    var preview = new ModelImportPreviewWindow(geometry, "box", _placementOrigin?.Invoke() ?? center, 1, context.Terrain, context.Ground)
                    { Owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive), Title = "Place box", ConfirmationText = "add box" };
                    if (!await ShowPlacementAsync(preview) || preview.Result == null) return;
                    geometry = preview.Result;
                }
                else geometry = LevelMeshCreation.Primitive(center,float.Parse(CreateSize.Text),(sender as Button)?.Tag as string == "box");
                string model = _modelFiles[area].FirstOrDefault() ?? throw new Exception("No compiled model source found for this area.");
                if (!Path.IsPathRooted(model)) model = Path.Combine(_levelPath,model);
                string collision = _collisionFiles[area];
                string geo = Path.Combine(_levelPath,"areas",System.Text.RegularExpressions.Regex.Match(area,@"\d+").Value,"geo.inc.c");
                string symbol = LevelMeshCreation.Append(model,geo,collision,geometry,CreateCollision.IsChecked == true,_projectRoot);
                LoadSelectedArea();
                StatusBarText.Text = $"Added and saved {geometry.Triangles.Count} triangles ({symbol}).";
                _reloadCallback?.Invoke();
                _selectPointCallback?.Invoke(geometry.Vertices.Aggregate(OpenTK.Mathematics.Vector3.ComponentMin),true);
            }
            catch (Exception ex) { MessageBox.Show(ex.Message,"Add mesh",MessageBoxButton.OK,MessageBoxImage.Error); }
            finally { if(sender is Button button) button.IsEnabled=true; }
        }

        private static List<string> CollisionSurfaces => Models.Levels.CollisionSurfaceCatalog.CollisionSurfaces;

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
        private readonly Dictionary<string,byte[]> loadedSourceVersions=new(StringComparer.OrdinalIgnoreCase);

        public LevelMeshEditorWindow(string levelPath, string initialArea, string projectRoot, Action reloadCallback, Action<OpenTK.Mathematics.Vector3?, bool>? selectPointCallback = null,Func<OpenTK.Mathematics.Vector3>? placementOrigin=null)
        {
            InitializeComponent();
            _placementOrigin=placementOrigin;

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

            var previousImport=(ImportedModelsCombo.SelectedItem as ImportedModelEntry)?.Symbol;
            ImportedModelsCombo.ItemsSource=_modelFiles.TryGetValue(selectedArea,out var importedFiles) ? ImportedModelPlacement.Find(importedFiles) : new();
            ImportedModelsCombo.SelectedItem=ImportedModelsCombo.Items.OfType<ImportedModelEntry>().FirstOrDefault(i=>i.Symbol==previousImport);
            if(ImportedModelsCombo.SelectedIndex<0 && ImportedModelsCombo.Items.Count>0) ImportedModelsCombo.SelectedIndex=ImportedModelsCombo.Items.Count-1;

            _isUpdatingUi = true;

            // Load Collision Mesh
            if (_collisionFiles.TryGetValue(selectedArea, out string? colPath) && File.Exists(colPath))
            {
                loadedSourceVersions[colPath]=File.ReadAllBytes(colPath);
                foreach(string name in new[]{"room.inc.c","rooms.inc.c"})
                { string room=Path.Combine(Path.GetDirectoryName(colPath)!,name); if(File.Exists(room)) loadedSourceVersions[room]=File.ReadAllBytes(room); }
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
                    VisualModelComboBox.Items.Add(Path.GetRelativePath(_levelPath,path));
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
            LoadSelectedModelFile();
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

            string? modelPath = paths.FirstOrDefault(p => Path.GetRelativePath(_levelPath,p) == selectedFileName);
            if (modelPath == null || !File.Exists(modelPath)) return;

            _isUpdatingUi = true;

            VisualArrayComboBox.Items.Clear();
            VisualVertices.Clear();

            string content = File.ReadAllText(modelPath);
            loadedSourceVersions[modelPath]=File.ReadAllBytes(modelPath);
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

        private bool CommitGridEdits()
        {
            foreach (var grid in new[] { ColVertexGrid, ColTriangleGrid, VisualVertexGrid, WaterBoxGrid })
                if (!grid.CommitEdit(DataGridEditingUnit.Cell, true) || !grid.CommitEdit(DataGridEditingUnit.Row, true)) return false;
            return true;
        }
        private bool TransformFits(int target, double sx, double sy, double sz, int dx = 0, int dy = 0, int dz = 0)
        {
            IEnumerable<(int X, int Y, int Z)> points = target == 0 ? ColVertices.Select(v => (v.X, v.Y, v.Z))
                : target == 1 ? VisualVertices.Select(v => (v.X, v.Y, v.Z))
                : _currentModelVertexArrays.Values.SelectMany(v => v).Select(v => (v.X, v.Y, v.Z));
            bool valid = Services.Levels.MeshTransforms.Fits(points, sx, sy, sz, dx, dy, dz);
            if (!valid) MessageBox.Show("The transform would exceed signed 16-bit source coordinates; no vertices were changed", "Mesh transform");
            return valid;
        }

        private void ApplyTranslation_Click(object sender, RoutedEventArgs e)
        {
            if (!CommitGridEdits()) { StatusBarText.Text = "Resolve invalid grid values first"; return; }
            if (!int.TryParse(OffsetXText.Text, out int dx) ||
                !int.TryParse(OffsetYText.Text, out int dy) ||
                !int.TryParse(OffsetZText.Text, out int dz))
            {
                MessageBox.Show("Please enter valid integers for translation offsets.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            int target = BulkTargetComboBox.SelectedIndex;
            if (!TransformFits(target, 1, 1, 1, dx, dy, dz)) return;

            if (target == 0) // Collision
            {
                Services.Levels.MeshTransforms.Translate(ColVertices, dx, dy, dz);
                ColVertexGrid.Items.Refresh();
                StatusBarText.Text = $"Translated collision mesh by ({dx}, {dy}, {dz}).";
            }
            else if (target == 1) // Visual Active
            {
                Services.Levels.MeshTransforms.Translate(VisualVertices, dx, dy, dz);
                VisualVertexGrid.Items.Refresh();
                StatusBarText.Text = $"Translated active visual vertex array by ({dx}, {dy}, {dz}).";
            }
            else if (target == 2) // Visual All Arrays
            {
                foreach (var key in _currentModelVertexArrays.Keys)
                {
                    var vertices = _currentModelVertexArrays[key];
                    Services.Levels.MeshTransforms.Translate(vertices, dx, dy, dz);
                }
                LoadSelectedVertexArray(); // reload display
                StatusBarText.Text = $"Translated all visual vertex arrays by ({dx}, {dy}, {dz}).";
            }
        }

        private void ApplyScale_Click(object sender, RoutedEventArgs e)
        {
            if (!CommitGridEdits()) { StatusBarText.Text = "Resolve invalid grid values first"; return; }
            if (!double.TryParse(ScaleXText.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double sx) ||
                !double.TryParse(ScaleYText.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double sy) ||
                !double.TryParse(ScaleZText.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double sz) || !double.IsFinite(sx) || !double.IsFinite(sy) || !double.IsFinite(sz) || Math.Abs(sx) > 1000 || Math.Abs(sy) > 1000 || Math.Abs(sz) > 1000)
            {
                MessageBox.Show("Please enter valid decimal factors for scale.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            int target = BulkTargetComboBox.SelectedIndex;
            if (!TransformFits(target, sx, sy, sz)) return;

            if (target == 0) // Collision
            {
                Services.Levels.MeshTransforms.Scale(ColVertices, sx, sy, sz);
                ColVertexGrid.Items.Refresh();
                StatusBarText.Text = $"Scaled collision mesh by ({sx}, {sy}, {sz}).";
            }
            else if (target == 1) // Visual Active
            {
                Services.Levels.MeshTransforms.Scale(VisualVertices, sx, sy, sz);
                VisualVertexGrid.Items.Refresh();
                StatusBarText.Text = $"Scaled active visual vertex array by ({sx}, {sy}, {sz}).";
            }
            else if (target == 2) // Visual All Arrays
            {
                foreach (var key in _currentModelVertexArrays.Keys)
                {
                    var vertices = _currentModelVertexArrays[key];
                    Services.Levels.MeshTransforms.Scale(vertices, sx, sy, sz);
                }
                LoadSelectedVertexArray(); // reload display
                StatusBarText.Text = $"Scaled all visual vertex arrays by ({sx}, {sy}, {sz}).";
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (!CommitGridEdits()) { StatusBarText.Text = "Resolve invalid grid values before saving"; return; }
            if (AreaComboBox.SelectedItem is not string selectedArea) return;
            if (ColTriangles.Any(t => Math.Min(t.V1,Math.Min(t.V2,t.V3)) < 0 || Math.Max(t.V1,Math.Max(t.V2,t.V3)) >= ColVertices.Count))
            {
                MessageBox.Show("A collision triangle refers to a vertex that does not exist.","Save mesh");
                return;
            }

            try
            {
                var staged=new Dictionary<string,byte[]>(StringComparer.OrdinalIgnoreCase);
                var originals=new Dictionary<string,byte[]?>(StringComparer.OrdinalIgnoreCase);
                void Remember(string path) { if(!originals.ContainsKey(path)) originals[path]=loadedSourceVersions.TryGetValue(path,out var loaded) ? loaded : File.Exists(path) ? File.ReadAllBytes(path) : null; }
                if (_currentColMesh != null && _collisionFiles.TryGetValue(selectedArea, out string? colPath))
                {
                    Remember(colPath);
                    foreach(string name in new[]{"room.inc.c","rooms.inc.c"})
                    { string room=Path.Combine(Path.GetDirectoryName(colPath)!,name); if(File.Exists(room)) Remember(room); }
                    _currentColMesh.Vertices=ColVertices.Select(v=>new CollisionVertex(v.X,v.Y,v.Z)).ToList();
                    _currentColMesh.Triangles=ColTriangles.Select(t=>new CollisionTriangle(t.V1,t.V2,t.V3,t.SurfaceType,t.SpecialParam)).ToList();
                    _currentColMesh.WaterBoxes=WaterBoxes.Select(w=>new WaterBox(w.Id,w.X1,w.Z1,w.X2,w.Z2,w.Y)).ToList();
                    foreach(var item in CollisionSourceWriter.Stage(colPath,_currentColMesh)) { Remember(item.Key); staged[item.Key]=item.Value; }
                }
                if(VisualArrayComboBox.SelectedItem is string arrayName && VisualModelComboBox.SelectedItem is string modelFileName && _modelFiles.TryGetValue(selectedArea,out var modelPaths))
                {
                    string modelPath=modelPaths.FirstOrDefault(p=>Path.GetRelativePath(_levelPath,p)==modelFileName) ?? throw new InvalidDataException("Visual source not found.");
                    Remember(modelPath); string content=File.ReadAllText(modelPath);
                    var activeList=VisualVertices.Select(v=>new ModelVertex(v.X,v.Y,v.Z,v.S,v.T,v.NX,v.NY,v.NZ,v.Alpha)).ToList();
                    _currentModelVertexArrays[arrayName]=activeList;
                    if(BulkTargetComboBox.SelectedIndex==2)
                        foreach(var array in _currentModelVertexArrays) content=LevelMeshService.StageVisualMesh(content,array.Key,array.Value);
                    else content=LevelMeshService.StageVisualMesh(content,arrayName,activeList);
                    staged[modelPath]=System.Text.Encoding.UTF8.GetBytes(content);
                }
                SourceFileTransaction.Commit(staged,originals.Where(p=>staged.ContainsKey(p.Key)).ToDictionary(p=>p.Key,p=>p.Value));
                foreach(var item in staged) loadedSourceVersions[item.Key]=item.Value;
                StatusBarText.Text="Geometry, collision and rooms saved; preview refreshed.";
                _reloadCallback?.Invoke();
            }
            catch(Exception ex) { StatusBarText.Text = "Mesh was not saved: " + ex.Message; MessageBox.Show("Mesh was not saved: "+ex.Message,"Save mesh",MessageBoxButton.OK,MessageBoxImage.Error); }
        }
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            if (EmbeddedCloseRequested != null) EmbeddedCloseRequested.Invoke(); else Close();
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
        public void SelectSourceVertex(string arrayName, int index)
        {
            if (AreaComboBox.SelectedItem is not string area || !_modelFiles.TryGetValue(area,out var files)) return;
            foreach (string file in files)
            {
                if (!System.Text.RegularExpressions.Regex.IsMatch(File.ReadAllText(file),@"\bVtx\s+"+System.Text.RegularExpressions.Regex.Escape(arrayName)+@"\s*\[")) continue;
                VisualModelComboBox.SelectedItem = Path.GetRelativePath(_levelPath,file);
                LoadSelectedModelFile();
                VisualArrayComboBox.SelectedItem = arrayName;
                SelectVisualVertex(index);
                return;
            }
        }
        public class ColVertexViewModel : Models.Levels.ColVertexViewModel { }
        public class ColTriangleViewModel : Models.Levels.ColTriangleViewModel { }
        public class WaterBoxViewModel : Models.Levels.WaterBoxViewModel { }
        public class ModelVertexViewModel : Models.Levels.ModelVertexViewModel { }
        private (EditableGeometry Terrain, CollisionMesh Ground) PlacementContext(string area, string? excluded = null) => Services.Levels.MeshPlacementContext.Load(_projectRoot, _levelPath, _collisionFiles, _collisionParser, area, excluded);
    }
}
