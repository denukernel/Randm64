using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Sm64DecompLevelViewer;
internal static class ProjectToolsUi
{
    public static DockPanel Setup(Window window,string title)
    {
        window.Title=title; window.Width=1120; window.Height=740; window.MinWidth=760; window.MinHeight=480;
        window.Background=new SolidColorBrush(Color.FromRgb(30,30,30)); window.Foreground=Brushes.Gainsboro;
        window.WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var panel=new DockPanel { Margin=new Thickness(12),Background=window.Background }; window.Content=panel; return panel;
    }
    public static Button Button(Panel parent,string text,Action action)
    {
        var button=new Button { Content=text,Margin=new Thickness(3),Padding=new Thickness(9,5,9,5) };
        button.Click+=(_,_)=> { try { action(); } catch(Exception ex) { MessageBox.Show(ex.Message,text); } }; parent.Children.Add(button); return button;
    }
    public static TextBox Input(Panel parent,string label,string value,int width=150)
    {
        parent.Children.Add(new TextBlock { Text=label,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(6) });
        var input=new TextBox { Text=value,Width=width,Margin=new Thickness(3),VerticalContentAlignment=VerticalAlignment.Center }; parent.Children.Add(input); return input;
    }
    public static DataGrid Grid()
    {
        var grid=new DataGrid { IsReadOnly=true,AutoGenerateColumns=true,CanUserAddRows=false,SelectionMode=DataGridSelectionMode.Single,
            Background=new SolidColorBrush(Color.FromRgb(30,30,30)),RowBackground=new SolidColorBrush(Color.FromRgb(37,37,38)),AlternatingRowBackground=new SolidColorBrush(Color.FromRgb(45,45,48)),Foreground=Brushes.Gainsboro,EnableRowVirtualization=true,EnableColumnVirtualization=true,RowHeaderWidth=0,MaxColumnWidth=260,GridLinesVisibility=DataGridGridLinesVisibility.Horizontal };
        var header=new Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader));
        header.Setters.Add(new Setter(Control.BackgroundProperty,new SolidColorBrush(Color.FromRgb(55,55,58))));
        header.Setters.Add(new Setter(Control.ForegroundProperty,Brushes.White)); header.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(7,5,7,5))); grid.ColumnHeaderStyle=header;
        return grid;
    }
    public static void Guard(Action action) { try { action(); } catch(Exception ex) { MessageBox.Show(ex.Message,"Project tools"); } }
}
