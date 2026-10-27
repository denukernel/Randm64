using System.Windows;
using System.Windows.Threading;
using Sm64DecompLevelViewer.Services.Levels;

namespace Sm64DecompLevelViewer;
public partial class LevelEditorWindow
{
    private Dictionary<Sm64DecompLevelViewer.Models.LevelObject, int[]> _liveOriginals = new();
    private KernelLiveBridge? _liveBridge;
    private DispatcherTimer? _liveTimer;
    private bool _liveBusy;
    private string? _liveLast;
    private async void LiveEditing_Click(object sender, RoutedEventArgs e)
    {
        if (_liveBridge != null) { StopLiveEditing(); return; }
        var picker = new Microsoft.Win32.OpenFileDialog { Title = "Select the Active Decomp Build ROM", Filter = "Big Endian ROM (*.z64)|*.z64", InitialDirectory = System.IO.Path.Combine(_projectRoot, "build", "us") };
        if (picker.ShowDialog(this) != true) return;
        try
        {
            _liveBridge = new KernelLiveBridge(_projectRoot, picker.FileName, _liveOriginals);
            LiveEditingButton.Content = "Disconnect Live Editing";
            _liveLast = null;
            _liveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            _liveTimer.Tick += LiveEditingTick;
            _liveTimer.Start();
            Closed += (_, _) => StopLiveEditing();
            await PushLiveEditing();
        }
        catch (Exception ex) { StopLiveEditing(); MessageBox.Show(this, ex.Message, "Live Editing"); }
    }
    private void StopLiveEditing()
    {
        _liveTimer?.Stop(); _liveTimer = null; _liveBridge = null; _liveLast = null;
        LiveEditingButton.Content = "Connect Live Editing";
    }
    private async void LiveEditingTick(object? sender, EventArgs e) => await PushLiveEditing();
    private async Task PushLiveEditing()
    {
        var bridge = _liveBridge; var obj = _selectedObject;
        if (_liveBusy || bridge == null || obj == null || _isClosing) return;
        string key = $"{_objects.IndexOf(obj)}:{obj.X}:{obj.Y}:{obj.Z}:{obj.RX}:{obj.RY}:{obj.RZ}";
        if (key == _liveLast) return;
        _liveBusy = true;
        try
        {
            await bridge.SendAsync(obj, _areaIndex);
            if (_liveBridge == bridge) { _liveLast = key; LiveEditingButton.ToolTip = "Transform applied in KernelN64"; }
        }
        catch (Exception ex)
        {
            if (_liveBridge == bridge) { StopLiveEditing(); LiveEditingButton.ToolTip = ex.Message; MessageBox.Show(this, ex.Message, "Live Editing Stopped"); }
        }
        finally { _liveBusy = false; }
    }
}
