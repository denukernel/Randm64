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
        private void Renderer_SplinePointModified()
        {
            Dispatcher.Invoke(() =>
            {
                if (_activeSplineName != null && _activeSplinePoints != null)
                {
                    _splineEdits.Save();
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
            StopPathPreview_Click(this,new RoutedEventArgs());
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
                        _renderer.SplinePathName = _activeSplineName;
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

            _splineEdits.Save();

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

            _splineEdits.Save();

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

                _splineEdits.Save();

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
        private void StopPathPreview_Click(object sender, RoutedEventArgs e)
        {
            _previewTimer?.Stop();
            _previewTimer = null;
            _previewClock?.Stop();
            _previewPaused = false;
            _renderer?.StopPreview();
        }
        private void PlayPathPreview_Click(object sender, RoutedEventArgs e)
        {
            if (_previewTimer != null)
            {
                _previewPaused = !_previewPaused;
                if (_previewPaused) { _previewTimer.Stop(); _previewClock?.Stop(); }
                else { _previewTimer.Start(); _previewClock?.Start(); }
                return;
            }
            if (_activeSplineName == null || _renderer == null) return;
            string baseName = _activeSplineName.Replace("Position", "").Replace("Focus", "");
            var positions = _cutsceneService.LoadSpline(_cameraCPath, baseName + "Position");
            var focuses = _cutsceneService.LoadSpline(_cameraCPath, baseName + "Focus");
            if (positions.Count < 4 || focuses.Count < 4)
            {
                MessageBox.Show("Select a paired Position/Focus path with at least four control points.", "Camera preview");
                return;
            }
            var lakituPlayback = _introActors?.CreateFlightPlayback();
            var playback = new CameraSplinePlayback(baseName == "sIntroPipeToDialog" ? 0.1f : 0);
            _previewClock = System.Diagnostics.Stopwatch.StartNew();
            long frame = 0;
            _previewTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.0/30) };
            _previewTimer.Tick += (_, _) =>
            {
                if (_renderer == null) { _previewTimer?.Stop(); return; }
                // The game evaluates position and focus sequentially using the shared spline cursor.
                long targetFrame = (long)(_previewClock!.Elapsed.TotalSeconds*30);
                int budget = 120;
                while (frame <= targetFrame && budget-- > 0 && !playback.Finished)
                {
                    var position = playback.Advance(positions);
                    var focus = playback.Advance(focuses);
                    var pose=CameraSplinePlayback.WorldPose(baseName,position,focus);
                    var actors = baseName == "sIntroStartToPipe" && lakituPlayback != null
                        ? lakituPlayback.Advance(pose.Position, pose.Focus) : Array.Empty<IntroActorPose>();
                    _renderer.SubmitCutsceneFrame(pose.Position, pose.Focus, actors);
                    frame++;
                }
                if (playback.Finished) StopPathPreview_Click(this, new RoutedEventArgs());
            };
            _previewTimer.Start();
        }
}
