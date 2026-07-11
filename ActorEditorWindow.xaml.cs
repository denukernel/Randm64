using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using OpenTK.Mathematics;
using OpenTK.Windowing.Desktop;
using Sm64DecompLevelViewer.Models;
using Sm64DecompLevelViewer.Services;
using Sm64DecompLevelViewer.Rendering;

namespace Sm64DecompLevelViewer
{
    public partial class ActorEditorWindow : Window
    {
        private readonly string _projectRoot;
        private readonly AppSettings _settings;
        private readonly ModelParser _modelParser;
        private string? _selectedActor;
        private GeometryRenderer? _renderer;
        private List<ActorTextureItem> _textures = new();
        private List<MarioAnimation> _marioAnimations = new();
        private MarioAnimation? _selectedAnimation;

        public ActorEditorWindow(string projectRoot)
        {
            InitializeComponent();
            _projectRoot = projectRoot;
            _modelParser = new ModelParser();
            
            var settingsService = new SettingsService();
            _settings = settingsService.LoadSettings();

            Loaded += ActorEditorWindow_Loaded;
            Closing += ActorEditorWindow_Closing;
        }

        private void ActorEditorWindow_Loaded(object sender, RoutedEventArgs e)
        {
            ScanActors();
        }

        private void ScanActors()
        {
            try
            {
                string actorsPath = Path.Combine(_projectRoot, "actors");
                if (!Directory.Exists(actorsPath))
                {
                    MessageBox.Show($"Actors directory not found: {actorsPath}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    Close();
                    return;
                }

                var dirs = Directory.GetDirectories(actorsPath);
                var actorList = new List<string>();

                foreach (var dir in dirs)
                {
                    string actorName = Path.GetFileName(dir);
                    string modelPath = Path.Combine(dir, "model.inc.c");
                    
                    // A folder is a valid actor if it contains model.inc.c
                    if (File.Exists(modelPath))
                    {
                        actorList.Add(actorName);
                    }
                }

                ActorsListBox.ItemsSource = actorList.OrderBy(x => x).ToList();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error scanning actors: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ActorsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedActor = ActorsListBox.SelectedItem as string;

            if (string.IsNullOrEmpty(_selectedActor))
            {
                ActorNameText.Text = "Select an actor";
                ActorPathText.Text = "";
                Open3DButton.IsEnabled = false;
                TexturesListBox.ItemsSource = null;
                return;
            }

            ActorNameText.Text = _selectedActor;
            string actorDir = Path.Combine(_projectRoot, "actors", _selectedActor);
            ActorPathText.Text = actorDir;
            Open3DButton.IsEnabled = true;

            if (_selectedActor == "mario")
            {
                MarioColorsPanel.Visibility = Visibility.Visible;
                MarioAnimationsPanel.Visibility = Visibility.Visible;
                LoadMarioColorsFromSource(actorDir);
                LoadMarioAnimations();
            }
            else
            {
                MarioColorsPanel.Visibility = Visibility.Collapsed;
                MarioAnimationsPanel.Visibility = Visibility.Collapsed;
            }

            LoadActorTextures(actorDir);
        }

        private void LoadActorTextures(string actorDir)
        {
            _textures.Clear();
            try
            {
                var pngFiles = Directory.GetFiles(actorDir, "*.png");
                foreach (var file in pngFiles)
                {
                    _textures.Add(new ActorTextureItem
                    {
                        FileName = Path.GetFileName(file),
                        FilePath = file,
                        ImageSource = LoadImageWithoutLocking(file),
                        ResolutionText = GetImageResolution(file)
                    });
                }
                TexturesListBox.ItemsSource = null;
                TexturesListBox.ItemsSource = _textures;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading actor textures: {ex.Message}");
            }
        }

        private BitmapImage LoadImageWithoutLocking(string path)
        {
            try
            {
                var bitmap = new BitmapImage();
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = stream;
                    bitmap.EndInit();
                }
                bitmap.Freeze();
                return bitmap;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading image without lock: {ex.Message}");
                return new BitmapImage();
            }
        }

        private string GetImageResolution(string path)
        {
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                    var frame = decoder.Frames[0];
                    return $"{frame.PixelWidth}x{frame.PixelHeight}";
                }
            }
            catch
            {
                return "Unknown size";
            }
        }

