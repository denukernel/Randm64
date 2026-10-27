using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OpenTK.Mathematics;
using Sm64DecompLevelViewer.Services;

namespace Sm64DecompLevelViewer;

public sealed class IntroActorsWindow : Window
{
    public IntroActorsWindow(IntroActorService service, Action updated, Action<Vector3> focus)
    {
        Title="Intro actor position"; Width=420; Height=350; ResizeMode=ResizeMode.NoResize; WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var panel=new StackPanel { Margin=new Thickness(18) }; Content=panel;
        panel.Children.Add(new TextBlock { Text="Peach position at intro start",FontSize=18,Margin=new Thickness(0,0,0,12) });
        panel.Children.Add(new TextBlock { Text="The game keeps Peach relative to the intro camera. Lakitu follows its source spline for preview.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12) });
        var fields=new List<TextBox>(); var position=service.PeachWorldPosition;
        foreach(var pair in new[]{("x",position.X),("y",position.Y),("z",position.Z)})
        {
            var row=new DockPanel { Margin=new Thickness(0,3,0,3) }; row.Children.Add(new TextBlock { Text=pair.Item1,Width=28 });
            var input=new TextBox { Text=pair.Item2.ToString("0.###",CultureInfo.InvariantCulture) }; row.Children.Add(input); fields.Add(input); panel.Children.Add(row);
        }
        var status=new TextBlock { TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0) };
        var buttons=new StackPanel { Orientation=Orientation.Horizontal,Margin=new Thickness(0,8,0,0) }; panel.Children.Add(buttons);
        var save=new Button { Content="Save Peach position",Padding=new Thickness(10,5,10,5) }; buttons.Children.Add(save);
        save.Click+=(_,_)=>
        {
            try
            {
                var values=fields.Select(f=>float.Parse(f.Text,CultureInfo.InvariantCulture)).ToArray();
                service.SavePeachPosition(new Vector3(values[0],values[1],values[2])); updated(); status.Text="Peach position saved to game source";
            }
            catch(Exception ex) { status.Text="Position was not saved: "+ex.Message; }
        };
        var frame=new Button { Content="Focus Peach",Padding=new Thickness(10,5,10,5),Margin=new Thickness(8,0,0,0) }; buttons.Children.Add(frame);
        frame.Click+=(_,_)=>focus(service.PeachWorldPosition);
        panel.Children.Add(status);
    }
}
