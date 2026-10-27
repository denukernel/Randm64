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
using Sm64DecompLevelViewer.Services.Levels;
namespace Sm64DecompLevelViewer;
public partial class LevelEditorWindow
{
        private void PopulateTreeView()
        {
            NormalObjectsNode.Items.Clear();
            MacroObjectsNode.Items.Clear();
            SpecialObjectsNode.Items.Clear();

            SceneInfoText.Text = $"Area {_areaIndex}  ·  {_visualScene?.TriangleCount ?? 0:N0} triangles  ·  {_objects.Count(o => !o.IsDeleted):N0} objects";

            var normalBrush = (SolidColorBrush)new BrushConverter().ConvertFrom("#569CD6");
            var macroBrush = (SolidColorBrush)new BrushConverter().ConvertFrom("#6A9955");
            var specialBrush = (SolidColorBrush)new BrushConverter().ConvertFrom("#DCDCAA");

            for (int i = 0; i < _objects.Count; i++)
            {
                var obj = _objects[i];
                if (obj.IsDeleted) continue;

                var item = new TreeViewItem 
                { 
                    Header = $"[{i}] {(obj.ModelName == "MODEL_NONE" ? obj.Behavior.Replace("bhv", "") : obj.ModelName.Replace("MODEL_", ""))}",
                    ToolTip = $"{obj.ModelName}\n{obj.Behavior}\n({obj.X}, {obj.Y}, {obj.Z})",
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
            RotXTextBox.Text = obj.RX.ToString();
            RotZTextBox.Text = obj.RZ.ToString();
            
            BehaviorTextBox.Text = obj.Behavior;
            AddressTextBox.Text = obj.SourceIndex >= 0 ? $"0x{obj.SourceIndex:X8}" : "N/A";
            ParamsTextBox.Text = obj.RawParams ?? obj.Params.ToString("X8");

            NoSelectionText.Visibility = Visibility.Collapsed;
            PropertyStack.Visibility = Visibility.Visible;

            UpdatePropertyGrid(obj);

            _isUpdatingFromCode = false;
        }
        private void UpdatePropertyGrid(LevelObject obj)
        {
            string? reason = LevelObjectModelRules.RecordRestriction(obj) ??
                (_modelRules == null ? "checking model compatibility..." : _modelRules.Restriction(obj));
            SelectModelButton.IsEnabled = reason == null;
            ModelTextBox.ToolTip = SelectModelButton.ToolTip = reason ?? "choose a model compatible with this behavior";
            SelectBehaviorButton.IsEnabled = obj.SourceType != ObjectSourceType.Mario;
            SelectBehaviorButton.ToolTip = SelectBehaviorButton.IsEnabled ? null : LevelObjectModelRules.RecordRestriction(obj);
            string? problem = _modelRules?.ExistingModelWarning(obj);
            ModelCompatibilityText.Text = problem ?? "";
            ModelCompatibilityText.Visibility = problem == null ? Visibility.Collapsed : Visibility.Visible;
        }
        public void PerformCopy(object? sender = null, RoutedEventArgs? e = null)
        {
            if (_selectedObject != null)
            {
                _clipboard = _objectEdits.Copy(_selectedObject);
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
                    RX = _clipboard.RX,
                    RZ = _clipboard.RZ,
                    RY = _clipboard.RY,
                    AreaIndex = _clipboard.AreaIndex,
                    Params = _clipboard.Params,
                    RawParams = _clipboard.RawParams,
                    RawActs = _clipboard.RawActs,
                    PresetName = _clipboard.PresetName,
                    SourceType = _clipboard.SourceType,
                    IsNew = true
                };

                if (!PrepareNewObject(newObj)) return;

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
            if (_selectedObject.SourceType == ObjectSourceType.Mario || _modelRules == null) return;

            var behaviors = _behaviorService.GetBehaviors().Where(behavior => behavior == _selectedObject.Behavior ||
                _modelRules.PairRejection(behavior, _selectedObject.ModelName) == null).ToList();
            var dialog = new BehaviorSelectionWindow(behaviors, _selectedObject.Behavior);
            dialog.Owner = this;

            if (dialog.ShowDialog() == true)
            {
                string newBehavior = dialog.SelectedBehavior ?? string.Empty;
                if (newBehavior == _selectedObject.Behavior) return;
                if (_modelRules.PairRejection(newBehavior, _selectedObject.ModelName) is { } problem)
                {
                    MessageBox.Show(problem, "incompatible model and behavior");
                    return;
                }

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
                UpdatePropertyGrid(_selectedObject);

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
            if (_selectedObject == null || _modelRules == null || _modelRules.Restriction(_selectedObject) != null) return;

            var dialog = new ModelSelectionWindow(_projectRoot, _supportedModels,
                model => _modelRules.Rejection(_selectedObject, model) == null);
            dialog.Owner = this;

            if (dialog.ShowDialog() == true)
            {
                string model = dialog.SelectedModel ?? "MODEL_NONE";
                if (_modelRules.Rejection(_selectedObject, model) is { } reason)
                {
                    MessageBox.Show(reason, "model cannot be changed");
                    return;
                }
                _selectedObject.ModelName = model;
                ModelTextBox.Text = _selectedObject.ModelName;
                UpdatePropertyGrid(_selectedObject);

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
            if (_selectedObject.SourceType == ObjectSourceType.Mario)
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
            _removedObjects.Push(_selectedObject);
            _selectedObject.IsDeleted = true;
            
            // Hide from renderer
            if (_renderer != null)
            {
                int index = _objects.IndexOf(_selectedObject);
                _renderer.UpdateObject(index, _selectedObject);
                _renderer.SelectObject(-1);
            }

            // Update UI
            PopulateTreeView();
            NoSelectionText.Visibility = Visibility.Visible;
            PropertyStack.Visibility = Visibility.Collapsed;
            _selectedObject = null;
        }
        private void GroundObject_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedObject == null || _selectedObject.IsDeleted) return;
            try
            {
                _selectedObject.Y = ObjectSpawnPlacement.GroundY(_selectedObject.X, _selectedObject.Y, _selectedObject.Z, _collisionMesh);
                _renderer?.UpdateObject(_objects.IndexOf(_selectedObject), _selectedObject);
                PopulateTreeView();
                SelectObjectInUI(_objects.IndexOf(_selectedObject));
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "cannot place object on terrain", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
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

            if (_collisionMesh != null)
            {
                try { spawnY = ObjectSpawnPlacement.GroundY(spawnX, spawnY, spawnZ, _collisionMesh); }
                catch (InvalidOperationException) { } // The dialog explains off-terrain positions on confirmation.
            }
            var addWindow = new ObjectAddWindow(_projectRoot, behaviors, macroPresets, specialPresets, _supportedModels, spawnX, spawnY, spawnZ, _collisionMesh, _visualScene?.LevelDirectory);
            addWindow.Owner = this;
            if (addWindow.ShowDialog() == true)
            {
                var newObj = addWindow.NewObject;
                if (newObj != null)
                {
                    newObj.IsNew = true;
                    
                    if (!PrepareNewObject(newObj)) return;

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
        private bool PrepareNewObject(LevelObject obj)
        {
            try
            {
                _modelRules ??= _modelRulesTask.GetAwaiter().GetResult();
                if (obj.SourceType == ObjectSourceType.Normal && _modelRules.PairRejection(obj.Behavior, obj.ModelName) is { } problem)
                    throw new InvalidDataException(problem);
                LevelObjectEditSession.Prepare(obj, _objects, _areaIndex, _visualScene?.LevelDirectory, _collisionFilePath);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "cannot add object", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
        }
        private void RestoreRemovedObject_Click(object sender, RoutedEventArgs e)
        {
            if (!_removedObjects.TryPop(out var obj)) return;
            obj.IsDeleted = false;
            if (obj.SourceIndex < 0) obj.IsNew = true;
            PopulateTreeView();
            _renderer?.SetObjects(_objects);
            _renderer?.SelectObject(_objects.IndexOf(obj));
            SelectObjectInUI(_objects.IndexOf(obj));
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
                if (sender == ModelTextBox)
                {
                    // Model changes go through the compatibility-filtered picker.
                    if (ModelTextBox.Text != _selectedObject.ModelName) ModelTextBox.Text = _selectedObject.ModelName;
                    return;
                }
                else if (sender == PosXTextBox) _selectedObject.X = int.Parse(PosXTextBox.Text);
                else if (sender == PosYTextBox) _selectedObject.Y = int.Parse(PosYTextBox.Text);
                else if (sender == PosZTextBox) _selectedObject.Z = int.Parse(PosZTextBox.Text);
                else if (sender == RotXTextBox) _selectedObject.RX = int.Parse(RotXTextBox.Text);
                else if (sender == RotZTextBox) _selectedObject.RZ = int.Parse(RotZTextBox.Text);
                else if (sender == RotYTextBox) _selectedObject.RY = int.Parse(RotYTextBox.Text);
                else if (sender == ParamsTextBox)
                {
                    string parameter = ParamsTextBox.Text.Trim();
                    if (uint.TryParse(parameter.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? parameter[2..] : parameter,
                        System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out uint value))
                    {
                        _selectedObject.Params = value;
                        _selectedObject.RawParams = null;
                    }
                    else if (!string.IsNullOrWhiteSpace(parameter))
                    {
                        string wrapped = "PARAM(" + parameter + ")";
                        var parsed = CSourceCalls.Read(wrapped, name => name == "PARAM");
                        if (parsed.Count != 1 || parsed[0].End != wrapped.Length || parsed[0].Arguments.Count != 1 ||
                            parameter.IndexOfAny(new[] { ';', '{', '}', '\r', '\n' }) >= 0) return;
                        _selectedObject.RawParams = parameter;
                    }
                }
                
                if (_renderer != null)
                {
                    int index = _objects.IndexOf(_selectedObject);
                    _renderer.UpdateObject(index, _selectedObject);
                }
            }
            catch { }
        }
        private void UpdateRendererPosition()
        {
            if (_rendererHwnd == IntPtr.Zero) return;

            Dispatcher.BeginInvoke(new Action(() => {
                try {
                    // Get position relative to window
                    Point position = RendererContainer.TranslatePoint(new Point(0, 0), this);
                    var scale = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
                    Point devicePosition = scale.Transform(position);
                    MoveWindow(_rendererHwnd, (int)devicePosition.X, (int)devicePosition.Y,
                               (int)(RendererContainer.ActualWidth * scale.M11), (int)(RendererContainer.ActualHeight * scale.M22), true);
                } catch { }
            }), System.Windows.Threading.DispatcherPriority.Render);
        }
        private void UpdateUIFromObject(LevelObject obj)
        {
            _isUpdatingFromCode = true;
            PosXTextBox.Text = obj.X.ToString();
            PosYTextBox.Text = obj.Y.ToString();
            PosZTextBox.Text = obj.Z.ToString();
            RotYTextBox.Text = obj.RY.ToString();
            RotXTextBox.Text = obj.RX.ToString();
            RotZTextBox.Text = obj.RZ.ToString();
            _isUpdatingFromCode = false;
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
                string searchable = item.Header.ToString() ?? "";
                if (item.Tag is LevelObject obj) searchable += $" {obj.ModelName} {obj.Behavior} {obj.PresetName}";
                bool visible = string.IsNullOrEmpty(query) || searchable.Contains(query, StringComparison.OrdinalIgnoreCase);
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
        private void BehaviorTextBox_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            SelectBehaviorButton_Click(sender, e);
        }
}
