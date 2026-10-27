using System.IO;
using System.Windows;
using System.Windows.Controls;
using Sm64DecompLevelViewer.Services;

namespace Sm64DecompLevelViewer;

public partial class LevelEditorWindow
{
    private IntroActorService? _introActors;
    private void InitializeIntroActors()
    {
        if (_areaIndex == 1 && Path.GetFileName(_visualScene?.LevelDirectory) == "castle_grounds")
        {
            try { _introActors = new IntroActorService(_projectRoot); }
            catch (Exception ex) { PeachPositionButton.ToolTip = ex.Message; }
        }
        PeachPositionButton.IsEnabled = IntroActorsCheckBox.IsEnabled = _introActors != null;
    }
    private void RefreshIntroActors()
    {
        if (_renderer == null) return;
        _renderer.ShowIntroActors = _introActors != null && SidebarTabControl.SelectedIndex == 1 && IntroActorsCheckBox.IsChecked == true;
        if (_introActors != null)
            _renderer.IntroPreviewActors = _introActors.Preview(_introActors.ReferenceCamera.Position, _introActors.ReferenceCamera.Focus);
    }
    private void IntroTab_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, SidebarTabControl)) return;
        if (_previewTimer != null) StopPathPreview_Click(this, new RoutedEventArgs());
        RefreshIntroActors();
    }
    private void IntroPreview_Changed(object sender, RoutedEventArgs e) => RefreshIntroActors();
    private void PeachPosition_Click(object sender, RoutedEventArgs e)
    {
        if (_introActors == null) return;
        new IntroActorsWindow(_introActors, RefreshIntroActors, p => _renderer?.FocusOnPoint(p)) { Owner = this }.Show();
    }
    private void LevelMusic_Click(object sender, RoutedEventArgs e)
    {
        if (_visualScene?.LevelDirectory == null) return;
        try { new LevelMusicWindow(_projectRoot, _visualScene.LevelDirectory, _areaIndex) { Owner = this }.Show(); }
        catch (Exception ex) { MessageBox.Show("Cannot load level music: " + ex.Message, "Level music"); }
    }
}
