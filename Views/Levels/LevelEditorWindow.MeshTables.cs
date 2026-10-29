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
                int removed = _collisionMesh.Vertices.IndexOf(v);
                _collisionMesh.Triangles.RemoveAll(t => t.V1==removed || t.V2==removed || t.V3==removed);
                foreach (var triangle in _collisionMesh.Triangles)
                {
                    if (triangle.V1>removed) triangle.V1--;
                    if (triangle.V2>removed) triangle.V2--;
                    if (triangle.V3>removed) triangle.V3--;
                }
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
                _renderer?.LoadMeshThreadSafe(_collisionMesh);
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
            var vertices = _visualScene?.SubMeshes.Count > 0
                ? _visualScene.SubMeshes.SelectMany(mesh => mesh.Vertices).ToList()
                : _visualScene?.Vertices;
            if (vertices != null && index >= 0 && index < vertices.Count)
            {
                var source = vertices[index];
                if (source.SourceArrayName != null)
                {
                    if (IsMeshMode) EnsureMeshViewer();
                    else LevelMeshEditorButton_Click(this,new RoutedEventArgs());
                    _embeddedMeshEditor?.SelectSourceVertex(source.SourceArrayName,source.SourceArrayIndex);
                    MeshSelectionText.Text = $"Vertex {source.SourceArrayIndex} selected";
                    return;
                }
            }
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
}
