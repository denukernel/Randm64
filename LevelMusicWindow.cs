using System.IO;
using System.Windows;
using System.Windows.Controls;
using Sm64DecompLevelViewer.Services;

namespace Sm64DecompLevelViewer;

public sealed class LevelMusicWindow : Window
{
    public LevelMusicWindow(string root,string directory,int area)
    {
        Title="Level and intro music"; Width=620; Height=440; MinHeight=440; MinWidth=500; WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var service=new LevelMusicService(root,directory,area);
        var panel=new StackPanel { Margin=new Thickness(20) }; Content=panel;
        panel.Children.Add(new TextBlock { Text="Level and intro music",FontSize=20,Margin=new Thickness(0,0,0,14) });
        panel.Children.Add(new TextBlock { Text="Music event" });
        var bindings=new ComboBox { ItemsSource=service.LoadBindings(),Margin=new Thickness(0,5,0,12) }; panel.Children.Add(bindings);
        var current=new TextBlock { TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12) }; panel.Children.Add(current);
        panel.Children.Add(new TextBlock { Text="Sequence" });
        var sequences=new ComboBox { ItemsSource=service.Sequences,Margin=new Thickness(0,5,0,12) }; panel.Children.Add(sequences);
        var buttons=new StackPanel { Orientation=Orientation.Horizontal }; panel.Children.Add(buttons);
        var save=new Button { Content="Save / add music",Padding=new Thickness(12,6,12,6) }; buttons.Children.Add(save);
        var edit=new Button { Content="Edit sequence notes...",Padding=new Thickness(12,6,12,6),Margin=new Thickness(10,0,0,0) }; buttons.Children.Add(edit);
        panel.Children.Add(new TextBlock { Text="Background music applies to this area. intro cues use shared game functions. editing sequence notes affects every event using that sequence.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,14,0,0) });
        var status=new TextBlock { TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,0) }; panel.Children.Add(status);
        bindings.SelectionChanged+=(_,_)=>
        {
            if(bindings.SelectedItem is not LevelMusicBinding binding) return;
            current.Text="Current: "+(binding.Expression ?? "No area music command; Save adds one");
            sequences.SelectedItem=service.Resolve(binding.Expression) ?? service.Sequences.FirstOrDefault();
        };
        save.Click+=(_,_)=>
        {
            if(bindings.SelectedItem is not LevelMusicBinding binding || sequences.SelectedItem is not MusicSequenceChoice sequence) return;
            try
            {
                service.Save(binding,sequence); string label=binding.Label;
                bindings.ItemsSource=service.LoadBindings(); bindings.SelectedItem=bindings.Items.OfType<LevelMusicBinding>().First(b=>b.Label==label);
                status.Text="Music assignment saved; rebuild the ROM to hear the change";
            }
            catch(Exception ex) { status.Text="Music was not saved: "+ex.Message; }
        };
        edit.Click+=(_,_)=>
        {
            if(sequences.SelectedItem is not MusicSequenceChoice choice || choice.FilePath==null || !File.Exists(choice.FilePath)) { status.Text="This sequence has no extracted M64 file to edit"; return; }
            new MusicEditorWindow(root,choice.FilePath) { Owner=this }.Show();
        };
        bindings.SelectedIndex=0;
    }
}
