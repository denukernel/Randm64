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
        private string? _selectedActor;
        private GeometryRenderer? _renderer;
        private volatile bool _isClosing;
        private List<ActorTextureItem> _textures = new();
        private List<MarioAnimation> _actorAnimations = new();
        private MarioAnimation? _selectedAnimation;
        private bool _syncAnimationControls;
        private readonly System.Windows.Threading.DispatcherTimer _animationUiTimer = new()
            { Interval = TimeSpan.FromMilliseconds(100) };

        public ActorEditorWindow(string projectRoot)
        {
            InitializeComponent();
            _projectRoot = projectRoot;
            
            var settingsService = new SettingsService();
            _settings = settingsService.LoadSettings();

            _animationUiTimer.Tick += (_, _) =>
            {
                if (_renderer == null || _selectedAnimation == null) return;
                _syncAnimationControls = true;
                AnimationFrameSlider.Value = _renderer.AnimationFrame;
                AnimationFrameText.Text = $"Frame {_renderer.AnimationFrame + 1} / {_selectedAnimation.FrameCount}";
                PlayAnimButton.Content = _renderer.AnimationPlaying ? "Pause" : "Play";
                _syncAnimationControls = false;
            };

            Loaded += ActorEditorWindow_Loaded;
            Closing += ActorEditorWindow_Closing;
        }

        private void ActorEditorWindow_Loaded(object sender, RoutedEventArgs e)
        {
            ScanActors();
            _animationUiTimer.Start();
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
            if (_renderer != null && _selectedActor != ActorsListBox.SelectedItem as string)
            {
                _renderer.RequestCloseThreadSafe();
                _renderer = null;
            }
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
            LoadActorAnimations();

            if (_selectedActor == "mario")
            {
                MarioColorsPanel.Visibility = Visibility.Visible;
                LoadMarioColorsFromSource(actorDir);
            }
            else
            {
                MarioColorsPanel.Visibility = Visibility.Collapsed;
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
                VisualMesh visualMesh = ActorModelLoader.Load(_projectRoot, _selectedActor);

                // Frame the assembled idle pose rather than the unrotated bind skeleton.
                var previewAnimation = _selectedAnimation;
                new AnimationParser().ApplyFrame(visualMesh, previewAnimation, previewAnimation?.StartFrame ?? 0);
                bool loopAnimation = LoopAnimationCheckBox.IsChecked == true;
                float animationSpeed = (float)AnimationSpeedSlider.Value;

                // If renderer is already open, close it
                if (_renderer != null)
                {
                    try { _renderer.RequestCloseThreadSafe(); } catch { }

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
                    using var renderer = new GeometryRenderer(gameWindowSettings, nativeWindowSettings, _projectRoot);
                    _renderer = renderer;
                    if (_isClosing) { _renderer = null; return; }
                    renderer.CameraMoveSpeed = _settings.CameraMoveSpeed;
                    renderer.CameraRotationSensitivity = _settings.CameraRotationSpeed;
                    
                    renderer.LoadVisualMesh(visualMesh);
                    renderer.UseActorLighting = true;
                    renderer.LoopAnimationPreview = loopAnimation;
                    renderer.AnimationSpeed = animationSpeed;

                    // Frame camera nicely on actor model
                    if (visualMesh.Vertices.Count > 0)
                    {
                        float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
                        float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
                        foreach (var v in visualMesh.Vertices)
                        {
                            if (v.X < minX) minX = v.X;
                            if (v.X > maxX) maxX = v.X;
                            if (v.Y < minY) minY = v.Y;
                            if (v.Y > maxY) maxY = v.Y;
                            if (v.Z < minZ) minZ = v.Z;
                            if (v.Z > maxZ) maxZ = v.Z;
                        }
                        var center = new Vector3((minX + maxX) * 0.5f, (minY + maxY) * 0.5f, (minZ + maxZ) * 0.5f);
                        float size = (new Vector3(maxX - minX, maxY - minY, maxZ - minZ)).Length;
                        float distance = MathF.Max(size * 1.3f, 150f);
                        var camPos = center + new Vector3(distance * 0.45f, distance * 0.25f, distance * 1.5f);
                        renderer.SetCameraPositionAndTarget(camPos, center);
                    }

                    if (previewAnimation != null)
                    {
                        renderer.SetActiveAnimation(previewAnimation);
                    }
                    renderer.Run();
                    if (ReferenceEquals(_renderer, renderer)) _renderer = null;
                });

                rendererThread.SetApartmentState(System.Threading.ApartmentState.STA);
                rendererThread.IsBackground = true;
                rendererThread.Name = "Actor viewport";
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
                        var newMesh = ActorModelLoader.Load(_projectRoot, _selectedActor);
                        _renderer.LoadVisualMeshThreadSafe(newMesh);
                        _renderer.ReloadTexturesThreadSafe();
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
            _isClosing = true;
            _animationUiTimer.Stop();
            if (_renderer != null)
            {
                try { _renderer.RequestCloseThreadSafe(); } catch { }

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
                    var newMesh = ActorModelLoader.Load(_projectRoot, "mario");
                    _renderer.LoadVisualMeshThreadSafe(newMesh);
                    _renderer.ReloadTexturesThreadSafe();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error updating Mario color: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadActorAnimations()
        {
            try
            {
                var parser = new AnimationParser();
                var geo = new GeoLayoutParser();
                geo.ParseActorGeoLayout(Path.Combine(_projectRoot, "actors", _selectedActor!, "geo.inc.c"));
                _actorAnimations = parser.ScanActorAnimations(_projectRoot, _selectedActor!, geo.ParsedJoints.Count);
                AnimationComboBox.ItemsSource = _actorAnimations.Select(a => a.Name).ToList();
                MarioAnimationsPanel.Visibility = _actorAnimations.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                if (_actorAnimations.Count > 0)
                {
                    int idle = _actorAnimations.IndexOf(AnimationParser.SelectDefaultAnimation(_actorAnimations, _selectedActor!)!);
                    AnimationComboBox.SelectedIndex = idle >= 0 ? idle : 0;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading actor animations: {ex.Message}");
            }
        }

        private void AnimationComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (AnimationComboBox.SelectedIndex >= 0 && AnimationComboBox.SelectedIndex < _actorAnimations.Count)
            {
                _selectedAnimation = _actorAnimations[AnimationComboBox.SelectedIndex];
                var parser = new AnimationParser();
                try
                {
                    parser.LoadAnimationData(_selectedAnimation);
                }
                catch (Exception ex)
                {
                    _selectedAnimation = null;
                    MessageBox.Show($"Unable to load animation: {ex.Message}", "Animation", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                _syncAnimationControls = true;
                AnimationFrameSlider.Maximum = Math.Max(0, _selectedAnimation.FrameCount - 1);
                AnimationFrameSlider.Value = _selectedAnimation.StartFrame;
                AnimationFrameText.Text = $"Frame 1 / {_selectedAnimation.FrameCount}";
                _syncAnimationControls = false;
                
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
            if (_renderer == null && _selectedAnimation != null)
            {
                Open3D_Click(sender, e);
                return;
            }
            if (_renderer != null && _selectedAnimation != null)
            {
                _renderer.SetAnimationPlaying(!_renderer.AnimationPlaying);
            }
        }

        private void StopAnim_Click(object sender, RoutedEventArgs e)
        {
            if (_renderer != null)
            {
                _renderer.SeekAnimationFrame(_selectedAnimation?.StartFrame ?? 0);
                _renderer.SetAnimationPlaying(false);
            }
        }

        private void AnimationSettings_Changed(object sender, RoutedEventArgs e)
        {
            if (_renderer != null) _renderer.LoopAnimationPreview = LoopAnimationCheckBox.IsChecked == true;
        }

        private void AnimationSpeed_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_renderer != null) _renderer.AnimationSpeed = (float)e.NewValue;
        }

        private void AnimationFrame_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_syncAnimationControls && _renderer != null)
            {
                _renderer.SetAnimationPlaying(false);
                _renderer.SeekAnimationFrame((int)e.NewValue);
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
