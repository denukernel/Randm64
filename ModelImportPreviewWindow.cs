using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using Sm64DecompLevelViewer.Services;
using V3 = OpenTK.Mathematics.Vector3;
using V4 = OpenTK.Mathematics.Vector4;

namespace Sm64DecompLevelViewer;

/// <summary>A temporary, in-memory import session. Only the caller commits a confirmed result.</summary>
public sealed class ModelImportPreviewWindow : Window
{
    private readonly EditableGeometry source;
    private readonly Viewport3D viewport = new();
    private readonly Model3DGroup scene = new();
    private readonly Model3DGroup terrainScene = new();
    private readonly Models.CollisionMesh? ground;
    private readonly ISet<int>? excludedGround;
    private readonly CheckBox snapToGround = new() { Content="Keep model on ground", Foreground=Brushes.White, Margin=new Thickness(0,8,0,4) };
    private bool updatingPlacement;
    private readonly PerspectiveCamera camera = new() { FieldOfView=45, NearPlaneDistance=0.01 };
    private readonly TextBox[] position = new TextBox[3], rotation = new TextBox[3], scale = new TextBox[3];
    private readonly ComboBox materials = new() { MinWidth=230, DisplayMemberPath="Name" };
    private readonly TextBox tint = new();
    private readonly ComboBox wrapS = new(), wrapT = new();
    private readonly TextBlock status = new() { TextWrapping=TextWrapping.Wrap, Margin=new Thickness(8) };
    private readonly TextBlock textureInfo = new() { TextWrapping=TextWrapping.Wrap, Margin=new Thickness(0,8,0,8) };
    private readonly Button confirm = new() { Content="Import into level", Padding=new Thickness(18,8,18,8), IsDefault=true };
    private readonly Image thumbnail = new() { Height=100, Stretch=Stretch.Uniform, Margin=new Thickness(0,8,0,8) };
    private Point previous;
    private Point3D target;
    private double yaw=0.6, pitch=0.3, distance=500;
    private bool ready, editingMaterial;
    private string? materialError;
    private bool modeless,accepted,closed;
    public EditableGeometry? Result { get; private set; }
    public string ConfirmationText { set=>confirm.Content=value; }
    public event Action? PickInLevelRequested;
    public Task<bool> ShowForPlacementAsync(Window? owner)
    {
        modeless=true; Owner=owner;
        var completion=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Closed+=(_,_)=>completion.TrySetResult(accepted);
        Show();
        return completion.Task;
    }
    private void Finish(bool success)
    {
        if(modeless) { accepted=success; Close(); }
        else DialogResult=success;
    }
    public void ResumePlacement()
    {
        if(closed) return;
        if(!IsVisible) Show();
        Activate();
    }
    public void BeginPickInLevel()
    {
        if(!modeless) return;
        Hide(); Owner?.Activate(); PickInLevelRequested?.Invoke();
    }
    public void PlaceOnTerrain(V3 hit)
    {
        if(closed) return;
        try
        {
            var local=ModelImportService.Transform(source,V3.Zero,Read(scale),Read(rotation));
            var min=local.Vertices.Aggregate(V3.ComponentMin); var max=local.Vertices.Aggregate(V3.ComponentMax);
            updatingPlacement=true;
            position[0].Text=(hit.X-(min.X+max.X)/2).ToString("G",CultureInfo.InvariantCulture);
            position[2].Text=(hit.Z-(min.Z+max.Z)/2).ToString("G",CultureInfo.InvariantCulture);
            position[1].Text=(hit.Y-min.Y+1).ToString("G",CultureInfo.InvariantCulture);
            updatingPlacement=false; snapToGround.IsChecked=ground!=null; Refresh(); Frame();
            if(modeless) ResumePlacement();
        }
        catch(Exception ex) { updatingPlacement=false; status.Text=ex.Message; }
    }

