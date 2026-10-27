using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Sm64DecompLevelViewer;

public partial class LevelEditorControlsWindow : Window
{
    public LevelEditorControlsWindow()
    {
        InitializeComponent();
        AddSection("Camera navigation",
            ("W A S D", "Move across the level"),
            ("Space / X", "Move up / down"),
            ("Shift / Ctrl", "Move faster / more precisely"),
            ("Middle drag", "Pan the camera"),
            ("Right drag", "Orbit the camera"),
            ("Mouse wheel", "Zoom in / out"),
            ("R", "Reset the camera"));
        AddSection("Objects",
            ("Left click", "Select an object in the viewport or sidebar"),
            ("Arrow keys", "Move the selected object horizontally"),
            ("Page Up / Down", "Raise / lower the selected object"),
            ("Q / E", "Rotate around the Y axis in 15 degree steps"),
            ("F1 / F2", "Copy / paste an object"),
            ("Delete", "Remove the selected object"),
            ("Restore button", "Restore the last removed object, even after saving"));
        AddSection("View and geometry",
            ("C / V", "Toggle collision / terrain visibility"),
            ("1 to 9", "Toggle individual sub-models"),
            ("0", "Show all sub-models"),
            ("Frame level", "Fit the entire area in the viewport"),
            ("Focus selected", "Center the camera on the selection"),
            ("Mesh editor", "Use the sidebar tools to select and transform geometry"));
    }

    private void AddSection(string title, params (string Keys, string Action)[] shortcuts)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
        panel.Children.Add(new TextBlock
        {
            Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x79, 0xC7, 0xF5)),
            Margin = new Thickness(0, 0, 0, 8)
        });
        foreach (var shortcut in shortcuts)
        {
            var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(152) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x30, 0x36, 0x40)),
                CornerRadius = new CornerRadius(4), Padding = new Thickness(9, 5, 9, 5),
                Margin = new Thickness(0, 0, 16, 0), HorizontalAlignment = HorizontalAlignment.Left,
                Child = new TextBlock { Text = shortcut.Keys, FontWeight = FontWeights.SemiBold, FontSize = 12 }
            });
            var description = new TextBlock { Text = shortcut.Action, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(description, 1);
            row.Children.Add(description);
            panel.Children.Add(row);
        }
        Sections.Children.Add(panel);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
