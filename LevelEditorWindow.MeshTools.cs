using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Sm64DecompLevelViewer.Services;
using OpenTK.Mathematics;

namespace Sm64DecompLevelViewer;

public partial class LevelEditorWindow
{
    private MeshEditSession? _meshSession;
    private Task? _meshPreparation;
    private bool _meshSaving;
    private bool _meshCloseApproved;
    private string? _pendingMeshStatus;
    private int _meshStatusQueued;
    private bool IsMeshMode => LevelMeshMode?.IsChecked == true;

    private async void EditMode_Changed(object sender, RoutedEventArgs e)
    {
        if (MeshModePanel == null || ObjectModePanel == null || NavigationControls == null) return;
        MeshModePanel.Visibility = IsMeshMode ? Visibility.Visible : Visibility.Collapsed;
        ObjectModePanel.Visibility = IsMeshMode ? Visibility.Collapsed : Visibility.Visible;
        MeshDockRow.Height = new GridLength(0);
        if (_renderer != null && !_meshSaving) _renderer.MeshSessionBusy = false;
        NavigationControls.Visibility = IsMeshMode ? Visibility.Collapsed : Visibility.Visible;
        MeshTool_Changed(sender, e);
        UpdateRendererPosition();
        if (IsMeshMode) await PrepareMeshToolsAsync();
    }

    private void MeshTool_Changed(object sender, RoutedEventArgs e)
    {
        if (MeshToolHint == null || MeshBoxOptions == null || MeshPaintTextureCombo == null || CollisionSurfaceOptions == null) return;
        bool paint = MeshPaintTool.IsChecked == true;
        bool box = MeshBoxTool.IsChecked == true;
        bool collision = CollisionTypeTool.IsChecked == true;
        MeshEditOptions.Visibility = !paint && !box && !collision ? Visibility.Visible : Visibility.Collapsed;
        MeshPaintOptions.Visibility = paint ? Visibility.Visible : Visibility.Collapsed;
        MeshBoxOptions.Visibility = box ? Visibility.Visible : Visibility.Collapsed;
        CollisionSurfaceOptions.Visibility = collision ? Visibility.Visible : Visibility.Collapsed;
        MeshToolHint.Text = collision ? "change collision behavior without changing textures. unsupported surface types and force values are blocked."
            : paint ? "paint textures onto faces. edits preview immediately and stay in memory until save."
            : box ? "create a box, then move, rotate or scale it in the placement preview before confirming."
            : "drag vertices or faces. hold x, y or z to constrain an axis; shift snaps to 50 units; escape cancels the drag.";
        if (_renderer != null)
        {
            _renderer.MeshEditingEnabled = IsMeshMode;
            _renderer.MeshBoxToolActive = box;
            _renderer.CollisionPaintingEnabled = false;
            _renderer.TexturePaintingEnabled = IsMeshMode && paint;
            _renderer.CollisionSurfacePaintingEnabled = IsMeshMode && collision;
            _renderer.MeshTarget = (GeometryEditTarget)Math.Max(0, MeshMovementTarget.SelectedIndex);
            _renderer.MeshFaceSelection = MeshFaceSelection?.IsChecked == true;
            if (IsMeshMode && (collision || _renderer.MeshTarget == GeometryEditTarget.CollisionOnly)) CollisionViewCheckBox.IsChecked = true;
        }
    }