        private void Open3D_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_selectedActor)) return;

            try
            {
                string actorDir = Path.Combine(_projectRoot, "actors", _selectedActor);
                string modelPath = Path.Combine(actorDir, "model.inc.c");
                string geoPath = Path.Combine(actorDir, "geo.inc.c");

                var geoParser = new GeoLayoutParser();
                var transforms = geoParser.ParseActorGeoLayout(geoPath);

                VisualMesh? visualMesh = _modelParser.ParseModelFile(modelPath, "", _selectedActor, transforms, geoParser.DlToJointIndex);
                if (visualMesh != null)
                {
                    visualMesh.Joints = geoParser.ParsedJoints;
                    visualMesh.DlToJointIndex = geoParser.DlToJointIndex;
                    _modelParser.ParseActorTextureMapping(actorDir, modelPath, visualMesh, _projectRoot);
                }

                if (visualMesh == null)
                {
                    MessageBox.Show("Failed to parse actor model geometry.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // If renderer is already open, close it
                if (_renderer != null)
                {
                    try { _renderer.Close(); } catch { }
                    try { _renderer.Dispose(); } catch { }
                    _renderer = null;
                }

                var nativeWindowSettings = new NativeWindowSettings()
                {
                    Size = new Vector2i(800, 600),
                    Title = $"Actor View - {_selectedActor} | [W][A][S][D] Move | [Right-Click] Rotate",
                    Flags = OpenTK.Windowing.Common.ContextFlags.Default,
                    Profile = OpenTK.Windowing.Common.ContextProfile.Core,
                };

                var gameWindowSettings = new GameWindowSettings()
                {
                    UpdateFrequency = 60.0
                };

                var rendererThread = new System.Threading.Thread(() =>
                {
                    _renderer = new GeometryRenderer(gameWindowSettings, nativeWindowSettings, _projectRoot);
                    _renderer.CameraMoveSpeed = _settings.CameraMoveSpeed;
                    _renderer.CameraRotationSensitivity = _settings.CameraRotationSpeed;
                    
                    _renderer.LoadVisualMesh(visualMesh);
                    if (_selectedAnimation != null)
                    {
                        _renderer.SetActiveAnimation(_selectedAnimation);
                    }
                    _renderer.Run();
                });

                rendererThread.SetApartmentState(System.Threading.ApartmentState.STA);
                rendererThread.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error starting 3D viewport:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ReplaceTexture_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var item = button?.Tag as ActorTextureItem;
            if (item == null || string.IsNullOrEmpty(_selectedActor)) return;

            var dialog = new OpenFileDialog
            {
                Filter = "PNG Image (*.png)|*.png|All Files (*.*)|*.*",
                Title = $"Select Replacement for {item.FileName}"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    // Copy new file over the old one
                    File.Copy(dialog.FileName, item.FilePath, true);

                    // Refresh texture list view
                    string actorDir = Path.Combine(_projectRoot, "actors", _selectedActor);
                    LoadActorTextures(actorDir);

                    // If renderer is open, update visual mesh texture maps and reload OpenGL cache
                    if (_renderer != null)
                    {
                        string modelPath = Path.Combine(actorDir, "model.inc.c");
                        string geoPath = Path.Combine(actorDir, "geo.inc.c");

                        var geoParser = new GeoLayoutParser();
                        var transforms = geoParser.ParseActorGeoLayout(geoPath);

                        VisualMesh? newMesh = _modelParser.ParseModelFile(modelPath, "", _selectedActor, transforms, geoParser.DlToJointIndex);
                        if (newMesh != null)
                        {
                            newMesh.Joints = geoParser.ParsedJoints;
                            newMesh.DlToJointIndex = geoParser.DlToJointIndex;
                            _modelParser.ParseActorTextureMapping(actorDir, modelPath, newMesh, _projectRoot);
                            _renderer.LoadVisualMeshThreadSafe(newMesh);
                            _renderer.ReloadTexturesThreadSafe();
                        }
                    }

                    MessageBox.Show($"Replaced texture: {item.FileName}.\nBuild the project to compile this texture into the game.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error replacing texture: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ActorEditorWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_renderer != null)
            {
                try { _renderer.Close(); } catch { }
                try { _renderer.Dispose(); } catch { }
                _renderer = null;
            }
        }

        private void LoadMarioColorsFromSource(string actorDir)
        {
            try
            {
                string modelPath = Path.Combine(actorDir, "model.inc.c");
                if (!File.Exists(modelPath)) return;

                string content = File.ReadAllText(modelPath);

                SetButtonColor(OverallsColorButton, GetColorFromGroup(content, "mario_blue_lights_group"));
                SetButtonColor(ShirtColorButton, GetColorFromGroup(content, "mario_red_lights_group"));
                SetButtonColor(GlovesColorButton, GetColorFromGroup(content, "mario_white_lights_group"));
                SetButtonColor(ShoesColorButton, GetColorFromGroup(content, "mario_brown1_lights_group"));
                SetButtonColor(SkinColorButton, GetColorFromGroup(content, "mario_beige_lights_group"));
                SetButtonColor(HairColorButton, GetColorFromGroup(content, "mario_brown2_lights_group"));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading Mario colors: {ex.Message}");
            }
        }

        private System.Windows.Media.Color GetColorFromGroup(string content, string groupName)
        {
            var regex = new System.Text.RegularExpressions.Regex(@"static\s+const\s+Lights1\s+" + groupName + @"\s*=\s*gdSPDefLights1\(\s*(0x[0-9a-fA-F]{2}|\d+),\s*(0x[0-9a-fA-F]{2}|\d+),\s*(0x[0-9a-fA-F]{2}|\d+),\s*(0x[0-9a-fA-F]{2}|\d+),\s*(0x[0-9a-fA-F]{2}|\d+),\s*(0x[0-9a-fA-F]{2}|\d+)", System.Text.RegularExpressions.RegexOptions.Compiled);
            var match = regex.Match(content);
            if (match.Success)
            {
                byte r = ParseByteHelper(match.Groups[4].Value);
                byte g = ParseByteHelper(match.Groups[5].Value);
                byte b = ParseByteHelper(match.Groups[6].Value);
                return System.Windows.Media.Color.FromRgb(r, g, b);
            }
            return System.Windows.Media.Colors.Gray;
        }

        private byte ParseByteHelper(string str)
        {
            if (str.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return Convert.ToByte(str, 16);
            return byte.Parse(str);
        }

        private void SetButtonColor(Button btn, System.Windows.Media.Color color)
        {
            btn.Background = new System.Windows.Media.SolidColorBrush(color);
        }

        private void ColorButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button == null || string.IsNullOrEmpty(_selectedActor)) return;

            var currentBrush = button.Background as System.Windows.Media.SolidColorBrush;
            var currentColor = currentBrush?.Color ?? System.Windows.Media.Colors.White;

            var dialog = new global::System.Windows.Forms.ColorDialog
            {
                Color = System.Drawing.Color.FromArgb(currentColor.R, currentColor.G, currentColor.B)
            };

            if (dialog.ShowDialog() == global::System.Windows.Forms.DialogResult.OK)
            {
                var newColor = System.Windows.Media.Color.FromRgb(dialog.Color.R, dialog.Color.G, dialog.Color.B);
                button.Background = new System.Windows.Media.SolidColorBrush(newColor);

                string groupName = string.Empty;
                if (button == OverallsColorButton) groupName = "mario_blue_lights_group";
                else if (button == ShirtColorButton) groupName = "mario_red_lights_group";
                else if (button == GlovesColorButton) groupName = "mario_white_lights_group";
                else if (button == ShoesColorButton) groupName = "mario_brown1_lights_group";
                else if (button == SkinColorButton) groupName = "mario_beige_lights_group";
                else if (button == HairColorButton) groupName = "mario_brown2_lights_group";

                if (!string.IsNullOrEmpty(groupName))
                {
                    UpdateMarioColorInSource(groupName, newColor);
                }
            }
        }

        private void UpdateMarioColorInSource(string groupName, System.Windows.Media.Color newColor)
        {
            try
            {
                string actorDir = Path.Combine(_projectRoot, "actors", "mario");
                string modelPath = Path.Combine(actorDir, "model.inc.c");
                if (!File.Exists(modelPath)) return;

                string content = File.ReadAllText(modelPath);

                byte rDif = newColor.R;
                byte gDif = newColor.G;
                byte bDif = newColor.B;

                byte rAmb = (byte)(rDif / 2);
                byte gAmb = (byte)(gDif / 2);
                byte bAmb = (byte)(bDif / 2);

                var regex = new System.Text.RegularExpressions.Regex(@"(static\s+const\s+Lights1\s+" + groupName + @"\s*=\s*gdSPDefLights1\(\s*)" +
                                     @"(?:0x[0-9a-fA-F]{2}|\d+),\s*(?:0x[0-9a-fA-F]{2}|\d+),\s*(?:0x[0-9a-fA-F]{2}|\d+),\s*" +
                                     @"(?:0x[0-9a-fA-F]{2}|\d+),\s*(?:0x[0-9a-fA-F]{2}|\d+),\s*(?:0x[0-9a-fA-F]{2}|\d+)", System.Text.RegularExpressions.RegexOptions.Compiled);

                string updatedContent = regex.Replace(content, $"${{1}}0x{rAmb:X2}, 0x{gAmb:X2}, 0x{bAmb:X2}, 0x{rDif:X2}, 0x{gDif:X2}, 0x{bDif:X2}");
                File.WriteAllText(modelPath, updatedContent);

                if (_renderer != null)
                {
                    string geoPath = Path.Combine(actorDir, "geo.inc.c");
                    var geoParser = new GeoLayoutParser();
                    var transforms = geoParser.ParseActorGeoLayout(geoPath);

                    VisualMesh? newMesh = _modelParser.ParseModelFile(modelPath, "", "mario", transforms, geoParser.DlToJointIndex);
                    if (newMesh != null)
                    {
                        newMesh.Joints = geoParser.ParsedJoints;
                        newMesh.DlToJointIndex = geoParser.DlToJointIndex;
                        _modelParser.ParseActorTextureMapping(actorDir, modelPath, newMesh, _projectRoot);
                        _renderer.LoadVisualMeshThreadSafe(newMesh);
                        _renderer.ReloadTexturesThreadSafe();
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error updating Mario color: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadMarioAnimations()
        {
            try
            {
                var parser = new AnimationParser();
                _marioAnimations = parser.ScanAnimations(_projectRoot);
                AnimationComboBox.ItemsSource = _marioAnimations.Select(a => a.Name).ToList();
                if (_marioAnimations.Count > 0)
                {
                    AnimationComboBox.SelectedIndex = 0;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading Mario animations: {ex.Message}");
            }
        }

        private void AnimationComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (AnimationComboBox.SelectedIndex >= 0 && AnimationComboBox.SelectedIndex < _marioAnimations.Count)
            {
                _selectedAnimation = _marioAnimations[AnimationComboBox.SelectedIndex];
                var parser = new AnimationParser();
                parser.LoadAnimationData(_selectedAnimation);
                
                if (_renderer != null && _selectedAnimation != null)
                {
                    _renderer.SetActiveAnimation(_selectedAnimation);
                }
            }
            else
            {
                _selectedAnimation = null;
                if (_renderer != null)
                {
                    _renderer.SetActiveAnimation(null);
                }
            }
        }

        private void PlayAnim_Click(object sender, RoutedEventArgs e)
        {
            if (_renderer != null && _selectedAnimation != null)
            {
                _renderer.SetActiveAnimation(_selectedAnimation);
            }
        }

        private void StopAnim_Click(object sender, RoutedEventArgs e)
        {
            if (_renderer != null)
            {
                _renderer.SetActiveAnimation(null);
            }
        }
    }

    public class ActorTextureItem
    {
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public System.Windows.Media.ImageSource? ImageSource { get; set; }
        public string ResolutionText { get; set; } = string.Empty;
    }
}
