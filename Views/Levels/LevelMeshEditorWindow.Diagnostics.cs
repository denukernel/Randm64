using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using OpenTK.Mathematics;
using Sm64DecompLevelViewer.Models;
using Sm64DecompLevelViewer.Services;

namespace Sm64DecompLevelViewer;
public partial class LevelMeshEditorWindow
{
    public event Action<Vector3[]>? DiagnosticHighlight;
    private void InitializeDiagnostics()
    {
        var panel=new DockPanel(); var tools=new WrapPanel(); DockPanel.SetDock(tools,Dock.Top); panel.Children.Add(tools);
        var small=ProjectToolsUi.Input(tools,"Small area","1",50); var slope=ProjectToolsUi.Input(tools,"Slope degrees","38",50); var distance=ProjectToolsUi.Input(tools,"Visual distance","5",50);
        var filter=ProjectToolsUi.Input(tools,"Issue filter","",110);
        var status=new TextBlock { Text="Read-only inspection. Run analysis after editing. Boundary edges and visual differences can be intentional.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(6) };
        DockPanel.SetDock(status,Dock.Bottom); panel.Children.Add(status);
        var grid=ProjectToolsUi.Grid(); panel.Children.Add(grid);
        var tab=new TabItem { Header="Diagnostics",Content=panel }; MeshTabControl.Items.Add(tab);
        List<CollisionFinding> findings=new(); string baseline=""; bool running=false;
        string Signature()=>AreaComboBox.SelectedItem+"|"+string.Join(";",ColVertices.Select(v=>$"{v.X},{v.Y},{v.Z}"))+"|"+string.Join(";",ColTriangles.Select(t=>$"{t.V1},{t.V2},{t.V3},{t.SurfaceType}"))+"|"+string.Join(";",VisualVertices.Select(v=>$"{v.X},{v.Y},{v.Z}"));
        void Clear() { findings.Clear(); grid.ItemsSource=null; DiagnosticHighlight?.Invoke(Array.Empty<Vector3>()); }
        void Filter()=>grid.ItemsSource=findings.Where(f=>f.Kind.Contains(filter.Text,StringComparison.OrdinalIgnoreCase)).ToList();
        filter.TextChanged+=(_,_)=>Filter();
        var timer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(500) };
        timer.Tick+=(_,_)=> { if(baseline.Length>0 && baseline!=Signature()) { Clear(); baseline=""; status.Text="Mesh changed. Run analysis again."; } };
        Loaded+=(_,_)=>timer.Start(); Unloaded+=(_,_)=>timer.Stop();
        grid.SelectionChanged+=(_,_)=>
        {
            if(grid.SelectedItem is not CollisionFinding finding || baseline!=Signature()) return;
            if(finding.Triangle<0 || finding.Triangle>=ColTriangles.Count) return;
            var t=ColTriangles[finding.Triangle]; int[] indices={t.V1,t.V2,t.V3};
            if(indices.Any(i=>i<0 || i>=ColVertices.Count)) return;
            var points=indices.Select(i=>new Vector3(ColVertices[i].X,ColVertices[i].Y,ColVertices[i].Z)).ToArray();
            ColTriangleGrid.SelectedItem=t; ColTriangleGrid.ScrollIntoView(t);
            DiagnosticHighlight?.Invoke(points); _selectPointCallback?.Invoke((points[0]+points[1]+points[2])/3,true);
        };
        ProjectToolsUi.Button(tools,"Analyze",async ()=>
        {
            if(running) return;
            try
            {
                if(!ColVertexGrid.CommitEdit(DataGridEditingUnit.Cell,true) || !ColVertexGrid.CommitEdit(DataGridEditingUnit.Row,true) || !ColTriangleGrid.CommitEdit(DataGridEditingUnit.Cell,true) || !ColTriangleGrid.CommitEdit(DataGridEditingUnit.Row,true)) throw new InvalidOperationException("Finish invalid table edits first.");
                var settings=new CollisionDiagnosticSettings(double.Parse(small.Text,CultureInfo.InvariantCulture),double.Parse(slope.Text,CultureInfo.InvariantCulture),double.Parse(distance.Text,CultureInfo.InvariantCulture));
                var snapshot=new CollisionMesh { Vertices=ColVertices.Select(v=>new CollisionVertex(v.X,v.Y,v.Z)).ToList(),Triangles=ColTriangles.Select(t=>new CollisionTriangle(t.V1,t.V2,t.V3,t.SurfaceType)).ToList() };
                bool visualDirty=VisualArrayComboBox.SelectedItem is string array && _currentModelVertexArrays.TryGetValue(array,out var originalVertices) && (originalVertices.Count!=VisualVertices.Count || originalVertices.Where((v,i)=>i<VisualVertices.Count && (v.X!=VisualVertices[i].X || v.Y!=VisualVertices[i].Y || v.Z!=VisualVertices[i].Z)).Any());
                string before=Signature(); Clear(); running=true; status.Text="Analyzing…";
                int area=int.Parse(System.Text.RegularExpressions.Regex.Match(AreaComboBox.SelectedItem as string ?? "",@"\d+").Value);
                string visualStatus="";
                var result=await Task.Run(()=>
                {
                    VisualMesh? visual=null;
                    try { if(visualDirty) visualStatus=" Visual comparison requires saving visual vertex edits first."; else visual=LevelSceneLoader.Load(_projectRoot,_levelPath,area); }
                    catch(Exception ex) { visualStatus=" Visual comparison unavailable: "+ex.Message; }
                    return CollisionDiagnostics.Analyze(snapshot,visual,settings);
                });
                if(before!=Signature()) { status.Text="Mesh changed during analysis. Run again."; return; }
                findings=result; baseline=before; Filter(); status.Text=$"{findings.Count} findings. Select one to frame and highlight its triangle in the level viewport."+visualStatus;
            }
            catch(Exception ex) { status.Text=ex.Message; }
            finally { running=false; }
        });
    }
    public void ShowDiagnostics() => MeshTabControl.SelectedIndex=MeshTabControl.Items.Count-1;
}
