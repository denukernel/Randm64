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
        private GeometryRenderer _renderer;
        private LevelSaver _levelSaver = new LevelSaver();
        private LevelObject _selectedObject;
        private bool _isUpdatingFromCode = false;
        private IntPtr _rendererHwnd = IntPtr.Zero;
        private BehaviorService _behaviorService;
        private string _projectRoot;
        private int _areaIndex;
        private List<string> _supportedModels;
        private LevelObject? _clipboard;
        private AppSettings _settings;
        private CollisionMesh? _collisionMesh;
        private string _collisionFilePath;

        public event EventHandler? RequestReload;

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
            _areaIndex = areaIndex;
            _supportedModels = supportedModels;
            _settings = settings;
            _collisionMesh = collisionMesh;
            _collisionFilePath = collisionFilePath;
            
            _projectRoot = projectRoot;
            _behaviorService = new BehaviorService(projectRoot);
            
            PopulateTreeView();
            InitializeMeshEditor();
            
            this.SizeChanged += (s, e) => UpdateRendererPosition();

            // Start the renderer
            StartRenderer(collisionMesh, visualMesh);

            // Forward keys to renderer if not typing in a textbox
            this.PreviewKeyDown += (s, e) => {
                // 1. Copy (F1)
                if (e.Key == System.Windows.Input.Key.F1)
                {
                    PerformCopy();
                    e.Handled = true;
                }
                // 2. Paste (F2)
                else if (e.Key == System.Windows.Input.Key.F2)
                {
                    PerformPaste();
                    e.Handled = true;
                }
                // 3. Mesh/Geometry Keyboard Transformation (Arrow Keys / PageUp / PageDown)
                else if (SidebarTabControl.SelectedIndex == 2 && MeshItemListBox.SelectedItem is MeshItemViewModel selectedVM && !(System.Windows.Input.Keyboard.FocusedElement is TextBox))
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

        private void PopulateTreeView()
        {
            NormalObjectsNode.Items.Clear();
            MacroObjectsNode.Items.Clear();
            SpecialObjectsNode.Items.Clear();

            var normalBrush = (SolidColorBrush)new BrushConverter().ConvertFrom("#569CD6");
            var macroBrush = (SolidColorBrush)new BrushConverter().ConvertFrom("#6A9955");
            var specialBrush = (SolidColorBrush)new BrushConverter().ConvertFrom("#DCDCAA");

            for (int i = 0; i < _objects.Count; i++)
            {
                var obj = _objects[i];
                if (obj.IsDeleted) continue;

                var item = new TreeViewItem 
                { 
                    Header = $"[{i}] {obj.ModelName}",
                    Tag = obj
                };

                if (obj.Behavior == "(Special Object)" || obj.SourceType == ObjectSourceType.Special)
                {
                    item.Foreground = specialBrush;
                    SpecialObjectsNode.Items.Add(item);
                }
                else if (obj.SourceType == ObjectSourceType.Macro)
                {
                    item.Foreground = macroBrush;
                    MacroObjectsNode.Items.Add(item);
                }
                else
                {
                    item.Foreground = normalBrush;
                    NormalObjectsNode.Items.Add(item);
                }
            }
        }

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
                _renderer = new GeometryRenderer(gameWindowSettings, nativeWindowSettings, _behaviorService.ProjectRoot);
                _renderer.CameraMoveSpeed = _settings.CameraMoveSpeed;
                _renderer.CameraRotationSensitivity = _settings.CameraRotationSpeed;
                _renderer.LoadMesh(colMesh);
                if (visMesh != null) 
                {
                    _renderer.SetSkyboxBackground(visMesh.SkyboxBin);
                    _renderer.LoadVisualMesh(visMesh);
                }
                _renderer.SetObjects(_objects);
                
                _renderer.ObjectSelected += (index) => {
                    Dispatcher.Invoke(() => SelectObjectInUI(index));
                };

                _renderer.CopyRequested += () => {
                    Dispatcher.Invoke(() => PerformCopy());
                };

                _renderer.PasteRequested += () => {
                    Dispatcher.Invoke(() => PerformPaste());
                };

                _renderer.SplinePointModified += Renderer_SplinePointModified;

                _renderer.CollisionTriangleSelected += (index) => {
                    Dispatcher.Invoke(() => SelectCollisionTriangleInUI(index));
                };

                _renderer.VisualVertexSelected += (index) => {
                    Dispatcher.Invoke(() => SelectVisualVertexInUI(index));
                };

                // Capture HWND and embed
                unsafe {
                    _rendererHwnd = GLFW.GetWin32Window(_renderer.WindowPtr);
                }
                
                Dispatcher.Invoke(() => {
                    IntPtr parentHwnd = new WindowInteropHelper(this).Handle;
                    // Note: Ideally we'd get the HWND of the specific Border container, 
                    // but for simplicity in WPF without a HwndHost, we'll parent to the window
                    // and use MoveWindow to position it over our placeholder.
                    SetParent(_rendererHwnd, parentHwnd);
                    SetWindowLong(_rendererHwnd, GWL_STYLE, WS_CHILD | WS_VISIBLE);
                    UpdateRendererPosition();
                });

                _renderer.Run();
            });

            rendererThread.SetApartmentState(System.Threading.ApartmentState.STA);
            rendererThread.Start();
        }

        private void SelectObjectInUI(int index)
        {
            if (index < 0 || index >= _objects.Count) return;
            
            var obj = _objects[index];
            _selectedObject = obj;
            _isUpdatingFromCode = true;

            // Find in TreeView
            FindAndSelectInTreeView(obj);

            ModelTextBox.Text = obj.ModelName;
            PosXTextBox.Text = obj.X.ToString();
            PosYTextBox.Text = obj.Y.ToString();
            PosZTextBox.Text = obj.Z.ToString();
            RotYTextBox.Text = obj.RY.ToString();
            
            BehaviorTextBox.Text = obj.Behavior;
            AddressTextBox.Text = obj.SourceIndex >= 0 ? $"0x{obj.SourceIndex:X8}" : "N/A";
            ParamsTextBox.Text = obj.Params.ToString("X8");

            NoSelectionText.Visibility = Visibility.Collapsed;
            PropertyStack.Visibility = Visibility.Visible;

            UpdatePropertyGrid(obj);

            _isUpdatingFromCode = false;
        }

        private void UpdatePropertyGrid(LevelObject obj)
        {
            // Future: Dynamically generate or update property grid based on object type
            // For now, the XAML bindings (if any) or manual updates in SelectObjectInUI are enough
        }

        public void PerformCopy(object? sender = null, RoutedEventArgs? e = null)
        {
            if (_selectedObject != null)
            {
                _clipboard = new LevelObject
                {
                    ModelName = _selectedObject.ModelName,
                    Behavior = _selectedObject.Behavior,
                    X = _selectedObject.X,
                    Y = _selectedObject.Y,
                    Z = _selectedObject.Z,
                    RY = _selectedObject.RY,
                    AreaIndex = _selectedObject.AreaIndex,
                    Params = _selectedObject.Params,
                    PresetName = _selectedObject.PresetName,
                    SourceType = _selectedObject.SourceType
                };
                Console.WriteLine($"Copied: {_clipboard.ModelName} (Type: {_clipboard.SourceType})");
            }
        }

        public void PerformPaste(object? sender = null, RoutedEventArgs? e = null)
        {
            if (_clipboard != null)
            {
                var newObj = new LevelObject
                {
                    ModelName = _clipboard.ModelName,
                    Behavior = _clipboard.Behavior,
                    X = _clipboard.X + 250, // Slightly larger offset to make it VERY obvious
                    Y = _clipboard.Y,
                    Z = _clipboard.Z + 250,
                    RY = _clipboard.RY,
                    AreaIndex = _clipboard.AreaIndex,
                    Params = _clipboard.Params,
                    PresetName = _clipboard.PresetName,
                    SourceType = _clipboard.SourceType,
                    IsNew = true
                };

                // Assign source file
                var template = _objects.FirstOrDefault(o => o.SourceType == newObj.SourceType && !string.IsNullOrEmpty(o.SourceFile));
                if (template != null)
                {
                    newObj.SourceFile = template.SourceFile;
                }
                else
                {
                    // Fallback: try to find script.c using robust search
                    var anyTemplate = _objects.FirstOrDefault(o => !string.IsNullOrEmpty(o.SourceFile));
                    if (anyTemplate != null)
                    {
                        string resolvedScript = ResolveScriptPath(anyTemplate.SourceFile);
                        if (!string.IsNullOrEmpty(resolvedScript))
                        {
                            if (newObj.SourceType == ObjectSourceType.Normal) newObj.SourceFile = resolvedScript;
                            else if (newObj.SourceType == ObjectSourceType.Macro) newObj.SourceFile = Path.Combine(Path.GetDirectoryName(resolvedScript)!, "areas", newObj.AreaIndex.ToString(), "macro.inc.c");
                            else if (newObj.SourceType == ObjectSourceType.Special) newObj.SourceFile = Path.Combine(Path.GetDirectoryName(resolvedScript)!, "areas", newObj.AreaIndex.ToString(), "collision.inc.c");
                        }
                    }
                }

                _objects.Add(newObj);
                PopulateTreeView();

                if (_renderer != null)
                {
                    _renderer.SetObjects(_objects); // Refresh all
                    _renderer.SelectObject(_objects.Count - 1);
                }

                SelectObjectInUI(_objects.Count - 1);
                Console.WriteLine($"Pasted: {newObj.ModelName}");
            }
        }

        private void SelectBehaviorButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedObject == null) return;

            var behaviors = _behaviorService.GetBehaviors();
            var dialog = new BehaviorSelectionWindow(behaviors, _selectedObject.Behavior);
            dialog.Owner = this;

            if (dialog.ShowDialog() == true)
            {
                string newBehavior = dialog.SelectedBehavior ?? string.Empty;
                if (newBehavior == _selectedObject.Behavior) return;

                // Handle conversion for Macro and Special objects
                if (_selectedObject.SourceType == ObjectSourceType.Macro || _selectedObject.SourceType == ObjectSourceType.Special)
                {
                    var result = MessageBox.Show(
                        "Converting to Standard Object. This object will be removed from its current file and re-added as a standard OBJECT in script.c to support the new behavior. Proceed?",
                        "Confirm Conversion",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        var oldObj = _selectedObject;
                        
                        // 1. Create the new standard object
                        var newObj = new LevelObject
                        {
                            ModelName = oldObj.ModelName,
                            Behavior = newBehavior,
                            X = oldObj.X,
                            Y = oldObj.Y,
                            Z = oldObj.Z,
                            RY = oldObj.RY,
                            AreaIndex = oldObj.AreaIndex,
                            SourceType = ObjectSourceType.Normal,
                            IsNew = true
                        };

                            string resolvedScript = ResolveScriptPath(oldObj.SourceFile);
                            if (!string.IsNullOrEmpty(resolvedScript))
                            {
                                newObj.SourceFile = resolvedScript;
                            }

                        // 3. Delete the old one
                        oldObj.IsDeleted = true;
                        
                        // 4. Update the session
                        _objects.Add(newObj);
                        PopulateTreeView();
                        
                        if (_renderer != null)
                        {
                            _renderer.SetObjects(_objects); // Refresh all
                            _renderer.SelectObject(_objects.Count - 1);
                        }
                        
                        SelectObjectInUI(_objects.Count - 1);
                        return;
                    }
                    else
                    {
                        // User cancelled conversion, don't change behavior
                        return;
                    }
                }

                // Normal object behavior change
                _selectedObject.Behavior = newBehavior;
                BehaviorTextBox.Text = _selectedObject.Behavior;

                if (_renderer != null)
                {
                    int index = _objects.IndexOf(_selectedObject);
                    _renderer.UpdateObject(index, _selectedObject);
                }
            }
        }

        private string ResolveScriptPath(string? currentFilePath)
        {
            if (string.IsNullOrEmpty(currentFilePath)) return string.Empty;

            try
            {
                string dir = Path.GetDirectoryName(currentFilePath)!;
                
                // 1. Same directory
                string path = Path.Combine(dir, "script.c");
                if (File.Exists(path)) return path;

                // 2. One level up (e.g. levels/castle_grounds/script.c if we are in levels/castle_grounds/areas/1/)
                path = Path.Combine(dir, "..", "script.c");
                if (File.Exists(path)) return Path.GetFullPath(path);

                // 3. Two levels up (Standard decomp: levels/bob/areas/1/macro.inc.c -> levels/bob/script.c)
                path = Path.Combine(dir, "..", "..", "script.c");
                if (File.Exists(path)) return Path.GetFullPath(path);
            }
            catch { }

            return string.Empty;
        }

        private void SelectModelButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedObject == null) return;

            var dialog = new ModelSelectionWindow(_projectRoot, _supportedModels);
            dialog.Owner = this;

            if (dialog.ShowDialog() == true)
            {
                _selectedObject.ModelName = dialog.SelectedModel ?? "MODEL_NONE";
                ModelTextBox.Text = _selectedObject.ModelName;

                if (_renderer != null)
                {
                    int index = _objects.IndexOf(_selectedObject);
                    _renderer.UpdateObject(index, _selectedObject);
                }
            }
        }

        private void RemoveObject_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedObject == null) return;

            // 1. Mario Safety Guard: Mario is essential for level entry.
            if (_selectedObject.SourceType == ObjectSourceType.Mario || _selectedObject.ModelName == "MODEL_MARIO")
            {
                MessageBox.Show("Mario cannot be removed. Every level requires exactly one Mario spawn point to function correctly.", 
                    "Safety Guard: Mario Required", MessageBoxButton.OK, MessageBoxImage.Stop);
                return;
            }

            // 2. Warp Safety Guard: Warps are critical for level transitions.
            bool isWarp = _selectedObject.Behavior.Contains("Warp", System.StringComparison.OrdinalIgnoreCase) || 
                          _selectedObject.Behavior.Contains("Node", System.StringComparison.OrdinalIgnoreCase);

            if (isWarp)
            {
                var result = MessageBox.Show(
                    "WARNING: This object appears to be a Warp or Node.\n" +
                    "Removing essential warps can cause the game to crash, softlock, or break level transitions.\n\n" +
                    "Are you absolutely sure you want to remove it?", 
                    "Critical Safety Warning", 
                    MessageBoxButton.YesNo, 
                    MessageBoxImage.Warning, 
                    MessageBoxResult.No);

                if (result != MessageBoxResult.Yes) return;
            }
            else
            {
                // General confirmation for other objects
                if (MessageBox.Show($"Are you sure you want to remove this object?\n\n{_selectedObject.ModelName}", 
                    "Confirm Removal", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            _selectedObject.IsDeleted = true;
            
            // Hide from renderer
            if (_renderer != null)
            {
                int index = _objects.IndexOf(_selectedObject);
                _renderer.UpdateObject(index, _selectedObject);
            }

            // Update UI
            PopulateTreeView();
            NoSelectionText.Visibility = Visibility.Visible;
            PropertyStack.Visibility = Visibility.Collapsed;
            _selectedObject = null;
        }

        private void AddObject_Click(object sender, RoutedEventArgs e)
        {
            var behaviors = _behaviorService.GetBehaviors();
            var macroPresets = new List<string>();
            var specialPresets = new List<string>();

            try
            {
                // Try to find macro presets
                string projectRoot = _behaviorService.ProjectRoot;
                string macroPath = Path.Combine(projectRoot, "include", "macro_presets.inc.c");
                if (!File.Exists(macroPath)) macroPath = Path.Combine(projectRoot, "howtomake", "include", "macro_presets.inc.c");
                
                if (File.Exists(macroPath))
                {
                    var parser = new MacroObjectParser();
                    macroPresets = parser.ParsePresets(macroPath).Keys.ToList();
                }

                // Try to find special presets
                string specialPath = Path.Combine(projectRoot, "include", "special_presets.inc.c");
                if (!File.Exists(specialPath)) specialPath = Path.Combine(projectRoot, "howtomake", "include", "special_presets.inc.c");
                
                if (File.Exists(specialPath))
                {
                    var parser = new SpecialObjectParser();
                    specialPresets = parser.ParsePresets(specialPath).Keys.ToList();
                }
            }
            catch { }

            int spawnX = 0, spawnY = 0, spawnZ = 0;
            if (_renderer != null)
            {
                var target = _renderer.GetCameraTarget();
                spawnX = (int)target.X;
                spawnY = (int)target.Y;
                spawnZ = (int)target.Z;
            }

            var addWindow = new ObjectAddWindow(_projectRoot, behaviors, macroPresets, specialPresets, _supportedModels, spawnX, spawnY, spawnZ);
            addWindow.Owner = this;
            if (addWindow.ShowDialog() == true)
            {
                var newObj = addWindow.NewObject;
                if (newObj != null)
                {
                    newObj.IsNew = true;
                    
                    // Assign source file
                    var template = _objects.FirstOrDefault(o => o.SourceType == newObj.SourceType && !string.IsNullOrEmpty(o.SourceFile));
                    if (template != null)
                    {
                        newObj.SourceFile = template.SourceFile;
                    }
                    else
                    {
                        // Fallback: try to find script.c using robust search
                        var anyTemplate = _objects.FirstOrDefault(o => !string.IsNullOrEmpty(o.SourceFile));
                        if (anyTemplate != null)
                        {
                            string resolvedScript = ResolveScriptPath(anyTemplate.SourceFile);
                            if (!string.IsNullOrEmpty(resolvedScript))
                            {
                                if (newObj.SourceType == ObjectSourceType.Normal) newObj.SourceFile = resolvedScript;
                                else if (newObj.SourceType == ObjectSourceType.Macro) newObj.SourceFile = Path.Combine(Path.GetDirectoryName(resolvedScript)!, "areas", newObj.AreaIndex.ToString(), "macro.inc.c");
                                else if (newObj.SourceType == ObjectSourceType.Special) newObj.SourceFile = Path.Combine(Path.GetDirectoryName(resolvedScript)!, "areas", newObj.AreaIndex.ToString(), "collision.inc.c");
                            }
                        }
                    }
                    newObj.AreaIndex = _areaIndex;

                    _objects.Add(newObj);
                    PopulateTreeView();
                    
                    if (_renderer != null)
                    {
                        _renderer.SetObjects(_objects); // Refresh all objects in renderer
                        _renderer.SelectObject(_objects.Count - 1);
                    }
                    
                    SelectObjectInUI(_objects.Count - 1);
                }
            }
        }

        private void FindAndSelectInTreeView(LevelObject target)
        {
            foreach (TreeViewItem category in ObjectTreeView.Items)
            {
                foreach (TreeViewItem item in category.Items)
                {
                    if (item.Tag == target)
                    {
                        item.IsSelected = true;
                        item.BringIntoView();
                        return;
                    }
                }
            }
        }

        private void ObjectTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (_isUpdatingFromCode) return;
            
            if (e.NewValue is TreeViewItem item && item.Tag is LevelObject obj)
            {
                int index = _objects.IndexOf(obj);
                if (index != -1 && _renderer != null)
                {
                    _renderer.SelectObject(index);
                }
                
                SelectObjectInUI(index);
            }
        }

        private void Coordinate_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingFromCode || _selectedObject == null) return;

            try
            {
                if (sender == ModelTextBox) _selectedObject.ModelName = ModelTextBox.Text;
                else if (sender == PosXTextBox) _selectedObject.X = int.Parse(PosXTextBox.Text);
                else if (sender == PosYTextBox) _selectedObject.Y = int.Parse(PosYTextBox.Text);
                else if (sender == PosZTextBox) _selectedObject.Z = int.Parse(PosZTextBox.Text);
                else if (sender == RotYTextBox) _selectedObject.RY = int.Parse(RotYTextBox.Text);
                else if (sender == ParamsTextBox) _selectedObject.Params = uint.Parse(ParamsTextBox.Text, System.Globalization.NumberStyles.HexNumber);
                
                if (_renderer != null)
                {
                    int index = _objects.IndexOf(_selectedObject);
                    _renderer.UpdateObject(index, _selectedObject);
                }
            }
            catch { }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (_levelSaver.SaveLevel(_objects))
            {
                MessageBox.Show("Level saved successfully!", "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                string logPath = LogService.GetLogPath();
                MessageBox.Show($"Some files failed to save.\n\nDetailed errors have been written to:\n{logPath}\n\nClick 'View Logs' in the editor to open this file.", 
                    "Save Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void LogsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string path = LogService.GetLogPath();
                if (File.Exists(path))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = path,
                        UseShellExecute = true
                    });
                }
                else
                {
                    MessageBox.Show("Log file not found yet. Nothing has been logged this session.", "Logs", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open logs: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void TestButton_Click(object sender, RoutedEventArgs e)
        {
            // 1. Auto-save first
            _levelSaver.SaveLevel(_objects);

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
                    var revertWindow = new BuildOutputWindow(_projectRoot, isRevertMode: true);
                    revertWindow.Owner = this;
                    revertWindow.ShowDialog();

                    if (revertWindow.IsSuccessful)
                    {
                        RequestReload?.Invoke(this, EventArgs.Empty);
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

        private void UpdateRendererPosition()
        {
            if (_rendererHwnd == IntPtr.Zero) return;

            Dispatcher.BeginInvoke(new Action(() => {
                try {
                    // Get position relative to window
                    Point position = RendererContainer.TranslatePoint(new Point(0, 0), this);
                    MoveWindow(_rendererHwnd, (int)position.X, (int)position.Y, 
                               (int)RendererContainer.ActualWidth, (int)RendererContainer.ActualHeight, true);
                } catch { }
            }), System.Windows.Threading.DispatcherPriority.Render);
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
            string helpText = "3D VIEWER CONTROLS:\n\n" +
                              "Movement: [W][A][S][D]\n" +
                              "Rotate View: [Right-Click + Drag]\n" +
                              "Zoom: [Mouse Wheel]\n" +
                              "Toggle Collision: [C]\n" +
                              "Toggle Visual: [V]\n" +
                              "Hide Sub-Models: [1-9] (0 to reset)\n" +
                              "Reset Camera: [R]\n\n" +
                              "OBJECT MANIPULATION:\n\n" +
                              "Move Object: [Arrow Keys] / [PageUp][PageDown]\n" +
                              "Rotate Object (Y): [Q][E]\n" +
                              "Copy Object: [F1]\n" +
                              "Paste Object: [F2]\n" +
                              "Select: [Left-Click] in 3D or in Sidebar";

            MessageBox.Show(helpText, "Editor Controls", MessageBoxButton.OK, MessageBoxImage.Information);
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

        private void LevelMeshEditorButton_Click(object sender, RoutedEventArgs e)
        {
            string? macroFile = _objects.FirstOrDefault(o => !string.IsNullOrEmpty(o.SourceFile))?.SourceFile;
            if (string.IsNullOrEmpty(macroFile))
            {
                MessageBox.Show("No active level files found to determine the level path.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            string levelPath = Path.GetDirectoryName(macroFile)!;
            string initialArea = $"Area {_areaIndex}";

            var meshEditor = new LevelMeshEditorWindow(
                levelPath, 
                initialArea, 
                _projectRoot, 
                () => {
                    RequestReload?.Invoke(this, EventArgs.Empty);
                },
                (point, focusCamera) => {
                    if (_renderer != null)
                    {
                        _renderer.SelectedMeshPoint = point;
                        if (point.HasValue && focusCamera)
                        {
                            _renderer.FocusOnPoint(point.Value);
                        }
                    }
                }
            );

            Action<int> onCollisionTriangleSelected = (index) => {
                Application.Current.Dispatcher.Invoke(() => {
                    if (meshEditor.IsVisible)
                    {
                        meshEditor.SelectCollisionTriangle(index);
                    }
                });
            };

            Action<int> onVisualVertexSelected = (index) => {
                Application.Current.Dispatcher.Invoke(() => {
                    if (meshEditor.IsVisible)
                    {
                        meshEditor.SelectVisualVertex(index);
                    }
                });
            };

            if (_renderer != null)
            {
                _renderer.CollisionTriangleSelected += onCollisionTriangleSelected;
                _renderer.VisualVertexSelected += onVisualVertexSelected;
            }

            meshEditor.Closed += (s, e) => {
                if (_renderer != null)
                {
                    _renderer.CollisionTriangleSelected -= onCollisionTriangleSelected;
                    _renderer.VisualVertexSelected -= onVisualVertexSelected;
                }
            };

            meshEditor.Owner = this;
            meshEditor.Show();
        }

        private void WarpEditorButton_Click(object sender, RoutedEventArgs e)
        {
            string? macroFile = _objects.FirstOrDefault(o => !string.IsNullOrEmpty(o.SourceFile))?.SourceFile;
            if (string.IsNullOrEmpty(macroFile))
            {
                MessageBox.Show("No active level files found to determine the level path.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            string levelPath = Path.GetDirectoryName(macroFile)!;

            var warpEditor = new WarpEditorWindow(levelPath, () =>
            {
                RequestReload?.Invoke(this, EventArgs.Empty);
            });
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

        private void UpdateUIFromObject(LevelObject obj)
        {
            _isUpdatingFromCode = true;
            PosXTextBox.Text = obj.X.ToString();
            PosYTextBox.Text = obj.Y.ToString();
            PosZTextBox.Text = obj.Z.ToString();
            RotYTextBox.Text = obj.RY.ToString();
            _isUpdatingFromCode = false;
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
                _renderer.CameraMoveSpeed = (float)e.NewValue * 10f; // 100% = 1000 units/sec
            }
            if (CameraSpeedText != null)
            {
                CameraSpeedText.Text = $"{(int)e.NewValue}%";
            }
        }

        private void SearchBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (SearchBox.Text == "Search objects...")
            {
                SearchBox.Text = "";
                SearchBox.Opacity = 1.0;
            }
        }

        private void SearchBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(SearchBox.Text))
            {
                SearchBox.Text = "Search objects...";
                SearchBox.Opacity = 0.5;
                
                // Reset filtering
                FilterTreeView(NormalObjectsNode, "");
                FilterTreeView(MacroObjectsNode, "");
                FilterTreeView(SpecialObjectsNode, "");
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string query = SearchBox.Text.ToLower();
            if (query == "search objects...") return;
            
            FilterTreeView(NormalObjectsNode, query);
            FilterTreeView(MacroObjectsNode, query);
            FilterTreeView(SpecialObjectsNode, query);
        }

        private void FilterTreeView(TreeViewItem category, string query)
        {
            foreach (TreeViewItem item in category.Items)
            {
                bool visible = string.IsNullOrEmpty(query) || item.Header.ToString()!.ToLower().Contains(query);
                item.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void DropToGround_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedObject == null) return;

            if (_collisionMesh == null || _collisionMesh.Triangles.Count == 0)
            {
                MessageBox.Show("No collision mesh loaded for this area.", "Drop to Ground Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            double? floorY = FindFloorY(_selectedObject.X, _selectedObject.Y, _selectedObject.Z);

            if (floorY.HasValue)
            {
                _selectedObject.Y = (int)Math.Round(floorY.Value);
                UpdateUIFromObject(_selectedObject);
                int index = _objects.IndexOf(_selectedObject);
                if (index >= 0 && _renderer != null)
                {
                    _renderer.UpdateObject(index, _selectedObject);
                }
            }
            else
            {
                MessageBox.Show("No floor found directly below or near the object's X/Z position.", "Drop to Ground", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private double? FindFloorY(double x, double currentY, double z)
        {
            if (_collisionMesh == null) return null;

            double? bestFloorY = null;
            double minDistanceBelow = double.MaxValue;

            foreach (var tri in _collisionMesh.Triangles)
            {
                if (tri.V1 >= _collisionMesh.Vertices.Count || 
                    tri.V2 >= _collisionMesh.Vertices.Count || 
                    tri.V3 >= _collisionMesh.Vertices.Count)
                    continue;

                var v0 = _collisionMesh.Vertices[tri.V1];
                var v1 = _collisionMesh.Vertices[tri.V2];
                var v2 = _collisionMesh.Vertices[tri.V3];

                // Calculate triangle normal vector Y component
                // normal = (v1 - v0) x (v2 - v0)
                double edge1X = v1.X - v0.X;
                double edge1Y = v1.Y - v0.Y;
                double edge1Z = v1.Z - v0.Z;

                double edge2X = v2.X - v0.X;
                double edge2Y = v2.Y - v0.Y;
                double edge2Z = v2.Z - v0.Z;

                double normalY = edge1Z * edge2X - edge1X * edge2Z;

                // Floors must face upwards (normalY > 0.01)
                if (normalY <= 0.01)
                    continue;

                // Check if (x, z) is within the projected 2D triangle
                if (!IsPointInTriangle2D(x, z, v0.X, v0.Z, v1.X, v1.Z, v2.X, v2.Z))
                    continue;

                double normalX = edge1Y * edge2Z - edge1Z * edge2Y;
                double normalZ = edge1X * edge2Y - edge1Y * edge2X;

                // Compute Y coordinate on the triangle's plane at (x, z)
                double y = v0.Y - (normalX * (x - v0.X) + normalZ * (z - v0.Z)) / normalY;

                // We want the floor closest to the object, preferably below it.
                // We allow a small tolerance of 50 units for being "at" or slightly below the floor.
                if (y <= currentY + 50)
                {
                    double dist = currentY - y;
                    if (dist < minDistanceBelow)
                    {
                        minDistanceBelow = dist;
                        bestFloorY = y;
                    }
                }
            }

            // Fallback: If no floor is found below the object, check for the closest floor overall (above or below)
            if (!bestFloorY.HasValue)
            {
                double minAbsDistance = double.MaxValue;
                foreach (var tri in _collisionMesh.Triangles)
                {
                    if (tri.V1 >= _collisionMesh.Vertices.Count || 
                        tri.V2 >= _collisionMesh.Vertices.Count || 
                        tri.V3 >= _collisionMesh.Vertices.Count)
                        continue;

                    var v0 = _collisionMesh.Vertices[tri.V1];
                    var v1 = _collisionMesh.Vertices[tri.V2];
                    var v2 = _collisionMesh.Vertices[tri.V3];

                    double edge1X = v1.X - v0.X;
                    double edge1Y = v1.Y - v0.Y;
                    double edge1Z = v1.Z - v0.Z;

                    double edge2X = v2.X - v0.X;
                    double edge2Y = v2.Y - v0.Y;
                    double edge2Z = v2.Z - v0.Z;

                    double normalY = edge1Z * edge2X - edge1X * edge2Z;

                    if (normalY <= 0.01)
                        continue;

                    if (!IsPointInTriangle2D(x, z, v0.X, v0.Z, v1.X, v1.Z, v2.X, v2.Z))
                        continue;

                    double normalX = edge1Y * edge2Z - edge1Z * edge2Y;
                    double normalZ = edge1X * edge2Y - edge1Y * edge2X;

                    double y = v0.Y - (normalX * (x - v0.X) + normalZ * (z - v0.Z)) / normalY;

                    double dist = Math.Abs(currentY - y);
                    if (dist < minAbsDistance)
                    {
                        minAbsDistance = dist;
                        bestFloorY = y;
                    }
                }
            }

            return bestFloorY;
        }

        private bool IsPointInTriangle2D(double px, double pz, double x1, double z1, double x2, double z2, double x3, double z3)
        {
            double d1 = (px - x2) * (z1 - z2) - (x1 - x2) * (pz - z2);
            double d2 = (px - x3) * (z2 - z3) - (x2 - x3) * (pz - z3);
            double d3 = (px - x1) * (z3 - z1) - (x3 - x1) * (pz - z1);

            bool has_neg = (d1 < 0) || (d2 < 0) || (d3 < 0);
            bool has_pos = (d1 > 0) || (d2 > 0) || (d3 > 0);

            return !(has_neg && has_pos);
        }

        private void BehaviorTextBox_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            SelectBehaviorButton_Click(sender, e);
        }

        // Spline editing fields and methods
        private Services.CutsceneService _cutsceneService = new();
        private List<Services.SplinePoint>? _activeSplinePoints;
        private string? _activeSplineName;
        private string _cameraCPath = "";
        private bool _isUpdatingUi = false;

        private void Renderer_SplinePointModified()
        {
            Dispatcher.Invoke(() =>
            {
                if (_activeSplineName != null && _activeSplinePoints != null)
                {
                    _cutsceneService.SaveSpline(_cameraCPath, _activeSplineName, _activeSplinePoints);
                }
                
                _isUpdatingUi = true;
                int selIndex = SplinePointListBox.SelectedIndex;
                SplinePointListBox.Items.Refresh();
                SplinePointListBox.SelectedIndex = selIndex;
                if (selIndex >= 0)
                {
                    var p = _activeSplinePoints[selIndex];
                    SplineSpeedText.Text = p.Speed.ToString();
                    SplineXText.Text = p.X.ToString();
                    SplineYText.Text = p.Y.ToString();
                    SplineZText.Text = p.Z.ToString();
                }
                _isUpdatingUi = false;
            });
        }

        private void SplineSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SplineSelector.SelectedItem is ComboBoxItem item)
            {
                _activeSplineName = item.Tag as string;
                if (_activeSplineName != null)
                {
                    _cameraCPath = Path.Combine(_projectRoot, "src", "game", "camera.c");
                    _activeSplinePoints = _cutsceneService.LoadSpline(_cameraCPath, _activeSplineName);
                    
                    _isUpdatingUi = true;
                    SplinePointListBox.ItemsSource = _activeSplinePoints;
                    SplineEditorFields.Visibility = Visibility.Collapsed;
                    _isUpdatingUi = false;

                    if (_renderer != null)
                    {
                        _renderer.EditSplinePoints = _activeSplinePoints;
                        _renderer.SelectedSplinePointIndex = -1;
                        _renderer.UploadSplineData();
                        _renderer.UploadObjectData();
                    }
                }
            }
        }

        private void SplinePointListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingUi) return;

            int index = SplinePointListBox.SelectedIndex;
            if (_renderer != null)
            {
                _renderer.SelectedSplinePointIndex = index;
                _renderer.UploadObjectData();
            }

            if (index >= 0 && _activeSplinePoints != null)
            {
                _isUpdatingUi = true;
                var p = _activeSplinePoints[index];
                SplineSpeedText.Text = p.Speed.ToString();
                SplineXText.Text = p.X.ToString();
                SplineYText.Text = p.Y.ToString();
                SplineZText.Text = p.Z.ToString();
                SplineEditorFields.Visibility = Visibility.Visible;
                _isUpdatingUi = false;
            }
            else
            {
                SplineEditorFields.Visibility = Visibility.Collapsed;
            }
        }

        private void SplineField_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingUi || _activeSplinePoints == null || _activeSplineName == null) return;

            int index = SplinePointListBox.SelectedIndex;
            if (index < 0 || index >= _activeSplinePoints.Count) return;

            var p = _activeSplinePoints[index];
            
            int.TryParse(SplineSpeedText.Text, out int speed);
            int.TryParse(SplineXText.Text, out int x);
            int.TryParse(SplineYText.Text, out int y);
            int.TryParse(SplineZText.Text, out int z);

            p.Speed = speed;
            p.X = x;
            p.Y = y;
            p.Z = z;

            _cutsceneService.SaveSpline(_cameraCPath, _activeSplineName, _activeSplinePoints);

            if (_renderer != null)
            {
                _renderer.UploadSplineData();
                _renderer.UploadObjectData();
            }
        }

        private void AddSplinePoint_Click(object sender, RoutedEventArgs e)
        {
            if (_activeSplinePoints == null || _activeSplineName == null) return;

            int index = SplinePointListBox.SelectedIndex;
            var newPoint = new Services.SplinePoint { Speed = 50 };
            
            if (index >= 0 && index < _activeSplinePoints.Count)
            {
                var refPoint = _activeSplinePoints[index];
                newPoint.Index = refPoint.Index + 1;
                newPoint.X = refPoint.X + 200;
                newPoint.Y = refPoint.Y;
                newPoint.Z = refPoint.Z + 200;
                _activeSplinePoints.Insert(index + 1, newPoint);
            }
            else
            {
                if (_activeSplinePoints.Count > 0)
                {
                    var refPoint = _activeSplinePoints[_activeSplinePoints.Count - 1];
                    newPoint.Index = refPoint.Index + 1;
                    newPoint.X = refPoint.X + 200;
                    newPoint.Y = refPoint.Y;
                    newPoint.Z = refPoint.Z + 200;
                }
                else
                {
                    newPoint.Index = 0;
                    newPoint.X = 0;
                    newPoint.Y = 1000;
                    newPoint.Z = 0;
                }
                _activeSplinePoints.Add(newPoint);
            }

            _cutsceneService.SaveSpline(_cameraCPath, _activeSplineName, _activeSplinePoints);

            _isUpdatingUi = true;
            SplinePointListBox.ItemsSource = null;
            SplinePointListBox.ItemsSource = _activeSplinePoints;
            _isUpdatingUi = false;

            if (_renderer != null)
            {
                _renderer.UploadSplineData();
                _renderer.UploadObjectData();
            }
        }

        private void RemoveSplinePoint_Click(object sender, RoutedEventArgs e)
        {
            if (_activeSplinePoints == null || _activeSplineName == null) return;

            int index = SplinePointListBox.SelectedIndex;
            if (index >= 0 && index < _activeSplinePoints.Count)
            {
                _activeSplinePoints.RemoveAt(index);

                _cutsceneService.SaveSpline(_cameraCPath, _activeSplineName, _activeSplinePoints);

                _isUpdatingUi = true;
                SplinePointListBox.ItemsSource = null;
                SplinePointListBox.ItemsSource = _activeSplinePoints;
                _isUpdatingUi = false;

                if (_renderer != null)
                {
                    _renderer.SelectedSplinePointIndex = -1;
                    _renderer.UploadSplineData();
                    _renderer.UploadObjectData();
                }
            }
        }

        private void PlayPathPreview_Click(object sender, RoutedEventArgs e)
        {
            if (_activeSplinePoints == null || _activeSplinePoints.Count < 2) return;

            string baseName = _activeSplineName.Replace("Position", "").Replace("Focus", "");
            var posSpline = _cutsceneService.LoadSpline(_cameraCPath, baseName + "Position");
            var focSpline = _cutsceneService.LoadSpline(_cameraCPath, baseName + "Focus");

            if (posSpline.Count < 2 || focSpline.Count < 2) return;

            if (_renderer != null)
            {
                _renderer.IsPlayingPreview = true;
            }

            var timer = new System.Windows.Threading.DispatcherTimer();
            timer.Interval = TimeSpan.FromMilliseconds(33);
            double progress = 0.0;
            double step = 0.005;

            timer.Tick += (s, ev) =>
            {
                progress += step;
                if (progress >= 1.0)
                {
                    timer.Stop();
                    if (_renderer != null)
                    {
                        _renderer.StopPreview();
                    }
                    return;
                }

                OpenTK.Mathematics.Vector3 pos = InterpolateSpline(posSpline, progress);
                OpenTK.Mathematics.Vector3 foc = InterpolateSpline(focSpline, progress);

                if (_renderer != null)
                {
                    _renderer.SetCameraPositionAndTarget(pos, foc);
                }
            };

            timer.Start();
        }

        private OpenTK.Mathematics.Vector3 InterpolateSpline(List<Services.SplinePoint> spline, double t)
        {
            int n = spline.Count;
            double scaledT = t * (n - 1);
            int idx1 = (int)Math.Floor(scaledT);
            int idx2 = (int)Math.Ceiling(scaledT);
            
            if (idx1 < 0) idx1 = 0;
            if (idx2 >= n) idx2 = n - 1;
            if (idx1 == idx2) return new OpenTK.Mathematics.Vector3(spline[idx1].X, spline[idx1].Y, spline[idx1].Z);

            double frac = scaledT - idx1;
            var p1 = spline[idx1];
            var p2 = spline[idx2];

            float x = (float)(p1.X + (p2.X - p1.X) * frac);
            float y = (float)(p1.Y + (p2.Y - p1.Y) * frac);
            float z = (float)(p1.Z + (p2.Z - p1.Z) * frac);

            return new OpenTK.Mathematics.Vector3(x, y, z);
        }

        public class MeshItemViewModel
        {
            public object Target { get; set; }
            public string DisplayText { get; set; }
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
            "SURFACE_BOSS_FIGHT_CAMERA"
        };

        private void InitializeMeshEditor()
        {
            MeshTriSurfaceSelector.ItemsSource = CollisionSurfaces;
            RefreshMeshList();
        }

        private void RefreshMeshList()
        {
            if (_collisionMesh == null || MeshTypeSelector == null || MeshItemListBox == null) return;

            var selectedItem = (ComboBoxItem)MeshTypeSelector.SelectedItem;
            if (selectedItem == null) return;
            string type = selectedItem.Tag.ToString();

            var list = new List<MeshItemViewModel>();

            if (type == "ColVertices")
            {
                for (int i = 0; i < _collisionMesh.Vertices.Count; i++)
                {
                    var v = _collisionMesh.Vertices[i];
                    list.Add(new MeshItemViewModel
                    {
                        Target = v,
                        DisplayText = $"V{i}: ({v.X}, {v.Y}, {v.Z})"
                    });
                }
            }
            else if (type == "ColTriangles")
            {
                for (int i = 0; i < _collisionMesh.Triangles.Count; i++)
                {
                    var t = _collisionMesh.Triangles[i];
                    list.Add(new MeshItemViewModel
                    {
                        Target = t,
                        DisplayText = $"T{i}: V({t.V1},{t.V2},{t.V3}) {t.SurfaceType}"
                    });
                }
            }
            else if (type == "WaterBoxes")
            {
                if (_collisionMesh.WaterBoxes != null)
                {
                    for (int i = 0; i < _collisionMesh.WaterBoxes.Count; i++)
                    {
                        var wb = _collisionMesh.WaterBoxes[i];
                        list.Add(new MeshItemViewModel
                        {
                            Target = wb,
                            DisplayText = $"W{wb.Id}: X({wb.X1}..{wb.X2}) Z({wb.Z1}..{wb.Z2}) Y={wb.Y}"
                        });
                    }
                }
            }

            MeshItemListBox.ItemsSource = list;
            UpdateMeshEditorFieldsVisibility();
        }

        private void UpdateMeshEditorFieldsVisibility()
        {
            if (MeshTypeSelector == null) return;

            var selectedItem = (ComboBoxItem)MeshTypeSelector.SelectedItem;
            if (selectedItem == null) return;
            string type = selectedItem.Tag.ToString();

            var selectedVM = (MeshItemViewModel)MeshItemListBox.SelectedItem;

            MeshVertexEditorFields.Visibility = (type == "ColVertices" && selectedVM != null) ? Visibility.Visible : Visibility.Collapsed;
            MeshTriangleEditorFields.Visibility = (type == "ColTriangles" && selectedVM != null) ? Visibility.Visible : Visibility.Collapsed;
            MeshWaterEditorFields.Visibility = (type == "WaterBoxes" && selectedVM != null) ? Visibility.Visible : Visibility.Collapsed;

            if (selectedVM == null) return;

            _isUpdatingFromCode = true;

            if (type == "ColVertices" && selectedVM.Target is CollisionVertex v)
            {
                MeshVertXText.Text = v.X.ToString();
                MeshVertYText.Text = v.Y.ToString();
                MeshVertZText.Text = v.Z.ToString();
            }
            else if (type == "ColTriangles" && selectedVM.Target is CollisionTriangle t)
            {
                MeshTriV1Text.Text = t.V1.ToString();
                MeshTriV2Text.Text = t.V2.ToString();
                MeshTriV3Text.Text = t.V3.ToString();
                MeshTriSurfaceSelector.SelectedItem = t.SurfaceType;
                MeshTriParamText.Text = t.SpecialParam?.ToString() ?? "";
            }
            else if (type == "WaterBoxes" && selectedVM.Target is WaterBox wb)
            {
                MeshWaterIDText.Text = wb.Id.ToString();
                MeshWaterX1Text.Text = wb.X1.ToString();
                MeshWaterZ1Text.Text = wb.Z1.ToString();
                MeshWaterX2Text.Text = wb.X2.ToString();
                MeshWaterZ2Text.Text = wb.Z2.ToString();
                MeshWaterYText.Text = wb.Y.ToString();
            }

            _isUpdatingFromCode = false;
        }

        private void MeshTypeSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            RefreshMeshList();
        }

        private void MeshItemListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateMeshEditorFieldsVisibility();

            var selectedVM = (MeshItemViewModel)MeshItemListBox.SelectedItem;
            if (selectedVM == null) return;

            OpenTK.Mathematics.Vector3? point = null;

            if (selectedVM.Target is CollisionVertex v)
            {
                point = new OpenTK.Mathematics.Vector3((float)v.X, (float)v.Y, (float)v.Z);
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
                    point = new OpenTK.Mathematics.Vector3(
                        (float)((v1.X + v2.X + v3.X) / 3.0),
                        (float)((v1.Y + v2.Y + v3.Y) / 3.0),
                        (float)((v1.Z + v2.Z + v3.Z) / 3.0)
                    );
                }
            }
            else if (selectedVM.Target is WaterBox wb)
            {
                point = new OpenTK.Mathematics.Vector3(
                    (float)((wb.X1 + wb.X2) / 2.0),
                    (float)wb.Y,
                    (float)((wb.Z1 + wb.Z2) / 2.0)
                );
            }

            if (point.HasValue && _renderer != null)
            {
                _renderer.SelectedMeshPoint = point;
            }
        }

        private void MeshVertField_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingFromCode || _collisionMesh == null) return;

            var selectedVM = (MeshItemViewModel)MeshItemListBox.SelectedItem;
            if (selectedVM == null || !(selectedVM.Target is CollisionVertex v)) return;

            if (int.TryParse(MeshVertXText.Text, out int x)) v.X = x;
            if (int.TryParse(MeshVertYText.Text, out int y)) v.Y = y;
            if (int.TryParse(MeshVertZText.Text, out int z)) v.Z = z;

            selectedVM.DisplayText = $"V{_collisionMesh.Vertices.IndexOf(v)}: ({v.X}, {v.Y}, {v.Z})";
            var index = MeshItemListBox.SelectedIndex;
            MeshItemListBox.Items.Refresh();
            MeshItemListBox.SelectedIndex = index;
        }

        private void MeshTriField_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingFromCode || _collisionMesh == null) return;

            var selectedVM = (MeshItemViewModel)MeshItemListBox.SelectedItem;
            if (selectedVM == null || !(selectedVM.Target is CollisionTriangle t)) return;

            if (int.TryParse(MeshTriV1Text.Text, out int v1)) t.V1 = v1;
            if (int.TryParse(MeshTriV2Text.Text, out int v2)) t.V2 = v2;
            if (int.TryParse(MeshTriV3Text.Text, out int v3)) t.V3 = v3;
            
            if (string.IsNullOrWhiteSpace(MeshTriParamText.Text))
            {
                t.SpecialParam = null;
            }
            else
            {
                string pText = MeshTriParamText.Text.Trim();
                if (pText.StartsWith("0x"))
                {
                    try { t.SpecialParam = Convert.ToInt32(pText, 16); } catch { }
                }
                else
                {
                    if (int.TryParse(pText, out int pVal)) t.SpecialParam = pVal;
                }
            }

            selectedVM.DisplayText = $"T{_collisionMesh.Triangles.IndexOf(t)}: V({t.V1},{t.V2},{t.V3}) {t.SurfaceType}";
            var index = MeshItemListBox.SelectedIndex;
            MeshItemListBox.Items.Refresh();
            MeshItemListBox.SelectedIndex = index;
        }

        private void MeshTriSurfaceSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingFromCode || _collisionMesh == null) return;

            var selectedVM = (MeshItemViewModel)MeshItemListBox.SelectedItem;
            if (selectedVM == null || !(selectedVM.Target is CollisionTriangle t)) return;

            t.SurfaceType = MeshTriSurfaceSelector.SelectedItem as string ?? "SURFACE_DEFAULT";

            selectedVM.DisplayText = $"T{_collisionMesh.Triangles.IndexOf(t)}: V({t.V1},{t.V2},{t.V3}) {t.SurfaceType}";
            var index = MeshItemListBox.SelectedIndex;
            MeshItemListBox.Items.Refresh();
            MeshItemListBox.SelectedIndex = index;
        }

        private void MeshWaterField_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingFromCode || _collisionMesh == null) return;

            var selectedVM = (MeshItemViewModel)MeshItemListBox.SelectedItem;
            if (selectedVM == null || !(selectedVM.Target is WaterBox wb)) return;

            if (int.TryParse(MeshWaterIDText.Text, out int id)) wb.Id = id;
            if (int.TryParse(MeshWaterX1Text.Text, out int x1)) wb.X1 = x1;
            if (int.TryParse(MeshWaterZ1Text.Text, out int z1)) wb.Z1 = z1;
            if (int.TryParse(MeshWaterX2Text.Text, out int x2)) wb.X2 = x2;
            if (int.TryParse(MeshWaterZ2Text.Text, out int z2)) wb.Z2 = z2;
            if (int.TryParse(MeshWaterYText.Text, out int y)) wb.Y = y;

            selectedVM.DisplayText = $"W{wb.Id}: X({wb.X1}..{wb.X2}) Z({wb.Z1}..{wb.Z2}) Y={wb.Y}";
            var index = MeshItemListBox.SelectedIndex;
            MeshItemListBox.Items.Refresh();
            MeshItemListBox.SelectedIndex = index;
        }

        private void AddMeshItem_Click(object sender, RoutedEventArgs e)
        {
            if (_collisionMesh == null || MeshTypeSelector == null) return;

            var selectedItem = (ComboBoxItem)MeshTypeSelector.SelectedItem;
            if (selectedItem == null) return;
            string type = selectedItem.Tag.ToString();

            if (type == "ColVertices")
            {
                var newV = new CollisionVertex(0, 0, 0);
                _collisionMesh.Vertices.Add(newV);
            }
            else if (type == "ColTriangles")
            {
                var newT = new CollisionTriangle(0, 0, 0, "SURFACE_DEFAULT");
                _collisionMesh.Triangles.Add(newT);
            }
            else if (type == "WaterBoxes")
            {
                if (_collisionMesh.WaterBoxes == null) _collisionMesh.WaterBoxes = new List<WaterBox>();
                var newWb = new WaterBox(_collisionMesh.WaterBoxes.Count + 1, 0, 0, 0, 0, 0);
                _collisionMesh.WaterBoxes.Add(newWb);
            }

            RefreshMeshList();
            MeshItemListBox.SelectedIndex = MeshItemListBox.Items.Count - 1;
            MeshItemListBox.ScrollIntoView(MeshItemListBox.SelectedItem);
        }

        private void RemoveMeshItem_Click(object sender, RoutedEventArgs e)
        {
            if (_collisionMesh == null || MeshItemListBox.SelectedItem == null) return;

            var selectedVM = (MeshItemViewModel)MeshItemListBox.SelectedItem;
            int prevIndex = MeshItemListBox.SelectedIndex;

            if (selectedVM.Target is CollisionVertex v)
            {
                _collisionMesh.Vertices.Remove(v);
            }
            else if (selectedVM.Target is CollisionTriangle t)
            {
                _collisionMesh.Triangles.Remove(t);
            }
            else if (selectedVM.Target is WaterBox wb)
            {
                _collisionMesh.WaterBoxes?.Remove(wb);
            }

            RefreshMeshList();
            if (MeshItemListBox.Items.Count > 0)
            {
                MeshItemListBox.SelectedIndex = Math.Clamp(prevIndex, 0, MeshItemListBox.Items.Count - 1);
            }
        }

        private void SaveMeshChanges_Click(object sender, RoutedEventArgs e)
        {
            if (_collisionMesh == null || string.IsNullOrEmpty(_collisionFilePath)) return;

            var meshService = new LevelMeshService();
            if (meshService.SaveCollisionMesh(_collisionFilePath, _collisionMesh))
            {
                MessageBox.Show("Collision mesh changes saved successfully! The 3D viewport will now reload to apply the updates.", "Mesh Saved", MessageBoxButton.OK, MessageBoxImage.Information);
                RequestReload?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                MessageBox.Show("Failed to save collision mesh changes. Please make sure the file is writeable.", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SelectCollisionTriangleInUI(int index)
        {
            if (_collisionMesh == null || index < 0 || index >= _collisionMesh.Triangles.Count) return;

            // Switch to Mesh tab
            SidebarTabControl.SelectedIndex = 2;

            // Select "Collision Triangles"
            MeshTypeSelector.SelectedIndex = 1;

            var targetTri = _collisionMesh.Triangles[index];
            var items = MeshItemListBox.ItemsSource as List<MeshItemViewModel>;
            if (items != null)
            {
                var match = items.FirstOrDefault(item => item.Target == targetTri);
                if (match != null)
                {
                    MeshItemListBox.SelectedItem = match;
                    MeshItemListBox.ScrollIntoView(match);
                }
            }
        }

        private void SelectVisualVertexInUI(int index)
        {
            if (_collisionMesh == null || _renderer == null || !_renderer.SelectedMeshPoint.HasValue) return;

            var pt = _renderer.SelectedMeshPoint.Value;

            double minDist = double.MaxValue;
            CollisionVertex? closestVert = null;
            for (int i = 0; i < _collisionMesh.Vertices.Count; i++)
            {
                var v = _collisionMesh.Vertices[i];
                double d = Math.Sqrt(Math.Pow(v.X - pt.X, 2) + Math.Pow(v.Y - pt.Y, 2) + Math.Pow(v.Z - pt.Z, 2));
                if (d < minDist)
                {
                    minDist = d;
                    closestVert = v;
                }
            }

            if (closestVert != null && minDist < 200)
            {
                // Switch to Mesh tab
                SidebarTabControl.SelectedIndex = 2;
                // Select "Collision Vertices"
                MeshTypeSelector.SelectedIndex = 0;

                var items = MeshItemListBox.ItemsSource as List<MeshItemViewModel>;
                if (items != null)
                {
                    var match = items.FirstOrDefault(item => item.Target == closestVert);
                    if (match != null)
                    {
                        MeshItemListBox.SelectedItem = match;
                        MeshItemListBox.ScrollIntoView(match);
                    }
                }
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_renderer != null)
            {
                try { _renderer.Close(); } catch { }
                try { _renderer.Dispose(); } catch { }
                _renderer = null;
            }
        }
    }
}