    private void MoveMeshVertex_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(MeshMoveStep.Text, out int step) || step <= 0 || step > short.MaxValue)
        {
            MeshSelectionText.Text = "Enter a step between 1 and 32767";
            return;
        }
        string axis = (string)((Button)sender).Tag;
        int delta = axis.StartsWith("-") ? -step : step;
        _renderer?.NudgeMeshSelection(axis.EndsWith("x") ? new Vector3(delta, 0, 0) : axis.EndsWith("y") ? new Vector3(0, delta, 0) : new Vector3(0, 0, delta));
    }

    private void PlaceMeshBox_Click(object sender, RoutedEventArgs e)
    {
        if (_meshSession?.IsDirty == true) { MeshSelectionText.Text = "Save mesh before adding a box"; return; }
        EnsureMeshViewer();
        _embeddedMeshEditor?.PlaceBox(MeshBoxSize.Text, MeshBoxCollision.IsChecked == true);
    }

    private void ReportMeshStatus(string message)
    {
        _pendingMeshStatus = message;
        if (System.Threading.Interlocked.Exchange(ref _meshStatusQueued, 1) != 0) return;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            System.Threading.Interlocked.Exchange(ref _meshStatusQueued, 0);
            if (!_isClosing) MeshSelectionText.Text = _pendingMeshStatus;
        }));
    }

    private Task PrepareMeshToolsAsync()
    {
        if (_meshSession != null || _visualScene?.LevelDirectory == null) return Task.CompletedTask;
        return _meshPreparation ??= LoadMeshToolsAsync();
    }

    private async Task LoadMeshToolsAsync()
    {
        var scene = _visualScene!;
        MeshSelectionText.Text = "Preparing mesh tools...";
        try
        {
            var collision = _collisionMesh;
            var session = await Task.Run(() => new MeshEditSession(scene, _projectRoot, collision: collision, collisionPath: _collisionFilePath));
            if (_isClosing || !ReferenceEquals(scene, _visualScene)) return;
            _meshSession = session;
            if (_renderer != null) _renderer.InstallMeshSession(session);
            MeshPaintTextureCombo.ItemsSource = session.Textures;
            MeshPaintTextureCombo.SelectedIndex = session.Textures.Count > 0 ? 0 : -1;
            CollisionSurfaceSelector.ItemsSource = session.SurfacePolicy?.Choices;
            CollisionSurfaceSelector.SelectedItem = session.SurfacePolicy?.Choices.FirstOrDefault(c => c.Name == "SURFACE_DEFAULT");
            MeshSelectionText.Text = "Ready; drag vertices or faces, or select a texture to paint";
        }
        catch (Exception ex) { MeshSelectionText.Text = "Mesh tools could not load: " + ex.Message; }
        finally { _meshPreparation = null; }
    }

    private void MeshPaintTexture_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (MeshTexturePreview == null) return;
        var texture = MeshPaintTextureCombo.SelectedItem as MeshPaintTexture;
        if (_renderer != null) _renderer.MeshPaintSymbol = texture?.Symbol;
        MeshTexturePreview.Source = texture == null ? null : new BitmapImage(new Uri(texture.Path));
    }

    private async void SaveMeshSession_Click(object sender, RoutedEventArgs e) => await SaveMeshSessionAsync();

    private void CollisionSurface_Changed(object sender, SelectionChangedEventArgs e) => UpdateCollisionSurfaceBrush();
    private void CollisionForce_Changed(object sender, TextChangedEventArgs e) => UpdateCollisionSurfaceBrush();
    private void UpdateCollisionSurfaceBrush()
    {
        if (CollisionSurfaceHelp == null || CollisionForceText == null) return;
        var choice = CollisionSurfaceSelector.SelectedItem as CollisionSurfaceChoice;
        bool forceSurface = choice != null && CollisionSourceValidation.HasForce(choice.Name);
        CollisionForceText.IsEnabled = forceSurface;
        string text = CollisionForceText.Text.Trim();
        bool valid = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? int.TryParse(text[2..], System.Globalization.NumberStyles.HexNumber, null, out int force)
            : int.TryParse(text, out force);
        string help = choice?.Reason ?? "select a supported collision type";
        if (choice != null && choice.IsSupported)
        {
            try { _meshSession?.SurfacePolicy?.Validate(choice.Name, forceSurface ? valid ? force : null : null); if (forceSurface) help = "flowing water and moving sand require a speed index of 0 to 3 in the high byte; the low byte is direction"; }
            catch (Exception ex) { valid = false; help = ex.Message; }
        }
        if (_renderer != null)
        {
            _renderer.CollisionSurfaceSymbol = choice?.IsSupported == true ? choice.Name : null;
            _renderer.CollisionSurfaceForce = force;
            _renderer.CollisionSurfaceForceValid = !forceSurface || valid;
        }
        CollisionSurfaceHelp.Text = help;
    }

    private async Task<bool> SaveMeshSessionAsync()
    {
        if (_meshSaving) return false;
        if (MeshDockRow.Height.Value > 0 && _embeddedMeshEditor != null)
        {
            bool saved = _embeddedMeshEditor.SaveTableChanges();
            MeshSelectionText.Text = saved ? "mesh tables saved" : "mesh tables were not saved; check the viewer status";
            return saved;
        }
        if (_meshSession == null || _renderer == null) return true;
        _meshSaving = true;
        MeshSaveButton.IsEnabled = false;
        var renderer = _renderer;
        var session = _meshSession;
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        renderer.QueueMeshAction(() => { renderer.PrepareMeshSave(); ready.TrySetResult(); });
        try
        {
            MeshSelectionText.Text = "Saving mesh...";
            await ready.Task;
            await Task.Run(session.Save);
            _embeddedMeshEditor?.Close();
            _embeddedMeshEditor = null;
            MeshDock.Child = null;
            MeshDockRow.Height = new GridLength(0);
            MeshSelectionText.Text = "Mesh saved";
            GeometryChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (Exception ex) { MeshSelectionText.Text = "Mesh was not saved: " + ex.Message; return false; }
        finally { renderer.MeshSessionBusy = false; _meshSaving = false; MeshSaveButton.IsEnabled = true; }
    }

    private async void SaveMeshBeforeClosing()
    {
        if (await SaveMeshSessionAsync()) { _meshCloseApproved = true; Close(); }
    }
}
