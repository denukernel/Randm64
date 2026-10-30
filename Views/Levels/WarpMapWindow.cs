using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Sm64DecompLevelViewer.Services;

namespace Sm64DecompLevelViewer;
public sealed class WarpMapWindow : Window
{
    private readonly string root;
    private WarpGraphData data=new(new(),new(),new());
    private readonly Canvas canvas=new() { Background=new SolidColorBrush(Color.FromRgb(35,38,43)) };
    private readonly ScrollViewer scroll=new() { HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Auto };
    private readonly ListBox connections=new() { DisplayMemberPath="Label" };
    private readonly TextBlock details=new() { TextWrapping=TextWrapping.Wrap,Margin=new Thickness(6) };
    private readonly TextBox filter;
    private double scale=1;
    private Point? drag; private double dragX,dragY;
    private readonly Dictionary<WarpGraphConnection,System.Windows.Shapes.Path> edgePaths=new();
    public WarpMapWindow(string projectRoot)
    {
        root=ProjectObjectIndex.Root(projectRoot);
        var panel=ProjectToolsUi.Setup(this,"Visual warp map");
        var tools=new WrapPanel(); DockPanel.SetDock(tools,Dock.Top); panel.Children.Add(tools);
        ProjectToolsUi.Button(tools,"Refresh",()=>_ = Reload());
        ProjectToolsUi.Button(tools,"Zoom +",()=>Zoom(1.2)); ProjectToolsUi.Button(tools,"Zoom -",()=>Zoom(1/1.2));
        filter=ProjectToolsUi.Input(tools,"Level filter","",140); filter.TextChanged+=(_,_)=>Draw();
        ProjectToolsUi.Button(tools,"Edit selected source",Edit);
        var side=new DockPanel { Width=350 }; DockPanel.SetDock(side,Dock.Right); panel.Children.Add(side);
        DockPanel.SetDock(details,Dock.Bottom); side.Children.Add(details); side.Children.Add(connections);
        scroll.Content=canvas; panel.Children.Add(scroll);
        connections.Background=new SolidColorBrush(Color.FromRgb(37,37,38)); connections.Foreground=Brushes.Gainsboro;
        connections.SelectionChanged+=(_,_)=>
        {
            foreach(var path in edgePaths) path.Value.StrokeThickness=Equals(connections.SelectedItem,path.Key) ? 5 : 2;
            if(connections.SelectedItem is WarpGraphConnection edge) details.Text=edge.Label+"\n"+edge.Node.MacroType+"\n"+(edge.Warning.Length==0 ? "Destination found in declared source" : edge.Warning);
        };
        canvas.MouseDown+=(_,e)=> { if(e.ChangedButton!=MouseButton.Middle) return; drag=e.GetPosition(scroll); dragX=scroll.HorizontalOffset; dragY=scroll.VerticalOffset; canvas.CaptureMouse(); e.Handled=true; };
        canvas.MouseMove+=(_,e)=> { if(drag is {} start) { var point=e.GetPosition(scroll); scroll.ScrollToHorizontalOffset(dragX+start.X-point.X); scroll.ScrollToVerticalOffset(dragY+start.Y-point.Y); } };
        canvas.MouseUp+=(_,_)=> { drag=null; canvas.ReleaseMouseCapture(); };
        canvas.MouseWheel+=(_,e)=> { if(Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { Zoom(e.Delta>0 ? 1.1 : 1/1.1); e.Handled=true; } };
        Loaded+=async (_,_)=>await Reload();
    }
    private void Zoom(double factor) { scale=Math.Clamp(scale*factor,.25,3); canvas.LayoutTransform=new ScaleTransform(scale,scale); }
    private async Task Reload()
    {
        try { details.Text="Scanning declared warp connections…"; data=await Task.Run(()=>WarpGraph.Load(root)); if(IsLoaded) Draw(); }
        catch(Exception ex) { details.Text=ex.Message; }
    }
    private void Edit()
    {
        if(connections.SelectedItem is not WarpGraphConnection edge) return;
        var existing=Application.Current.Windows.OfType<WarpEditorWindow>().FirstOrDefault(w=>w.EditingLevel(edge.Source.LevelPath));
        if(existing!=null) { existing.Activate(); return; }
        new WarpEditorWindow(edge.Source.LevelPath,()=>_ = Reload(),edge.Source.Area) { Owner=this }.Show();
    }
    private void Draw()
    {
        canvas.Children.Clear(); edgePaths.Clear();
        var shown=data.Connections.Where(e=>e.Source.Level.Contains(filter.Text,StringComparison.OrdinalIgnoreCase) || e.Destination?.Level.Contains(filter.Text,StringComparison.OrdinalIgnoreCase)==true).ToList();
        var areas=data.Areas.Where(a=>a.Level.Contains(filter.Text,StringComparison.OrdinalIgnoreCase) || shown.Any(e=>e.Source==a || e.Destination==a)).ToList();
        var positions=areas.Select((a,i)=>(a,new Point(70+i%4*270,65+i/4*170))).ToDictionary(p=>p.a,p=>p.Item2);
        canvas.Width=1200; canvas.Height=Math.Max(700,170*Math.Ceiling(areas.Count/4.0)+150);
        foreach(var edge in shown)
        {
            var a=positions[edge.Source]; var b=edge.Destination!=null && positions.TryGetValue(edge.Destination,out var destination) ? destination : a+new Vector(95,100);
            var brush=edge.Warning.Length>0 ? Brushes.OrangeRed : Brushes.SkyBlue;
            if(edge.Destination!=null && edge.Source!=edge.Destination)
            {
                var offset=a-b; if(offset.Length>0) { offset.Normalize(); b+=offset*65; }
            }
            var line=new System.Windows.Shapes.Path { Stroke=brush,StrokeThickness=2,ToolTip=edge.Label+"\n"+edge.Warning };
            var geometry=new PathGeometry(); var figure=new PathFigure { StartPoint=a };
            var control=new Point((a.X+b.X)/2+60,(a.Y+b.Y)/2-70);
            if(edge.Source==edge.Destination) { b=a+new Vector(20,0); control=a+new Vector(130,-120); }
            figure.Segments.Add(new QuadraticBezierSegment(control,b,true)); geometry.Figures.Add(figure); line.Data=geometry;
            line.MouseLeftButtonDown+=(_,_)=>connections.SelectedItem=edge; canvas.Children.Add(line);
            edgePaths[edge]=line;
            var direction=b-control; if(direction.Length>0) direction.Normalize();
            var normal=new Vector(-direction.Y,direction.X);
            var arrow=new Polygon { Fill=brush,Points=new PointCollection { b,b-direction*13+normal*5,b-direction*13-normal*5 },ToolTip=edge.Label };
            arrow.MouseLeftButtonDown+=(_,_)=>connections.SelectedItem=edge; canvas.Children.Add(arrow);
            var label=new TextBlock { Text=edge.Node.Id+" → "+edge.Node.DestNode,Foreground=brush,Background=Brushes.Black,FontSize=10,ToolTip=edge.Label };
            Canvas.SetLeft(label,control.X); Canvas.SetTop(label,control.Y); label.MouseLeftButtonDown+=(_,_)=>connections.SelectedItem=edge; canvas.Children.Add(label);
        }
        foreach(var area in areas)
        {
            var point=positions[area]; var button=new Button { Content=area.Level+" / area "+area.Area,Padding=new Thickness(8),ToolTip=area.LevelPath };
            button.Click+=(_,_)=>connections.ItemsSource=shown.Where(e=>e.Source==area || e.Destination==area).ToList();
            Canvas.SetLeft(button,point.X-45); Canvas.SetTop(button,point.Y-15); canvas.Children.Add(button);
        }
        connections.ItemsSource=shown;
        details.Text=$"{areas.Count} areas, {shown.Count} declared connections. Middle-drag to pan; ctrl-wheel to zoom.\n"+string.Join("\n",data.Errors);
    }
}
