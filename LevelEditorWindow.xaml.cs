using Sm64DecompLevelViewer.Services.Levels;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Sm64DecompLevelViewer.Models;
using Sm64DecompLevelViewer.Services;
using OpenTK.Mathematics;
using OpenTK.Windowing.Desktop;
using Sm64DecompLevelViewer.Rendering;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using System.Windows.Media;

namespace Sm64DecompLevelViewer
{
    public partial class LevelEditorWindow : System.Windows.Window
    {
        private List<LevelObject> _objects;
        private volatile bool _isClosing;
        private GeometryRenderer _renderer;
        private LevelSaver _levelSaver = new LevelSaver();
        private LevelObject _selectedObject;
        private bool _isUpdatingFromCode = false;
        private bool _updatingViewSettings;
        private IntPtr _rendererHwnd = IntPtr.Zero;
        private BehaviorService _behaviorService;
        private string _projectRoot;
        private int _areaIndex;
        private List<string> _supportedModels;
        private readonly LevelObjectEditSession _objectEdits = new();
        private LevelObject? _clipboard { get => _objectEdits.Clipboard; set => _objectEdits.Clipboard = value; }
        private AppSettings _settings;
        private CollisionMesh? _collisionMesh;
        private string _collisionFilePath;
        private readonly Task<LevelObjectModelRules> _modelRulesTask;
        private LevelObjectModelRules? _modelRules;

        public event EventHandler? RequestReload;
        public event EventHandler? GeometryChanged;

        [DllImport("user32.dll")]
        static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        [DllImport("user32.dll")]
        static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

        [DllImport("user32.dll")]
        static extern IntPtr SetFocus(IntPtr hWnd);

        private const int GWL_STYLE = -16;
        private const int WS_CHILD = 0x40000000;
        private const int WS_VISIBLE = 0x10000000;

