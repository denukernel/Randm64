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

        public LevelEditorWindow(List<LevelObject> objects, CollisionMesh collisionMesh, VisualMesh visualMesh, string projectRoot, int areaIndex, List<string> supportedModels, AppSettings settings)
        {
            InitializeComponent();
            _objects = objects;
            _areaIndex = areaIndex;
            _supportedModels = supportedModels;
            _settings = settings;
            
            _projectRoot = projectRoot;
            _behaviorService = new BehaviorService(projectRoot);
            
            PopulateTreeView();
            
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
                // 3. Fallback: focus renderer
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
                if (visMesh != null) _renderer.LoadVisualMesh(visMesh);
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

            var settingsService = new SettingsService();
            var settings = settingsService.LoadSettings();

            // 2. Check for emulator path
            if (string.IsNullOrEmpty(settings.EmulatorPath) || !File.Exists(settings.EmulatorPath))
            {
                var result = MessageBox.Show(
                    "Emulator path not set. Would you like to select your emulator (e.g. Project64.exe) now?",
                    "Setup Emulator",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    var dialog = new Microsoft.Win32.OpenFileDialog
                    {
                        Filter = "Executable Files (*.exe)|*.exe|All Files (*.*)|*.*",
                        Title = "Select N64 Emulator"
                    };

                    if (dialog.ShowDialog() == true)
                    {
                        settings.EmulatorPath = dialog.FileName;
                        settingsService.SaveSettings(settings);
                    }
                    else return;
                }
                else return;
            }

            // 3. Launch Build Window
            var buildWindow = new BuildOutputWindow(_projectRoot, settings.EmulatorPath);
            buildWindow.Owner = this;
            buildWindow.ShowDialog();
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
            // Future: Implement raycasting to drop object to nearest floor
            MessageBox.Show("Raycasting for 'Drop to Ground' is not yet implemented in this build.", "Feature Not Ready");
        }

        private void BehaviorTextBox_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            SelectBehaviorButton_Click(sender, e);
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
