using System.IO;
using System.Windows;
using System.Windows.Controls;
using OpenTK.Mathematics;
using Sm64DecompLevelViewer.Services;
using Sm64DecompLevelViewer.Services.Levels;

namespace Sm64DecompLevelViewer;
internal static class ProjectObjectNavigator
{
    public static bool HasOpenSource(string path) => Application.Current.Windows.OfType<LevelEditorWindow>().Any(w=>w.ContainsSource(path));
    public static void Source(ProjectObjectEntry entry)
    {
        ProjectObjectIndex.Validate(entry.Reference);
        SourceFile(entry.Reference.SourceFile,entry.Reference.Start,entry.Reference.Length);
    }
    public static void SourceFile(string path,int start,int length)
    {
        var box=new TextBox { Text=File.ReadAllText(path),IsReadOnly=true,AcceptsReturn=true,AcceptsTab=true,
            FontFamily=new System.Windows.Media.FontFamily("Consolas"),VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto };
        var window=new Window { Title=path,Width=1000,Height=650,Content=box };
        window.Loaded+=(_,_)=> { box.Select(start,length); box.ScrollToLine(box.GetLineIndexFromCharacterIndex(start)); };
        window.Show();
    }
    public static void Open(string root,ProjectObjectEntry entry)
    {
        ProjectObjectIndex.Validate(entry.Reference);
        if(!entry.CoordinatesResolved) { Source(entry); return; }
        foreach(var existing in Application.Current.Windows.OfType<LevelEditorWindow>())
            if(existing.NavigateObject(entry.Reference)) { existing.Activate(); return; }
        try
        {
            var files=new CollisionParser().FindCollisionFiles(entry.Level.LevelPath);
            string area="Area "+entry.Area;
            string collision=files.GetValueOrDefault(area) ?? throw new InvalidDataException("No collision source for this area. Use Open source.");
            var mesh=new CollisionParser().ParseCollisionFile(collision,area,entry.Level.ShortName) ?? throw new InvalidDataException("Collision could not be loaded.");
            var visual=LevelSceneLoader.Load(root,entry.Level.LevelPath,entry.Area);
            var objects=LevelEditorLaunchData.LoadObjects(entry.Level,root,area,collision,entry.Area,new MacroObjectParser());
            var editor=new LevelEditorWindow(objects,mesh,visual,root,entry.Area,new ObjectParser().ParseSupportedModels(entry.Level.LevelPath,root),new SettingsService().LoadSettings(),collision);
            editor.Show(); editor.NavigateObject(entry.Reference);
        }
        catch(Exception ex) { MessageBox.Show(ex.Message+"\nOpening the source reference instead.","Object navigation"); Source(entry); }
    }
}
public partial class LevelEditorWindow
{
    private ObjectNavigationReference? pendingObjectNavigation;
    private bool objectNavigationReady;
    private void ObjectivesTool_Click(object sender,RoutedEventArgs e) => new ProjectObjectBrowserWindow(_projectRoot,true,_visualScene?.LevelDirectory) { Owner=this }.Show();
    private void ObjectSearchTool_Click(object sender,RoutedEventArgs e) => new ProjectObjectBrowserWindow(_projectRoot) { Owner=this }.Show();
    private void SourceEditorTool_Click(object sender,RoutedEventArgs e) => new SourceEditorWindow(_projectRoot) { Owner=this }.Show();
    internal bool ContainsSource(string path) => _objects.Any(o=>string.Equals(o.SourceFile,path,StringComparison.OrdinalIgnoreCase));
    internal bool NavigateObject(ObjectNavigationReference reference)
    {
        if(_areaIndex!=reference.Area) return false;
        int index=_objects.FindIndex(o=>!o.IsDeleted && o.SourceType==reference.Type && o.SourceIndex==reference.Start && string.Equals(o.SourceFile,reference.SourceFile,StringComparison.OrdinalIgnoreCase));
        if(index<0) return false;
        SelectObjectInUI(index);
        if(!objectNavigationReady) { pendingObjectNavigation=reference; return true; }
        _renderer?.SelectObject(index);
        var obj=_objects[index]; _renderer?.FocusOnPoint(new Vector3(obj.X,obj.Y,obj.Z)); return true;
    }
}