        public LevelEditorWindow(List<LevelObject> objects, CollisionMesh collisionMesh, VisualMesh visualMesh, string projectRoot, int areaIndex, List<string> supportedModels, AppSettings settings, string collisionFilePath)
        {
            InitializeComponent();
            _objects = objects;
            _liveOriginals = KernelLiveBridge.Capture(objects);
            _levelSaver.CaptureSources(objects);
            _areaIndex = areaIndex;
            _supportedModels = supportedModels;
            _settings = settings;
            _collisionMesh = collisionMesh;
            _visualScene = visualMesh;
            _collisionFilePath = collisionFilePath;
            
            _projectRoot = projectRoot;
            _modelRulesTask = Task.Run(() => new LevelObjectModelRules(projectRoot, supportedModels, visualMesh?.LevelDirectory));
            Loaded += async (_, _) =>
            {
                try
                {
                    _modelRules = await _modelRulesTask;
                    if (!_isClosing && _selectedObject != null) UpdatePropertyGrid(_selectedObject);
                }
                catch (Exception ex) { SelectModelButton.ToolTip = "Model compatibility could not be loaded: " + ex.Message; }
            };
            SceneInfoText.Text = $"Area {areaIndex}  ·  {visualMesh?.TriangleCount ?? 0:N0} triangles  ·  {objects.Count:N0} objects";
            SkyboxViewCheckBox.IsEnabled = visualMesh?.SkyboxBin != null;
            _behaviorService = new BehaviorService(projectRoot);
            
            PopulateTreeView();
            InitializeMeshEditor();
            InitializeIntroActors();
            
            this.SizeChanged += (s, e) => UpdateRendererPosition();
            RendererContainer.SizeChanged += (s, e) => UpdateRendererPosition();

            // Start the renderer
            StartRenderer(collisionMesh, visualMesh);

            // Forward keys to renderer if not typing in a textbox
            this.PreviewKeyDown += (s, e) => {
                if(e.Key==System.Windows.Input.Key.Escape && _embeddedMeshEditor?.HasPlacementPreview==true)
                {
                    _embeddedMeshEditor.ResumePlacement();
                    e.Handled=true;
                    return;
                }
                if (!IsMeshMode && SidebarTabControl.SelectedIndex == 0 && e.Key == System.Windows.Input.Key.Delete && !(System.Windows.Input.Keyboard.FocusedElement is TextBox))
                {
                    RemoveObject_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    return;
                }
                // 1. Copy (F1)
                if (!IsMeshMode && e.Key == System.Windows.Input.Key.F1)
                {
                    PerformCopy();
                    e.Handled = true;
                }
                // 2. Paste (F2)
                else if (!IsMeshMode && e.Key == System.Windows.Input.Key.F2)
                {
                    PerformPaste();
                    e.Handled = true;
                }
                // 3. Mesh/Geometry Keyboard Transformation (Arrow Keys / PageUp / PageDown)
                else if (!IsMeshMode && SidebarTabControl.SelectedIndex == 2 && MeshItemListBox.SelectedItem is MeshItemViewModel selectedVM && !(System.Windows.Input.Keyboard.FocusedElement is TextBox))
                {
                    bool isArrowOrPage = e.Key == System.Windows.Input.Key.Up ||
                                         e.Key == System.Windows.Input.Key.Down ||
                                         e.Key == System.Windows.Input.Key.Left ||
                                         e.Key == System.Windows.Input.Key.Right ||
                                         e.Key == System.Windows.Input.Key.PageUp ||
                                         e.Key == System.Windows.Input.Key.PageDown;

                    if (isArrowOrPage)
                    {
                        int delta = 50; // Move by 50 units
                        if (System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Shift))
                        {
                            delta = 10; // Move by 10 units if Shift is held
                        }

                        int dx = 0, dy = 0, dz = 0;

                        if (e.Key == System.Windows.Input.Key.Left) dx = -delta;
                        else if (e.Key == System.Windows.Input.Key.Right) dx = delta;
                        else if (e.Key == System.Windows.Input.Key.Up) dz = -delta;
                        else if (e.Key == System.Windows.Input.Key.Down) dz = delta;
                        else if (e.Key == System.Windows.Input.Key.PageUp) dy = delta;
                        else if (e.Key == System.Windows.Input.Key.PageDown) dy = -delta;

                        bool modified = false;

                        if (selectedVM.Target is CollisionVertex v)
                        {
                            v.X += dx;
                            v.Y += dy;
                            v.Z += dz;
                            modified = true;
                            selectedVM.DisplayText = $"V{_collisionMesh.Vertices.IndexOf(v)}: ({v.X}, {v.Y}, {v.Z})";
                        }
                        else if (selectedVM.Target is CollisionTriangle t)
                        {
                            if (_collisionMesh != null &&
                                t.V1 < _collisionMesh.Vertices.Count &&
                                t.V2 < _collisionMesh.Vertices.Count &&
                                t.V3 < _collisionMesh.Vertices.Count)
                            {
                                var v1 = _collisionMesh.Vertices[t.V1];
                                var v2 = _collisionMesh.Vertices[t.V2];
                                var v3 = _collisionMesh.Vertices[t.V3];
                                v1.X += dx; v1.Y += dy; v1.Z += dz;
                                v2.X += dx; v2.Y += dy; v2.Z += dz;
                                v3.X += dx; v3.Y += dy; v3.Z += dz;
                                modified = true;
                                selectedVM.DisplayText = $"T{_collisionMesh.Triangles.IndexOf(t)}: V({t.V1},{t.V2},{t.V3}) {t.SurfaceType}";
                            }
                        }
                        else if (selectedVM.Target is WaterBox wb)
                        {
                            wb.X1 += dx;
                            wb.X2 += dx;
                            wb.Z1 += dz;
                            wb.Z2 += dz;
                            wb.Y += dy;
                            modified = true;
                            selectedVM.DisplayText = $"W{wb.Id}: X({wb.X1}..{wb.X2}) Z({wb.Z1}..{wb.Z2}) Y={wb.Y}";
                        }

                        if (modified)
                        {
                            // Refresh listbox item
                            var index = MeshItemListBox.SelectedIndex;
                            MeshItemListBox.Items.Refresh();
                            MeshItemListBox.SelectedIndex = index;

                            // Update textboxes in UI
                            UpdateMeshEditorFieldsVisibility();

                            // Force the renderer to redraw and reposition
                            if (_renderer != null)
                            {
                                _renderer.LoadMeshThreadSafe(_collisionMesh);
                                
                                OpenTK.Mathematics.Vector3? point = null;
                                if (selectedVM.Target is CollisionVertex vert)
                                {
                                    point = new OpenTK.Mathematics.Vector3((float)vert.X, (float)vert.Y, (float)vert.Z);
                                }
                                else if (selectedVM.Target is CollisionTriangle tri)
                                {
                                    var v1 = _collisionMesh.Vertices[tri.V1];
                                    var v2 = _collisionMesh.Vertices[tri.V2];
                                    var v3 = _collisionMesh.Vertices[tri.V3];
                                    point = new OpenTK.Mathematics.Vector3(
                                        (float)((v1.X + v2.X + v3.X) / 3.0),
                                        (float)((v1.Y + v2.Y + v3.Y) / 3.0),
                                        (float)((v1.Z + v2.Z + v3.Z) / 3.0)
                                    );
                                }
                                else if (selectedVM.Target is WaterBox wbox)
                                {
                                    point = new OpenTK.Mathematics.Vector3(
                                        (float)((wbox.X1 + wbox.X2) / 2.0),
                                        (float)wbox.Y,
                                        (float)((wbox.Z1 + wbox.Z2) / 2.0)
                                    );
                                }
                                if (point.HasValue)
                                {
                                    _renderer.SelectedMeshPoint = point;
                                }
                            }

                            e.Handled = true;
                        }
                    }
                }
                // 4. Fallback: focus renderer
                else if (!(System.Windows.Input.Keyboard.FocusedElement is TextBox) && _rendererHwnd != IntPtr.Zero)
                {
                    SetFocus(_rendererHwnd);
                }
            };
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var helper = new WindowInteropHelper(this);
            int style = GetWindowLong(helper.Handle, GWL_STYLE_WINDOW);
            SetWindowLong(helper.Handle, GWL_STYLE_WINDOW, style | WS_CLIPCHILDREN);
        }

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        private const int GWL_STYLE_WINDOW = -16;
        private const int WS_CLIPCHILDREN = 0x02000000;

