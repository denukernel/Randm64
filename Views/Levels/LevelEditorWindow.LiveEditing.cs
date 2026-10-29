using System.Windows;
using System.Windows.Threading;
using Sm64DecompLevelViewer.Services.Levels;

namespace Sm64DecompLevelViewer;
public partial class LevelEditorWindow
{
    private Dictionary<Sm64DecompLevelViewer.Models.LevelObject, LiveObjectSnapshot> _liveOriginals = new();
    private KernelLiveBridge? _liveBridge;
    private DispatcherTimer? _liveTimer;
    private bool _liveBusy;
    private string? _liveLast;
    private DateTime _liveRetryAfter;
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
            _liveRetryAfter = DateTime.MinValue;
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
        _liveTimer?.Stop(); _liveTimer = null; _liveBridge = null; _liveLast = null; _liveRetryAfter = DateTime.MinValue;
        LiveEditingButton.Content = "Connect Live Editing";
        LiveEditingButton.ToolTip = null;
        LiveEditingStatusText.Text = "";
        LiveEditingStatusText.Visibility = Visibility.Collapsed;
    }
    private async void LiveEditingTick(object? sender, EventArgs e) => await PushLiveEditing();
    private async Task PushLiveEditing()
    {
        var bridge = _liveBridge; var obj = _selectedObject;
        if (_liveBusy || bridge == null || _isClosing || DateTime.UtcNow < _liveRetryAfter) return;
        if (bridge.SelectionRestriction(obj) is { } restriction)
        {
            _liveLast = null;
            _liveRetryAfter = DateTime.MinValue;
            LiveEditingStatusText.Text = "Live Editing Enabled: " + restriction;
            LiveEditingStatusText.Visibility = Visibility.Visible;
            LiveEditingButton.ToolTip = restriction + " Live editing remains enabled. Click to stop.";
            return;
        }
        if (obj == null) return;
        string key = $"{_objects.IndexOf(obj)}:{obj.X}:{obj.Y}:{obj.Z}:{obj.RX}:{obj.RY}:{obj.RZ}";
        if (key == _liveLast) return;
        _liveBusy = true;
        try
        {
            await bridge.SendAsync(obj, _areaIndex);
            if (_liveBridge == bridge && ReferenceEquals(_selectedObject, obj))
            {
                _liveLast = key;
                _liveRetryAfter = DateTime.MinValue;
                LiveEditingButton.Content = "Disconnect Live Editing";
                LiveEditingStatusText.Text = "Live Editing: Transform Applied in KernelN64";
                LiveEditingStatusText.Visibility = Visibility.Visible;
                LiveEditingButton.ToolTip = "Transform applied in KernelN64. Click to disconnect.";
            }
        }
        catch (LiveBridgeUnavailableException ex)
        {
            if (_liveBridge == bridge)
            {
                _liveLast = null;
                _liveRetryAfter = DateTime.UtcNow.AddSeconds(1);
                LiveEditingButton.Content = "Stop Live Editing";
                LiveEditingStatusText.Text = ex.Status;
                LiveEditingStatusText.Visibility = Visibility.Visible;
                LiveEditingButton.ToolTip = ex.Message;
            }
        }
        catch (Exception ex)
        {
            if (_liveBridge == bridge) { StopLiveEditing(); LiveEditingButton.ToolTip = ex.Message; MessageBox.Show(this, ex.Message, "Live Editing Stopped"); }
        }
        finally { _liveBusy = false; }
    }
}
