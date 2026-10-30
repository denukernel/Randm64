using System.IO;
using System.Windows;
using System.Windows.Controls;
using Sm64DecompLevelViewer.Services;

namespace Sm64DecompLevelViewer;
public sealed class ProjectObjectBrowserWindow : Window
{
    private readonly string root;
    private readonly bool objectives;
    private readonly string? level;
    private readonly DataGrid grid=ProjectToolsUi.Grid();
    private readonly TextBlock status=new() { TextWrapping=TextWrapping.Wrap,Margin=new Thickness(5) };
    private readonly TextBlock detail=new() { TextWrapping=TextWrapping.Wrap,Margin=new Thickness(5) };
    private readonly TextBox query,levelFilter,areaFilter,typeFilter,star,acts;
    private readonly Button save;
    private readonly CheckBox showAll=new() { Content="Include custom / other objects",Margin=new Thickness(6),Foreground=System.Windows.Media.Brushes.Gainsboro };
    private List<ProjectObjectEntry> entries=new();
    private CancellationTokenSource? scan;
    private ProjectObjectEntry? selected => grid.SelectedItem as ProjectObjectEntry;
    public ProjectObjectBrowserWindow(string projectRoot,bool objectiveMode=false,string? levelPath=null)
    {
        root=ProjectObjectIndex.Root(projectRoot); objectives=objectiveMode; level=levelPath;
        var panel=ProjectToolsUi.Setup(this,objectives ? "Star and objective editor" : "Project object search");
        var tools=new WrapPanel(); DockPanel.SetDock(tools,Dock.Top); panel.Children.Add(tools);
        ProjectToolsUi.Button(tools,"Refresh",()=>_ = Refresh()); ProjectToolsUi.Button(tools,"Cancel",()=>scan?.Cancel());
        ProjectToolsUi.Button(tools,"Open object",()=> { if(selected is {} entry) ProjectObjectNavigator.Open(root,entry); });
        ProjectToolsUi.Button(tools,"Open source",()=> { if(selected is {} entry) ProjectObjectNavigator.Source(entry); });
        ProjectToolsUi.Button(tools,"Behavior source",()=>
        {
            if(selected is not {} entry) return;
            string path=Path.Combine(root,"data","behavior_data.c");
            string source=File.ReadAllText(path);
            var match=System.Text.RegularExpressions.Regex.Match(source,@"\b"+System.Text.RegularExpressions.Regex.Escape(entry.Behavior)+@"\s*\[\s*\]");
            if(!match.Success) throw new InvalidDataException("Behavior declaration was not found in data/behavior_data.c. The object source remains available.");
            ProjectObjectNavigator.SourceFile(path,match.Index,match.Length);
        });
        if(objectives) ProjectToolsUi.Button(tools,"Related candidates",()=>
        {
            if(selected is not {} entry) return;
            string related=ObjectiveCatalog.Kind(entry.Behavior) switch { "red coins"=>"bhvRedCoin","secrets"=>"bhvHiddenStarTrigger","race controller"=>entry.Behavior=="bhvKoopa" ? "bhvKoopaRaceEndpoint" : "bhvPenguinRaceFinishLine",_=>"" };
            if(related.Length==0) throw new InvalidDataException("No verified related-object rule for this behavior.");
            var browser=new ProjectObjectBrowserWindow(root) { Owner=this }; browser.query.Text=related; browser.levelFilter.Text=entry.LevelName; browser.areaFilter.Text=entry.Area.ToString(); browser.Show();
        });
        var filters=new WrapPanel(); DockPanel.SetDock(filters,Dock.Top); panel.Children.Add(filters);
        query=ProjectToolsUi.Input(filters,"Search","",200); levelFilter=ProjectToolsUi.Input(filters,"Level","",90);
        areaFilter=ProjectToolsUi.Input(filters,"Area","",55); typeFilter=ProjectToolsUi.Input(filters,"Type","",85);
        foreach(var box in new[]{query,levelFilter,areaFilter,typeFilter}) box.TextChanged+=(_,_)=>Filter();
        if(objectives) { filters.Children.Add(showAll); showAll.Checked+=(_,_)=>Filter(); showAll.Unchecked+=(_,_)=>Filter(); }
        DockPanel.SetDock(status,Dock.Bottom); panel.Children.Add(status);
        var editing=new WrapPanel(); DockPanel.SetDock(editing,Dock.Bottom); panel.Children.Add(editing);
        star=ProjectToolsUi.Input(editing,"Star index (0 to 6)","",60); acts=ProjectToolsUi.Input(editing,"Acts (1 to 6, comma separated)","",160);
        save=ProjectToolsUi.Button(editing,"Apply and save",Save); editing.Visibility=objectives ? Visibility.Visible : Visibility.Collapsed;
        DockPanel.SetDock(detail,Dock.Bottom); panel.Children.Add(detail); panel.Children.Add(grid);
        grid.AutoGeneratingColumn+=(_,e)=>
        {
            if(e.PropertyName is "Level" or "Object" or "Reference" or "CoordinatesResolved") e.Cancel=true;
            if(e.PropertyName=="StarIndex") { e.Column.Header="Star index"; if(!objectives) e.Cancel=true; }
            if(e.PropertyName=="Source") { e.Column.Width=180; e.Column.Header="Source file and offset"; }
        };
        grid.SelectionChanged+=(_,_)=>Selection();
        Loaded+=async (_,_)=>await Refresh(); Closed+=(_,_)=>scan?.Cancel();
    }
    private async Task Refresh()
    {
        scan?.Cancel(); var current=new CancellationTokenSource(); scan=current; save.IsEnabled=false; grid.ItemsSource=null; entries.Clear();
        var errors=new List<string>();
        try
        {
            var result=await Task.Run(()=>ProjectObjectIndex.Scan(root,current.Token,new Progress<string>(s=>Dispatcher.InvokeAsync(()=>status.Text="Scanning "+s)),errors,objectives ? level : null));
            if(scan!=current || !IsLoaded) return;
            entries=result; Filter(); status.Text=$"{entries.Count} placements. "+string.Join("\n",errors);
        }
        catch(OperationCanceledException) { if(scan==current) status.Text="Scan cancelled. Refresh to scan again."; }
        catch(Exception ex) { status.Text=ex.Message; }
        finally { current.Dispose(); if(scan==current) scan=null; }
    }
    private void Filter()
    {
        if(query==null) return;
        grid.ItemsSource=entries.Where(e=>(e.Behavior+" "+e.Model+" "+e.Preset+" "+e.Parameters).Contains(query.Text,StringComparison.OrdinalIgnoreCase)
            && e.LevelName.Contains(levelFilter.Text,StringComparison.OrdinalIgnoreCase) && (areaFilter.Text.Length==0 || e.Area.ToString()==areaFilter.Text)
            && e.Type.Contains(typeFilter.Text,StringComparison.OrdinalIgnoreCase)
            && (!objectives || showAll.IsChecked==true || ObjectiveCatalog.Kind(e.Behavior)!="custom / source reference" || e.Behavior.Contains("star",StringComparison.OrdinalIgnoreCase))).ToList();
    }
    private void Selection()
    {
        save.IsEnabled=false; star.IsEnabled=false; acts.IsEnabled=false;
        if(selected is not {} e) { detail.Text="Select an object to inspect its source and available fields."; star.Text=""; acts.Text=""; return; }
        var info=ObjectiveCatalog.Describe(root,e);
        detail.Text=$"{info.Kind}\n{info.Requirements}\n{info.Evidence}\n{e.Source}";
        var shared=entries.Where(other=>other.Reference.SourceFile==e.Reference.SourceFile && other.Reference.Start==e.Reference.Start).Select(other=>other.LevelName+"/"+other.Area).Distinct().ToList();
        if(shared.Count>1) detail.Text+="\nShared source: edits affect placements in "+string.Join(", ",shared);
        if(info.Kind is "red coins" or "secrets")
        {
            string related=info.Kind=="red coins" ? "bhvRedCoin" : "bhvHiddenStarTrigger";
            detail.Text+="\nCandidate related objects in area: "+entries.Count(o=>o.LevelName==e.LevelName && o.Area==e.Area && o.Behavior==related);
        }
        star.Text=info.CanEditStar && ObjectiveCatalog.TryParameters(e.Parameters,out uint parameter) ? (parameter>>24).ToString() : "";
        star.IsEnabled=info.CanEditStar;
        bool known=e.Object.RawActs==null || System.Text.RegularExpressions.Regex.IsMatch(e.Acts,@"^\s*(ACT_[1-6]\s*(\|\s*ACT_[1-6]\s*)*|ALL_ACTS)\s*$");
        acts.Text=e.Object.RawActs==null || e.Acts.Trim()=="ALL_ACTS" ? "1,2,3,4,5,6" : string.Join(",",System.Text.RegularExpressions.Regex.Matches(e.Acts,@"ACT_([1-6])").Select(m=>m.Groups[1].Value));
        acts.IsEnabled=e.Object.SourceType==Models.ObjectSourceType.Normal && known;
        save.IsEnabled=objectives && (star.IsEnabled || acts.IsEnabled);
    }
    private void Save()
    {
        if(selected is not {} e) return;
        ProjectObjectIndex.Validate(e.Reference);
        if(ProjectObjectNavigator.HasOpenSource(e.Reference.SourceFile)) throw new InvalidDataException("Close level editors using this source before saving objective changes, or edit the object there.");
        var saver=new LevelSaver(); saver.CaptureSources(new[]{e.Object});
        var oldParams=e.Object.Params; var oldRaw=e.Object.RawParams; var oldActs=e.Object.RawActs;
        try
        {
            if(star.IsEnabled)
            {
                if(!uint.TryParse(star.Text,out uint index) || index>6) throw new InvalidDataException("Star index must be between 0 and 6.");
                ObjectiveCatalog.TryParameters(e.Parameters,out uint value);
                if((value>>24)!=index) { e.Object.Params=(value&0x00FFFFFF)|(index<<24); e.Object.RawParams=$"0x{e.Object.Params:X8}"; }
            }
            if(acts.IsEnabled)
            {
                string[] values=acts.Text.Split(',',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries);
                if(values.Length==0 || values.Any(v=>!int.TryParse(v,out int n) || n<1 || n>6)) throw new InvalidDataException("Enter act numbers 1 to 6 separated by commas.");
                string expression=string.Join(" | ",values.Distinct().OrderBy(v=>v).Select(v=>"ACT_"+v));
                var original=e.Object.RawActs==null || e.Object.RawActs.Trim()=="ALL_ACTS" ? new[]{"1","2","3","4","5","6"} : System.Text.RegularExpressions.Regex.Matches(e.Acts,@"ACT_([1-6])").Select(m=>m.Groups[1].Value).Distinct().OrderBy(v=>v).ToArray();
                if(!original.SequenceEqual(values.Distinct().OrderBy(v=>v))) e.Object.RawActs=expression;
            }
            if(!saver.SaveLevel(new(){e.Object})) throw new InvalidDataException(saver.LastError);
            _=Refresh();
        }
        catch { e.Object.Params=oldParams; e.Object.RawParams=oldRaw; e.Object.RawActs=oldActs; throw; }
    }
}