        private void StartRenderer(CollisionMesh colMesh, VisualMesh visMesh)
        {
            var nativeWindowSettings = new NativeWindowSettings()
            {
                Size = new Vector2i(800, 600),
                Title = "Embedded 3D Viewer",
                Flags = OpenTK.Windowing.Common.ContextFlags.Default,
                Profile = OpenTK.Windowing.Common.ContextProfile.Core,
            };

            var gameWindowSettings = new GameWindowSettings()
            {
                UpdateFrequency = 60.0
            };

            // Using the existing renderer thread strategy to keep WPF responsive
            var rendererThread = new System.Threading.Thread(() =>
            {
                using var renderer = new GeometryRenderer(gameWindowSettings, nativeWindowSettings, _behaviorService.ProjectRoot);
                _renderer = renderer;
                if (_isClosing) { _renderer = null; return; }
                renderer.CameraMoveSpeed = _settings.CameraMoveSpeed;
                renderer.LevelNavigationEnabled = true;
                renderer.CameraRotationSensitivity = _settings.CameraRotationSpeed;
                renderer.LoadMesh(colMesh);
                if (visMesh != null) 
                {
                    renderer.SetSkyboxBackground(visMesh.SkyboxBin);
                    renderer.LoadVisualMesh(visMesh);
                }
                renderer.SetObjects(_objects);
                renderer.ShowCollision = false;
                renderer.ShowObjectMarkers = false;
                renderer.LevelActorsEnabled = true;
                renderer.FrameLevel();
                renderer.ViewSettingsChanged += () => Dispatcher.BeginInvoke(new Action(() =>
                {
                    _updatingViewSettings = true;
                    TerrainViewCheckBox.IsChecked = renderer.ShowVisual;
                    CollisionViewCheckBox.IsChecked = renderer.ShowCollision;
                    _updatingViewSettings = false;
                }));
                
                renderer.ObjectSelected += (index) => {
                    Dispatcher.Invoke(() => SelectObjectInUI(index));
                };
                renderer.TerrainPointPicked += point => Dispatcher.BeginInvoke(new Action(()=>_embeddedMeshEditor?.PlaceOnTerrain(point)));
                renderer.TerrainPickCanceled += () => Dispatcher.BeginInvoke(new Action(()=>_embeddedMeshEditor?.ResumePlacement()));

                renderer.CopyRequested += () => {
                    Dispatcher.Invoke(() => PerformCopy());
                };

                renderer.PasteRequested += () => {
                    Dispatcher.Invoke(() => PerformPaste());
                };
                renderer.RemoveObjectRequested += () => Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!IsMeshMode && SidebarTabControl.SelectedIndex == 0)
                        RemoveObject_Click(this, new RoutedEventArgs());
                }));

                renderer.SplinePointModified += Renderer_SplinePointModified;

                renderer.CollisionTriangleSelected += index => {
                    Dispatcher.Invoke(() => { SelectCollisionTriangleInUI(index); _embeddedMeshEditor?.SelectCollisionTriangle(index); });
                };
                renderer.MeshToolStatus += ReportMeshStatus;

                renderer.VisualVertexSelected += (index) => {
                    Dispatcher.Invoke(() => SelectVisualVertexInUI(index));
                };

                // Capture HWND and embed
                unsafe {
                    _rendererHwnd = GLFW.GetWin32Window(renderer.WindowPtr);
                }
                
                Dispatcher.Invoke(() => {
                    IntPtr parentHwnd = new WindowInteropHelper(this).Handle;
                    // Note: Ideally we'd get the HWND of the specific Border container, 
                    // but for simplicity in WPF without a HwndHost, we'll parent to the window
                    // and use MoveWindow to position it over our placeholder.
                    SetParent(_rendererHwnd, parentHwnd);
                    SetWindowLong(_rendererHwnd, GWL_STYLE, WS_CHILD | WS_VISIBLE);
                    UpdateRendererPosition();
                    ViewSettings_Changed(this, new RoutedEventArgs());
                    RefreshIntroActors();
                    MeshTool_Changed(this, new RoutedEventArgs());
                });

                renderer.Run();
                if (ReferenceEquals(_renderer, renderer)) _renderer = null;
            });

            rendererThread.SetApartmentState(System.Threading.ApartmentState.STA);
            rendererThread.IsBackground = true;
            rendererThread.Name = "Level viewport";
            rendererThread.Start();
        }

        private void ViewSettings_Changed(object sender, RoutedEventArgs e)
        {
            if (_renderer == null || _updatingViewSettings) return;
            _renderer.ShowVisual = TerrainViewCheckBox.IsChecked == true;
            _renderer.SkyboxEnabled = SkyboxViewCheckBox.IsChecked == true;
            _renderer.WaterEnabled = WaterViewCheckBox.IsChecked == true;
            _renderer.LevelActorsEnabled = ActorsViewCheckBox.IsChecked == true;
            _renderer.AnimateLevelActors = AnimateActorsCheckBox.IsChecked == true;
            _renderer.ShowCollision = CollisionViewCheckBox.IsChecked == true;
            _renderer.ShowObjectMarkers = MarkersViewCheckBox.IsChecked == true;
        }

        private void FrameLevel_Click(object sender, RoutedEventArgs e) => _renderer?.FrameLevel();
        private void FocusSelected_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedObject != null) _renderer?.FocusOnPoint(new Vector3(_selectedObject.X, _selectedObject.Y, _selectedObject.Z));
        }

        private Stack<LevelObject> _removedObjects => _objectEdits.Removed;

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (IsMeshMode) { await SaveMeshSessionAsync(); return; }
            try { _modelRules ??= await _modelRulesTask; }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Model Compatibility Could Not Be Checked"); return; }
            if (SaveObjectChanges(out string? saveError))
            {
                MessageBox.Show("Level saved successfully!", "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                string logPath = LogService.GetLogPath();
                MessageBox.Show($"Level was not saved: {saveError}\n\nDetailed errors:\n{logPath}",
                    "Save Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        private bool SaveObjectChanges(out string? error)
        {
            try
            {
                _modelRules ??= _modelRulesTask.GetAwaiter().GetResult();
                var animations = CustomObjectAnimations.Prepare(_projectRoot, _visualScene?.LevelDirectory, _objects);
                bool saved = _levelSaver.SaveLevel(_objects, _collisionFilePath,
                    obj => _modelRules.PairRejection(obj.Behavior, obj.ModelName), animations);
                error = _levelSaver.LastError;
                if (saved && _selectedObject != null) UpdatePropertyGrid(_selectedObject);
                return saved;
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        private void OpenScanDebugger_Click(object sender, RoutedEventArgs e)
        {
            var debuggerWindow = new ScanDebuggerWindow();
            debuggerWindow.Owner = this;
            debuggerWindow.ShowDialog();
        }

        private void TestButton_Click(object sender, RoutedEventArgs e)
        {
            // 1. Auto-save first
            if(!SaveObjectChanges(out string? saveError))
            {
                MessageBox.Show($"Build canceled because saving failed:\n{saveError}","Save failed",MessageBoxButton.OK,MessageBoxImage.Error);
                return;
            }

            // 2. Launch Build Options Window
            var optionsWindow = new BuildOptionsWindow(_projectRoot);
            optionsWindow.Owner = this;
            if (optionsWindow.ShowDialog() == true)
            {
                if (optionsWindow.IsCleanAndClone)
                {
                    var cleanWindow = new BuildOutputWindow(_projectRoot, optionsWindow.GitUrlToClone);
                    cleanWindow.Owner = this;
                    cleanWindow.ShowDialog();

                    if (cleanWindow.IsSuccessful)
                    {
                        RequestReload?.Invoke(this, EventArgs.Empty);
                    }
                }
                else if (optionsWindow.IsRevertSource)
                {
                    var revertSelectWindow = new RevertChangesWindow(_projectRoot) { Owner = this };
                    if (revertSelectWindow.ShowDialog() == true)
                    {
                        var revertWindow = new BuildOutputWindow(_projectRoot, revertSelectWindow.SelectedBackupFiles, revertSelectWindow.RunGitRevert);
                        revertWindow.Owner = this;
                        revertWindow.ShowDialog();

                        if (revertWindow.IsSuccessful)
                        {
                            RequestReload?.Invoke(this, EventArgs.Empty);
                        }
                    }
                }
                else
                {
                    var buildWindow = new BuildOutputWindow(_projectRoot, optionsWindow.SelectedSettings);
                    buildWindow.Owner = this;
                    buildWindow.ShowDialog();
                }
            }
        }

        private void RendererContainer_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_rendererHwnd != IntPtr.Zero)
            {
                SetFocus(_rendererHwnd);
                // Also focus the border itself to help WPF routing
                RendererContainer.Focus();
                Console.WriteLine("Redirected focus to 3D Viewer");
            }
        }

        private void HelpButton_Click(object sender, RoutedEventArgs e)
        {
            var controls = new LevelEditorControlsWindow { Owner = this };
            controls.ShowDialog();
        }

        private void ToolsDashboardButton_Click(object sender, RoutedEventArgs e)
        {
            string? macroFile = _objects.FirstOrDefault(o => !string.IsNullOrEmpty(o.SourceFile))?.SourceFile;
            string? levelPath = string.IsNullOrEmpty(macroFile) ? null : Path.GetDirectoryName(macroFile);

            var tools = new ToolsWindow(_projectRoot, levelPath, () =>
            {
                RequestReload?.Invoke(this, EventArgs.Empty);
            });
            tools.Owner = this;
            tools.Show();
        }

        private void ActorEditorButton_Click(object sender, RoutedEventArgs e)
        {
            var actorEditor = new ActorEditorWindow(_projectRoot);
            actorEditor.Owner = this;
            actorEditor.ShowDialog();
        }

        private void MusicEditorButton_Click(object sender, RoutedEventArgs e)
        {
            var musicEditor = new MusicEditorWindow(_projectRoot);
            musicEditor.Owner = this;
            musicEditor.Show();
        }

        private LevelMeshEditorWindow? _embeddedMeshEditor;
        private VisualMesh? _visualScene;
        private void LevelMeshEditorButton_Click(object sender, RoutedEventArgs e)
        {
            if (_meshSession?.IsDirty == true) { MeshSelectionText.Text = "Save mesh before opening the mesh viewer"; return; }
            EnsureMeshViewer();
            if (_embeddedMeshEditor == null) return;
            if (_renderer != null) _renderer.MeshSessionBusy = true;
            MeshDockRow.Height = new GridLength(1, GridUnitType.Star);
            NavigationControls.Visibility = Visibility.Collapsed;
            UpdateRendererPosition();
        }
        private void EnsureMeshViewer()
        {
            if (_visualScene?.LevelDirectory == null) return;
            if (_embeddedMeshEditor == null)
            {
                _embeddedMeshEditor = new LevelMeshEditorWindow(_visualScene.LevelDirectory, $"Area {_areaIndex}", _projectRoot,
                    ReloadEmbeddedGeometry, (point, focus) =>
                    {
                        if (_renderer == null) return;
                        _renderer.SelectedMeshPoint = point;
                        if (point.HasValue && focus) _renderer.FocusOnPoint(point.Value);
                    }, () => _renderer?.GetCameraTarget() ?? OpenTK.Mathematics.Vector3.Zero);
                MeshDock.Child = _embeddedMeshEditor.CreateEmbeddedView();
                _embeddedMeshEditor.PlacementPickingChanged+=(enabled,excluded)=>
                {
                    if(_renderer==null) return;
                    _renderer.TerrainPlacementExcludedModel=excluded;
                    _renderer.TerrainPlacementEnabled=enabled;
                    SceneInfoText.Text=enabled ? "Model placement: Shift-click terrain · Escape returns to preview" :
                        $"Area {_areaIndex}  ·  {_visualScene.TriangleCount:N0} triangles  ·  {_objects.Count:N0} objects";
                };
                _embeddedMeshEditor.EmbeddedCloseRequested += () => {
                    MeshDockRow.Height = new GridLength(0);
                    if (_renderer != null) _renderer.MeshSessionBusy = false;
                    NavigationControls.Visibility = IsMeshMode ? Visibility.Collapsed : Visibility.Visible;
                    UpdateRendererPosition();
                };
            }
        }
        private void ReloadEmbeddedGeometry()
        {
            if (_visualScene?.LevelDirectory == null) return;
            string directory = _visualScene.LevelDirectory;
            string yaml = System.IO.File.ReadAllText(System.IO.Path.Combine(directory,"level.yaml"));
            string texture = System.Text.RegularExpressions.Regex.Match(yaml,@"texture-bin:\s*(\w+)").Groups[1].Value;
            var scene = LevelSceneLoader.Load(_projectRoot,directory,_areaIndex,texture);
            var collision = new CollisionParser().ParseCollisionFile(_collisionFilePath,$"Area {_areaIndex}",scene.LevelName);
            _visualScene = scene;
            _meshSession = null;
            _meshPreparation = null;
            _renderer?.InstallMeshSession(null);
            _collisionMesh = collision;
            _renderer?.LoadVisualMeshThreadSafe(_visualScene);
            if (_collisionMesh != null) _renderer?.LoadMeshThreadSafe(_collisionMesh);
            RefreshMeshList();
            SceneInfoText.Text = $"Area {_areaIndex}  ·  {_visualScene.TriangleCount:N0} triangles  ·  {_objects.Count:N0} objects";
            // The embedded editor already refreshed this window. RequestReload closes it in MainWindow.
            GeometryChanged?.Invoke(this,EventArgs.Empty);
            if (IsMeshMode) _ = PrepareMeshToolsAsync();
        }
        private void WarpEditorButton_Click(object sender, RoutedEventArgs e)
        {
            string? levelPath = _visualScene?.LevelDirectory;
            if (levelPath == null || !File.Exists(Path.Combine(levelPath, "script.c")))
            {
                MessageBox.Show("The active level script is unavailable", "Warps");
                return;
            }
            var warpEditor = new WarpEditorWindow(levelPath, () => RequestReload?.Invoke(this, EventArgs.Empty), _areaIndex.ToString());
            warpEditor.Owner = this;
            warpEditor.Show();
        }

        private void PaintingEditorButton_Click(object sender, RoutedEventArgs e)
        {
            string? macroFile = _objects.FirstOrDefault(o => !string.IsNullOrEmpty(o.SourceFile))?.SourceFile;
            if (string.IsNullOrEmpty(macroFile))
            {
                MessageBox.Show("No active level files found to determine the level path.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            string levelPath = Path.GetDirectoryName(macroFile)!;

            var paintingService = new Sm64DecompLevelViewer.Services.PaintingService();
            string? paintingPath = paintingService.FindPaintingFile(levelPath);
            if (string.IsNullOrEmpty(paintingPath) || !File.Exists(paintingPath))
            {
                MessageBox.Show("No painting.inc.c file found in this level's folders.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var paintingEditor = new PaintingEditorWindow(levelPath, () =>
            {
                RequestReload?.Invoke(this, EventArgs.Empty);
            });
            paintingEditor.Owner = this;
            paintingEditor.Show();
        }

        private void PatchButton_Click(object sender, RoutedEventArgs e)
        {
            var patchWindow = new PatchWindow(_projectRoot);
            patchWindow.Owner = this;
            patchWindow.ShowDialog();
        }

        // --- NEW UI HANDLERS ---

        private void GizmoMove_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedObject == null || _renderer == null) return;
            
            var btn = sender as Button;
            string direction = btn?.Content.ToString() ?? "";
            
            int amount = 100;
            if (System.Windows.Input.Keyboard.IsKeyDown(System.Windows.Input.Key.LeftShift)) amount = 10;
            
            switch (direction)
            {
                case "▲": _selectedObject.Z -= amount; break;
                case "▼": _selectedObject.Z += amount; break;
                case "◀": _selectedObject.X -= amount; break;
                case "▶": _selectedObject.X += amount; break;
                case "➕": _selectedObject.Y += amount; break;
                case "➖": _selectedObject.Y -= amount; break;
            }
            
            UpdateUIFromObject(_selectedObject);
            int index = _objects.IndexOf(_selectedObject);
            _renderer.UpdateObject(index, _selectedObject);
        }

        private void MoveSpeedSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_renderer != null)
            {
                _renderer.ObjectMoveSpeed = (float)e.NewValue * 5f; // 100% = 500 units/sec
            }
            if (MoveSpeedText != null)
            {
                MoveSpeedText.Text = $"{(int)e.NewValue}%";
            }
        }

        private void CameraSpeedSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_renderer != null)
            {
                _renderer.CameraMoveSpeed = _settings.CameraMoveSpeed * (float)e.NewValue / 100f;
            }
            if (CameraSpeedText != null)
            {
                CameraSpeedText.Text = $"{(int)e.NewValue}%";
            }
        }

        // Spline editing fields and methods
        private readonly SplineEditSession _splineEdits = new();
        private CutsceneService _cutsceneService => _splineEdits.Service;
        private List<SplinePoint>? _activeSplinePoints { get => _splineEdits.Points; set => _splineEdits.Points = value; }
        private string? _activeSplineName { get => _splineEdits.Name; set => _splineEdits.Name = value; }
        private string _cameraCPath { get => _splineEdits.Path; set => _splineEdits.Path = value; }
        private bool _isUpdatingUi = false;

        private System.Windows.Threading.DispatcherTimer? _previewTimer;
        private System.Diagnostics.Stopwatch? _previewClock;
        private bool _previewPaused;

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
            "SURFACE_BOSS_FIGHT_CAMERA"
        };

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_meshSaving) { e.Cancel = true; return; }
            if (!_meshCloseApproved && _meshSession?.IsDirty == true)
            {
                var answer = MessageBox.Show("Save mesh changes before closing?", "Mesh editor", MessageBoxButton.YesNoCancel);
                if (answer == MessageBoxResult.Cancel) { e.Cancel = true; return; }
                if (answer == MessageBoxResult.Yes) { e.Cancel = true; SaveMeshBeforeClosing(); return; }
            }
            _isClosing = true;
            _previewTimer?.Stop();
            _embeddedMeshEditor?.Close();
            if (_renderer != null)
            {
                try { _renderer.RequestCloseThreadSafe(); } catch { }
                _renderer = null;
            }
        }
        private double? FindFloorY(double x, double currentY, double z) => LevelFloorQuery.FindFloorY(_collisionMesh, x, currentY, z);
        public class MeshItemViewModel : Models.Levels.MeshItemViewModel { }
    }
}

