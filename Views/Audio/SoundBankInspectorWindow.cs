using System.IO;
using System.Windows;
using System.Windows.Controls;
using Sm64DecompLevelViewer.Services;

namespace Sm64DecompLevelViewer;
public sealed class SoundBankInspectorWindow : Window
{
    private readonly string root;
    private readonly ComboBox sequence=new() { Width=235,Margin=new Thickness(4),DisplayMemberPath="Name" };
    private readonly ComboBox bank=new() { Width=180,Margin=new Thickness(4) };
    private readonly ComboBox region=new() { Width=65,Margin=new Thickness(4),ItemsSource=new[]{"us","jp","eu","sh","cn"},SelectedIndex=0 };
    private readonly DataGrid grid=ProjectToolsUi.Grid();
    private readonly TextBlock details=new() { TextWrapping=TextWrapping.Wrap,Margin=new Thickness(5) };
    private System.Media.SoundPlayer? player; private MemoryStream? stream;
    private bool loading;
    public SoundBankInspectorWindow(string projectRoot,string? initialSequence=null)
    {
        root=ProjectObjectIndex.Root(projectRoot); var panel=ProjectToolsUi.Setup(this,"Sound bank inspector");
        var tools=new WrapPanel(); DockPanel.SetDock(tools,Dock.Top); panel.Children.Add(tools);
        tools.Children.Add(new TextBlock { Text="Sequence",VerticalAlignment=VerticalAlignment.Center }); tools.Children.Add(sequence);
        tools.Children.Add(new TextBlock { Text="Bank",VerticalAlignment=VerticalAlignment.Center }); tools.Children.Add(bank); tools.Children.Add(region);
        ProjectToolsUi.Button(tools,"Refresh",()=>Refresh(initialSequence)); ProjectToolsUi.Button(tools,"Raw preview at file rate",Play); ProjectToolsUi.Button(tools,"Stop",Stop);
        ProjectToolsUi.Button(tools,"All banks",()=>
        {
            loading=true;
            try { sequence.SelectedItem=null; bank.ItemsSource=Directory.GetFiles(Path.Combine(root,"sound","sound_banks"),"*.json").Select(Path.GetFileNameWithoutExtension).OrderBy(s=>s).ToList(); bank.SelectedIndex=0; }
            finally { loading=false; }
            LoadBank();
        });
        DockPanel.SetDock(details,Dock.Bottom); panel.Children.Add(details); panel.Children.Add(grid);
        grid.SelectionChanged+=(_,_)=>Describe();
        grid.AutoGeneratingColumn+=(_,e)=> { if(e.PropertyName is "SamplePath" or "Source" or "Envelope") e.Cancel=true; };
        sequence.SelectionChanged+=(_,_)=>
        {
            if(loading || sequence.SelectedItem is not FileInfo file) return;
            ProjectToolsUi.Guard(()=>
            {
                loading=true;
                try { region.SelectedItem=Sm64SoundBank.GetVersionForSequence(file.FullName); bank.ItemsSource=SequenceBankInspection.Banks(root,file.FullName); bank.SelectedIndex=0; }
                finally { loading=false; }
                LoadBank();
            });
        };
        bank.SelectionChanged+=(_,_)=> { if(!loading) LoadBank(); }; region.SelectionChanged+=(_,_)=> { if(!loading) LoadBank(); };
        Loaded+=(_,_)=>Refresh(initialSequence); Closed+=(_,_)=>Stop();
    }
    private void Refresh(string? initialSequence)
    {
        try
        {
            loading=true;
            string folder=Path.Combine(root,"sound","sequences");
            var files=Directory.Exists(folder) ? Directory.GetFiles(folder,"*.m64",SearchOption.AllDirectories).Select(p=>new FileInfo(p)).ToList() : new();
            sequence.ItemsSource=files;
            sequence.SelectedItem=files.FirstOrDefault(f=>string.Equals(f.FullName,initialSequence,StringComparison.OrdinalIgnoreCase));
            string banks=Path.Combine(root,"sound","sound_banks");
            bank.ItemsSource=Directory.Exists(banks) ? Directory.GetFiles(banks,"*.json").Select(Path.GetFileNameWithoutExtension).OrderBy(s=>s).ToList() : new();
            bank.SelectedIndex=0;
            if(sequence.SelectedItem is FileInfo file) { region.SelectedItem=Sm64SoundBank.GetVersionForSequence(file.FullName); bank.ItemsSource=SequenceBankInspection.Banks(root,file.FullName); bank.SelectedIndex=0; }
        }
        catch(Exception ex) { details.Text=ex.Message; }
        finally { loading=false; }
        LoadBank();
    }
    private void LoadBank()
    {
        Stop(); grid.ItemsSource=null;
        try
        {
            if(bank.SelectedItem is string id) { grid.ItemsSource=new Sm64SoundBank(root).Inspect(id,region.SelectedItem as string ?? "us"); details.Text="Read-only resolved bank metadata. Sequence mappings can contain several banks; the first is the default."; }
        }
        catch(Exception ex) { details.Text=ex.Message; }
    }
    private void Describe()
    {
        if(grid.SelectedItem is not SoundBankInspectionEntry entry) return;
        details.Text=entry.Source+"\n"+entry.SamplePath+"\nTuning: "+entry.Tuning+"; pan: "+entry.Pan+"; release rate: "+entry.ReleaseRate+"\nEnvelope: "+entry.Envelope+"\n"+entry.Warning;
        if(File.Exists(entry.SamplePath))
            try { details.Text+="\n"+SoundSampleInfo.Describe(AiffWavTranscoder.SafeReadAllBytes(entry.SamplePath)); }
            catch(Exception ex) { details.Text+="\nSample metadata unavailable: "+ex.Message; }
    }
    private void Play()
    {
        Stop();
        if(grid.SelectedItem is not SoundBankInspectionEntry entry) return;
        byte[] bytes=AiffWavTranscoder.SafeReadAllBytes(entry.SamplePath); PcmSampleFile.AudioRange(bytes);
        stream=new MemoryStream(AiffWavTranscoder.ConvertAiffToWav(bytes)); player=new(stream); player.Play();
    }
    private void Stop() { player?.Stop(); player?.Dispose(); player=null; stream?.Dispose(); stream=null; }
}
