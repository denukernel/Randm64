using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Sm64DecompLevelViewer.Services.Chaos;

namespace Sm64DecompLevelViewer;

internal static class ChaosControls
{
    internal static void AddSearch(Window window, ItemsControl list, Func<object, string> text)
    {
        var header = ((Grid)window.Content).Children.OfType<StackPanel>().First();
        var search = new TextBox { Margin = new Thickness(0, 8, 0, 0), ToolTip = "Search effects by name or description" };
        search.TextChanged += (_, _) =>
        {
            var view = CollectionViewSource.GetDefaultView(list.ItemsSource);
            if (view != null) view.Filter = item => text(item).Contains(search.Text, StringComparison.OrdinalIgnoreCase);
        };
        header.Children.Add(new TextBlock { Text = "Search effects", Margin = new Thickness(0, 6, 0, 0) });
        header.Children.Add(search);
    }

    internal static Window Busy(Window owner, CancellationTokenSource cancellation)
    {
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = "Preparing or applying chaos; cancellation rolls back this entry", TextWrapping = TextWrapping.Wrap });
        var cancel = new Button { Content = "Cancel run", Margin = new Thickness(0, 12, 0, 0) };
        var window = NewWindow(owner, "Chaos run", panel, 430, 150);
        cancel.Click += (_, _) => { cancellation.Cancel(); cancel.IsEnabled = false; cancel.Content = "Cancelling..."; };
        panel.Children.Add(cancel);
        window.Closing += (_, e) => { if (!Equals(window.Tag, "finished")) { cancellation.Cancel(); e.Cancel = true; } };
        window.Show();
        return window;
    }

    internal static bool Preview(Window owner, ChaosRunPlan plan)
    {
        var panel = new DockPanel { Margin = new Thickness(12) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        DockPanel.SetDock(buttons, Dock.Bottom); panel.Children.Add(buttons);
        var apply = new Button { Content = "Apply prepared changes", Padding = new Thickness(10), Margin = new Thickness(6) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Padding = new Thickness(10), Margin = new Thickness(6) };
        buttons.Children.Add(apply); buttons.Children.Add(cancel);
        panel.Children.Add(new TextBox { Text = plan.Preview(), IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto });
        var window = NewWindow(owner, "Review prepared chaos", panel, 820, 600);
        apply.Click += (_, _) => window.DialogResult = true;
        return window.ShowDialog() == true;
    }

    internal static Dictionary<string, double>? EditIntensities(Window owner, Dictionary<string, double> current)
    {
        var panel = new StackPanel { Margin = new Thickness(15) };
        panel.Children.Add(new TextBlock { Text = "Blank values inherit global intensity. range: 0–500; M64 caps at 100.", TextWrapping = TextWrapping.Wrap });
        var inputs = new Dictionary<string, TextBox>();
        foreach (string tool in ChaosTuning.Tools)
        {
            panel.Children.Add(new TextBlock { Text = tool switch { "M64" => "M64", "HUD" => "HUD", "Goddard" => "Goddard", _ => char.ToUpperInvariant(tool[0]) + tool.Substring(1) }, Margin = new Thickness(0, 8, 0, 0) });
            var input = new TextBox { Text = current.TryGetValue(tool, out var value) ? value.ToString(CultureInfo.InvariantCulture) : "" };
            inputs.Add(tool, input); panel.Children.Add(input);
        }
        var save = new Button { Content = "Save overrides", Margin = new Thickness(0, 12, 0, 0), Padding = new Thickness(8) };
        panel.Children.Add(save);
        var window = NewWindow(owner, "Per-tool intensity", new ScrollViewer { Content = panel }, 470, 620);
        Dictionary<string, double>? result = null;
        save.Click += (_, _) =>
        {
            var values = new Dictionary<string, double>();
            foreach (var pair in inputs)
            {
                if (string.IsNullOrWhiteSpace(pair.Value.Text)) continue;
                if (!double.TryParse(pair.Value.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || !double.IsFinite(value) || value < 0 || value > 500)
                { MessageBox.Show(window, "Enter a value from 0 to 500 for " + pair.Key); return; }
                values.Add(pair.Key, value);
            }
            result = values; window.DialogResult = true;
        };
        window.ShowDialog(); return result;
    }

    internal sealed class MusicEditor
    {
        private readonly CheckBox[] _channels;
        private readonly CheckBox _percussion = new() { Content = "Protect percussion channels (including mixed channels)", Foreground = Brushes.White, Margin = new Thickness(0, 6, 0, 0) };
        private readonly CheckBox _sequence = new() { Content = "Include sequence-wide tempo and master volume", Foreground = Brushes.White, Margin = new Thickness(0, 6, 0, 0) };
        private readonly TextBox _limit = new() { Width = 70, HorizontalAlignment = HorizontalAlignment.Left };
        private readonly CheckBox _volumeFloor = new() { Content = "Keep original minimum volumes (except muting/erasing)", Foreground = Brushes.White,
            ToolTip = "Prevents volume and velocity effects from lowering original values. Turn off to allow quiet dynamics, including velocity inversion.", Margin = new Thickness(0, 6, 0, 0) };
        internal MusicEditor(StackPanel header, M64MutationSettings settings)
        {
            header.Children.Add(new TextBlock { Text = "Target channels (0–15)", Margin = new Thickness(0, 8, 0, 0) });
            var channels = new WrapPanel();
            _channels = Enumerable.Range(0, 16).Select(c => new CheckBox { Content = c.ToString(), Foreground = Brushes.White, IsChecked = settings.Channels.Contains(c), Margin = new Thickness(4) }).ToArray();
            foreach (var box in _channels) channels.Children.Add(box);
            header.Children.Add(channels);
            _percussion.IsChecked = settings.ProtectPercussion; _sequence.IsChecked = settings.IncludeSequenceControls;
            header.Children.Add(_percussion); header.Children.Add(_sequence);
            _volumeFloor.IsChecked = settings.PreventVolumeLoss;
            header.Children.Add(_volumeFloor);
            header.Children.Add(new TextBlock { Text = "Maximum pitch displacement, semitones (blank = unlimited)", Margin = new Thickness(0, 6, 0, 0) });
            _limit.Text = settings.MaxPitchDisplacement?.ToString() ?? ""; header.Children.Add(_limit);
        }
        internal M64MutationSettings Read()
        {
            int? limit = null;
            if (!string.IsNullOrWhiteSpace(_limit.Text))
            {
                if (!int.TryParse(_limit.Text, out int value) || value < 0 || value > 63) throw new InvalidOperationException("Pitch limit must be 0–63 semitones");
                limit = value;
            }
            return new M64MutationSettings { Channels = Enumerable.Range(0, 16).Where(c => _channels[c].IsChecked == true).ToList(),
                ProtectPercussion = _percussion.IsChecked == true, IncludeSequenceControls = _sequence.IsChecked == true,
                PreventVolumeLoss = _volumeFloor.IsChecked == true, MaxPitchDisplacement = limit };
        }
    }

    private static Window NewWindow(Window owner, string title, object content, double width, double height) => new()
    {
        Owner = owner, Title = title, Content = content, Width = width, Height = height,
        WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = new SolidColorBrush(Color.FromRgb(38, 38, 38)), Foreground = Brushes.White
    };
}