    public ModelImportPreviewWindow(EditableGeometry geometry,string filename,V3 initialPosition,float initialScale,
        EditableGeometry? terrain=null,Models.CollisionMesh? collision=null,ISet<int>? excluded=null,bool autoGround=true)
    {
        source=geometry;
        Closed+=(_,_)=>closed=true;
        ground=collision; excludedGround=excluded;
        Title="Preview 3D model — "+Path.GetFileName(filename);
        Width=1120; Height=760; MinWidth=850; MinHeight=620; WindowStartupLocation=WindowStartupLocation.CenterOwner;
        Background=new SolidColorBrush(Color.FromRgb(35,37,43)); Foreground=Brushes.White;
        foreach(var type in new[]{typeof(TextBox),typeof(ComboBox),typeof(Button)})
        {
            var style=new Style(type); style.Setters.Add(new Setter(Control.ForegroundProperty,Brushes.Black)); Resources.Add(type,style);
        }
        var root=new DockPanel { Margin=new Thickness(12) }; Content=root;
        var footer=new StackPanel { Orientation=Orientation.Horizontal, HorizontalAlignment=HorizontalAlignment.Right };
        DockPanel.SetDock(footer,Dock.Bottom); root.Children.Add(footer);
        var cancel=new Button { Content="Cancel", Margin=new Thickness(8), Padding=new Thickness(18,8,18,8) };
        cancel.Click+=(_,_)=>Finish(false);
        PreviewKeyDown+=(_,e)=> { if(e.Key==Key.Escape) { e.Handled=true; Finish(false); } };
        footer.Children.Add(cancel); footer.Children.Add(confirm);
        confirm.Click+=(_,_)=> { Refresh(); if(Result!=null && confirm.IsEnabled) Finish(true); };
        DockPanel.SetDock(status,Dock.Bottom); root.Children.Add(status);
        var controls=new StackPanel { Margin=new Thickness(12,0,0,0) };
        var scroll=new ScrollViewer { Content=controls, Width=290, VerticalScrollBarVisibility=ScrollBarVisibility.Auto };
        DockPanel.SetDock(scroll,Dock.Right); root.Children.Add(scroll);
        Label(controls,"Transform");
        Fields(controls,"Position (level units)",position,new[]{initialPosition.X,initialPosition.Y,initialPosition.Z});
        Fields(controls,"Rotation (degrees)",rotation,new float[3]);
        Fields(controls,"Scale X / Y / Z",scale,new[]{initialScale,initialScale,initialScale});
        snapToGround.IsChecked=collision!=null && autoGround; snapToGround.IsEnabled=collision!=null;
        controls.Children.Add(snapToGround); snapToGround.Checked+=(_,_)=>Refresh(); snapToGround.Unchecked+=(_,_)=>Refresh();
        var drop=new Button { Content="Drop model to ground", Padding=new Thickness(6), IsEnabled=collision!=null };
        controls.Children.Add(drop); drop.Click+=(_,_)=> { snapToGround.IsChecked=true; Refresh(); Frame(); };
        var fit=new Button { Content="Frame model", Margin=new Thickness(0,8,0,8), Padding=new Thickness(6) };
        controls.Children.Add(fit); fit.Click+=(_,_)=>Frame();
        var frameArea=new Button { Content="Frame whole level", Padding=new Thickness(6), IsEnabled=terrain!=null };
        controls.Children.Add(frameArea); frameArea.Click+=(_,_)=>FrameBounds(true);
        var pickInLevel=new Button { Content="Pick position in level editor…", Margin=new Thickness(0,6,0,0), Padding=new Thickness(6) };
        controls.Children.Add(pickInLevel);
        pickInLevel.Click+=(_,_)=>BeginPickInLevel();
        var showTerrain=new CheckBox { Content="Show level terrain", IsChecked=terrain!=null, IsEnabled=terrain!=null, Foreground=Brushes.White, Margin=new Thickness(0,8,0,0) };
        controls.Children.Add(showTerrain);
        showTerrain.Checked+=(_,_)=> { if(terrain!=null) Render(terrain,terrainScene); };
        showTerrain.Unchecked+=(_,_)=>terrainScene.Children.Clear();
        Label(controls,"Materials and textures"); controls.Children.Add(materials);
        materials.ItemsSource=source.Materials; materials.SelectionChanged+=(_,_)=>SelectMaterial();
        controls.Children.Add(thumbnail); controls.Children.Add(textureInfo);
        var replace=new Button { Content="Choose texture…", Padding=new Thickness(6) }; controls.Children.Add(replace);
        replace.Click+=(_,_)=>
        {
            if(materials.SelectedItem is not ImportedMaterial material) return;
            var dialog=new Microsoft.Win32.OpenFileDialog { Filter="Textures|*.png;*.jpg;*.jpeg;*.bmp;*.tga;*.gif;*.psd;*.hdr" };
            if(dialog.ShowDialog(this)!=true) return;
            try { ModelImportService.ReplaceTexture(material,dialog.FileName); SelectMaterial(); Refresh(); }
            catch(Exception ex) { MessageBox.Show(this,ex.Message,"Texture",MessageBoxButton.OK,MessageBoxImage.Error); }
        };
        var remove=new Button { Content="Remove texture", Margin=new Thickness(0,6,0,6), Padding=new Thickness(6) }; controls.Children.Add(remove);
        remove.Click+=(_,_)=> { if(materials.SelectedItem is ImportedMaterial m) { m.Pixels=null; m.Width=m.Height=0; SelectMaterial(); Refresh(); } };
        Label(controls,"Material color (#RRGGBB or #AARRGGBB)"); controls.Children.Add(tint);
        Label(controls,"Texture wrap S / T");
        wrapS.ItemsSource=Enum.GetValues<Models.TextureAddressMode>(); wrapT.ItemsSource=Enum.GetValues<Models.TextureAddressMode>();
        controls.Children.Add(wrapS); controls.Children.Add(wrapT);
        tint.TextChanged+=(_,_)=>UpdateMaterial(); wrapS.SelectionChanged+=(_,_)=>UpdateMaterial(); wrapT.SelectionChanged+=(_,_)=>UpdateMaterial();
        controls.Children.Add(new TextBlock { Text="Textures are converted to N64 RGBA16 (32×32, 64×32 or 32×64). Preview shows the converted texture.\n\nDrag to orbit · Wheel to zoom\nTransform order: scale, rotate X/Y/Z, position.\nNothing is saved until Import into level.", TextWrapping=TextWrapping.Wrap, Margin=new Thickness(0,14,0,0), Foreground=Brushes.LightGray });
        var panel=new DockPanel { Background=new SolidColorBrush(Color.FromRgb(24,26,34)) };
        var help=new TextBlock { Text=$"{source.Triangles.Count:N0} triangles · {source.Materials.Count} materials    |    Drag to orbit · Wheel to zoom", Margin=new Thickness(10), Foreground=Brushes.LightGray };
        DockPanel.SetDock(help,Dock.Top); panel.Children.Add(help); panel.Children.Add(viewport); root.Children.Add(panel);
        viewport.Camera=camera; viewport.Children.Add(new ModelVisual3D { Content=new AmbientLight(Colors.White) });
        viewport.Children.Add(new ModelVisual3D { Content=terrainScene }); viewport.Children.Add(new ModelVisual3D { Content=scene });
        panel.MouseLeftButtonDown+=(_,e)=>
        {
            if(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                RayMeshGeometry3DHitTestResult? closest=null;
                VisualTreeHelper.HitTest(viewport,null,result=>
                {
                    if(result is RayMeshGeometry3DHitTestResult hit && terrainScene.Children.Contains(hit.ModelHit) &&
                        (closest==null || hit.DistanceToRayOrigin<closest.DistanceToRayOrigin)) closest=hit;
                    return HitTestResultBehavior.Continue;
                },new PointHitTestParameters(e.GetPosition(viewport)));
                if(closest!=null)
                {
                    PlaceOnTerrain(new V3((float)closest.PointHit.X,(float)closest.PointHit.Y,(float)closest.PointHit.Z));
                }
                return;
            }
            previous=e.GetPosition(panel); panel.CaptureMouse();
        };
        panel.MouseLeftButtonUp+=(_,_)=>panel.ReleaseMouseCapture();
        panel.MouseMove+=(_,e)=> { if(!panel.IsMouseCaptured) return; var p=e.GetPosition(panel); yaw-=(p.X-previous.X)*0.01; pitch=Math.Clamp(pitch+(p.Y-previous.Y)*0.01,-1.5,1.5); previous=p; UpdateCamera(); };
        panel.MouseWheel+=(_,e)=> { distance=Math.Clamp(distance*Math.Exp(-e.Delta/120.0*0.15),0.01,1e9); UpdateCamera(); };
        ready=true; materials.SelectedIndex=source.Materials.Count>0 ? 0 : -1;
        if(terrain!=null) Render(terrain,terrainScene);
        help.Text+=" · Shift-click terrain here or in the level editor to place model";
        Refresh(); Frame();
    }
    private static void Label(Panel parent,string text) => parent.Children.Add(new TextBlock { Text=text, Margin=new Thickness(0,10,0,5), FontWeight=FontWeights.SemiBold });
    private void Fields(Panel parent,string label,TextBox[] fields,float[] values)
    {
        Label(parent,label); var row=new UniformGrid { Columns=3 }; parent.Children.Add(row);
        for(int i=0;i<3;i++) { var field=new TextBox { Text=values[i].ToString("G",CultureInfo.InvariantCulture), Margin=new Thickness(0,0,4,0), Padding=new Thickness(4) }; fields[i]=field; row.Children.Add(field); field.TextChanged+=(_,_)=>Refresh(); }
    }
    private static V3 Read(TextBox[] fields) => new(float.Parse(fields[0].Text,CultureInfo.InvariantCulture),float.Parse(fields[1].Text,CultureInfo.InvariantCulture),float.Parse(fields[2].Text,CultureInfo.InvariantCulture));
    private void SelectMaterial()
    {
        if(materials.SelectedItem is not ImportedMaterial m) return;
        editingMaterial=true;
        materialError=null;
        tint.Text=$"#{(byte)(m.Color.W*255):X2}{(byte)(m.Color.X*255):X2}{(byte)(m.Color.Y*255):X2}{(byte)(m.Color.Z*255):X2}";
        wrapS.SelectedItem=m.WrapS; wrapT.SelectedItem=m.WrapT;
        thumbnail.Source=m.Pixels==null ? null : Bitmap(m,V4.One);
        textureInfo.Text=m.Pixels==null ? "Solid color — no texture" : $"{m.Width} × {m.Height} RGBA16 texture";
        editingMaterial=false;
    }
    private void UpdateMaterial()
    {
        if(editingMaterial || materials.SelectedItem is not ImportedMaterial m) return;
        try
        {
            string text=tint.Text;
            if((text.Length!=7 && text.Length!=9)||!text.StartsWith('#')) throw new FormatException("Enter a color as #RRGGBB or #AARRGGBB.");
            var c=(Color)ColorConverter.ConvertFromString(text);
            m.Color=new V4(c.R/255f,c.G/255f,c.B/255f,c.A/255f);
            if(wrapS.SelectedItem is Models.TextureAddressMode s) m.WrapS=s;
            if(wrapT.SelectedItem is Models.TextureAddressMode t) m.WrapT=t;
            materialError=null;
            Refresh();
        }
        catch(Exception ex) { materialError=ex.Message; confirm.IsEnabled=false; status.Text=ex.Message; Result=null; }
    }
    private void Refresh()
    {
        if(!ready || updatingPlacement) return;
        Result=null; confirm.IsEnabled=false;
        try
        {
            if(materialError!=null) throw new ArgumentException(materialError);
            var transformed=ModelImportService.Transform(source,Read(position),Read(scale),Read(rotation));
            if(transformed.Triangles.Count>20000) throw new ArgumentException("The model exceeds the 20,000 triangle import limit. Simplify it before importing.");
            if(transformed.Vertices.Any(p=>!float.IsFinite(p.X)||!float.IsFinite(p.Y)||!float.IsFinite(p.Z))) throw new FormatException("Transform values must be finite numbers.");
            if(snapToGround.IsChecked==true && ground!=null)
            {
                float offset;
                try { offset=ModelPlacementService.GroundOffset(transformed,ground,excludedGround); }
                catch { Render(transformed); throw; }
                var p=Read(position); p.Y+=offset;
                updatingPlacement=true; position[1].Text=p.Y.ToString("G",CultureInfo.InvariantCulture); updatingPlacement=false;
                transformed=ModelImportService.Transform(source,p,Read(scale),Read(rotation));
            }
            Render(transformed);
            LevelMeshCreation.Validate(transformed);
            // Reject values that the final display-list writer cannot encode before confirmation.
            Services.ImportedMeshWriter.Build(Path.Combine(Path.GetTempPath(),"preview.inc.c"),Path.GetTempPath(),transformed,"preview");
            Result=transformed; confirm.IsEnabled=true;
            var min=transformed.Vertices.Aggregate(V3.ComponentMin); var max=transformed.Vertices.Aggregate(V3.ComponentMax); var size=max-min;
            status.Text=$"Ready to import · Size {size.X:0.##} × {size.Y:0.##} × {size.Z:0.##} level units";
        }
        catch(Exception ex) { status.Text=ex is FormatException ? "Enter valid transform values and a material color." : ex.Message; }
    }
    private void Render(EditableGeometry mesh,Model3DGroup? destination=null)
    {
        destination ??=scene;
        destination.Children.Clear();
        // Split by average triangle vertex color so authored vertex tint remains visible.
        var groups=Enumerable.Range(0,mesh.Triangles.Count).GroupBy(i=>
        {
            var tri=mesh.Triangles[i]; var c=mesh.VertexColors.Count==mesh.Vertices.Count ? (mesh.VertexColors[tri.A]+mesh.VertexColors[tri.B]+mesh.VertexColors[tri.C])/3 : V4.One;
            return (Material:mesh.TriangleMaterials.Count>0 ? mesh.TriangleMaterials[i] : -1, Color:c);
        });
        foreach(var group in groups)
        {
            var material=group.Key.Material>=0 ? mesh.Materials[group.Key.Material] : new ImportedMaterial();
            var indices=group.SelectMany(i=>new[]{mesh.Triangles[i].A,mesh.Triangles[i].B,mesh.Triangles[i].C}).ToArray();
            double minU=0,minV=0,maxU=1,maxV=1;
            if(material.Pixels!=null)
            {
                minU=indices.Min(i=>(double)mesh.TextureCoordinates[i].X); maxU=indices.Max(i=>(double)mesh.TextureCoordinates[i].X);
                minV=indices.Min(i=>(double)mesh.TextureCoordinates[i].Y); maxV=indices.Max(i=>(double)mesh.TextureCoordinates[i].Y);
                if(!double.IsFinite(minU+minV+maxU+maxV)) throw new ArgumentException("Texture coordinates must be finite.");
                if(maxU-minU<0.000001) maxU=minU+0.000001;
                if(maxV-minV<0.000001) maxV=minV+0.000001;
            }
            var geometry=new MeshGeometry3D();
            foreach(int i in group)
            {
                var tri=mesh.Triangles[i];
                foreach(int index in new[]{tri.A,tri.B,tri.C})
                {
                    var p=mesh.Vertices[index]; geometry.Positions.Add(new Point3D(p.X,p.Y,p.Z)); geometry.TriangleIndices.Add(geometry.Positions.Count-1);
                    var uv=mesh.TextureCoordinates.Count>0 ? mesh.TextureCoordinates[index] : default;
                    geometry.TextureCoordinates.Add(new Point((uv.X-minU)/(maxU-minU),(uv.Y-minV)/(maxV-minV)));
                }
            }
            Brush brush;
            if(material.Pixels!=null)
            {
                // Bake the visible UV range, including independent repeat/mirror/clamp on each axis.
                int width=(int)Math.Clamp(Math.Ceiling((maxU-minU)*material.Width),1,1024),height=(int)Math.Clamp(Math.Ceiling((maxV-minV)*material.Height),1,1024);
                var atlas=new ImportedMaterial { Width=width,Height=height,Color=material.Color,Pixels=new byte[width*height*4] };
                static int Sample(double uv,int size,Models.TextureAddressMode mode)
                {
                    if(mode==Models.TextureAddressMode.Clamp) uv=Math.Clamp(uv,0,1);
                    else if(mode==Models.TextureAddressMode.Mirror) { uv%=2; if(uv<0) uv+=2; if(uv>1) uv=2-uv; }
                    else uv-=Math.Floor(uv);
                    return Math.Clamp((int)(uv*size),0,size-1);
                }
                for(int y=0;y<height;y++) for(int x=0;x<width;x++)
                {
                    int sx=Sample(minU+(x+0.5)/width*(maxU-minU),material.Width,material.WrapS);
                    int sy=Sample(minV+(y+0.5)/height*(maxV-minV),material.Height,material.WrapT);
                    Array.Copy(material.Pixels,(sy*material.Width+sx)*4,atlas.Pixels,(y*width+x)*4,4);
                }
                brush=new ImageBrush(Bitmap(atlas,group.Key.Color));
            }
            else { var c=material.Color*group.Key.Color; brush=new SolidColorBrush(Color.FromArgb(Byte(c.W),Byte(c.X),Byte(c.Y),Byte(c.Z))); }
            var surface=new DiffuseMaterial(brush);
            destination.Children.Add(new GeometryModel3D(geometry,surface) { BackMaterial=surface });
        }
    }
    private static byte Byte(float value)=>(byte)Math.Clamp((int)(value*255),0,255);
    private static BitmapSource Bitmap(ImportedMaterial material,V4 vertexColor)
    {
        var pixels=(byte[])material.Pixels!.Clone(); var c=material.Color*vertexColor;
        for(int i=0;i<pixels.Length;i+=4) { pixels[i]=Byte(pixels[i]/255f*c.X); pixels[i+1]=Byte(pixels[i+1]/255f*c.Y); pixels[i+2]=Byte(pixels[i+2]/255f*c.Z); pixels[i+3]=Byte(pixels[i+3]/255f*c.W); }
        for(int i=0;i<pixels.Length;i+=4) (pixels[i],pixels[i+2])=(pixels[i+2],pixels[i]);
        var bitmap=BitmapSource.Create(material.Width,material.Height,96,96,PixelFormats.Bgra32,null,pixels,material.Width*4); bitmap.Freeze(); return bitmap;
    }
    private void Frame()=>FrameBounds(false);
    private void FrameBounds(bool wholeLevel)
    {
        var bounds=scene.Bounds;
        if(wholeLevel) bounds.Union(terrainScene.Bounds);
        if(bounds.IsEmpty) return;
        target=new Point3D(bounds.X+bounds.SizeX/2,bounds.Y+bounds.SizeY/2,bounds.Z+bounds.SizeZ/2);
        distance=Math.Max(0.1,Math.Sqrt(bounds.SizeX*bounds.SizeX+bounds.SizeY*bounds.SizeY+bounds.SizeZ*bounds.SizeZ)*1.6); UpdateCamera();
    }
    private void UpdateCamera()
    {
        camera.Position=target+new Vector3D(Math.Sin(yaw)*Math.Cos(pitch)*distance,Math.Sin(pitch)*distance,Math.Cos(yaw)*Math.Cos(pitch)*distance);
        camera.LookDirection=target-camera.Position; camera.UpDirection=new Vector3D(0,1,0); camera.FarPlaneDistance=Math.Max(1000,distance*100);
    }
}
