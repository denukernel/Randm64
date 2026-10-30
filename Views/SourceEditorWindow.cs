using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Sm64DecompLevelViewer.Services;

namespace Sm64DecompLevelViewer;
public sealed partial class SourceEditorWindow : Window
{
    private readonly string root;
    private readonly ComboBox module=new() { Width=125,ItemsSource=SourceEditorCatalog.Modules,Margin=new Thickness(3) };
    private readonly ComboBox files=new() { Width=330,Margin=new Thickness(3),IsTextSearchEnabled=true };
    private readonly ComboBox functions=new() { Width=260,Margin=new Thickness(3),DisplayMemberPath="Name" };
    private readonly ComboBox blockView=new() { Width=180,Margin=new Thickness(3),ItemsSource=new[]{"Game code blocks","My block program"},SelectedIndex=0 };
    private readonly DispatcherTimer gameRefresh=new() { Interval=TimeSpan.FromMilliseconds(350) };
    private readonly TextBox fileSearch;
    private readonly TreeView workspace=new() { Background=new SolidColorBrush(Color.FromRgb(28,30,35)),BorderThickness=new Thickness(0),AllowDrop=true };
    private readonly TextBox first,second,dataType;
    private readonly TextBlock fieldHelp=new() { TextWrapping=TextWrapping.Wrap,Margin=new Thickness(5),Foreground=Brushes.Gainsboro };
    private readonly TextBlock status=new() { TextWrapping=TextWrapping.Wrap,Margin=new Thickness(5) };
    private readonly TextBox generated=CodeBox(true),source=CodeBox(false),preview=CodeBox(true);
    private readonly TabControl views=new();
    private readonly Button commit;
    private readonly Button updateBlock;
    private IReadOnlyList<string> available=Array.Empty<string>();
    private SourceBlockProject program=new();
    private ProjectFileDocument? document;
    private SourceEditPlan? pending;
    private string? previewStamp;
    private bool loading,designDirty,inspecting,pendingRaw;
    private int scanGeneration;
    private Point dragStart;
    private SourceCodeBlock? selected => (workspace.SelectedItem as TreeViewItem)?.Tag as SourceCodeBlock;
    private static TextBox CodeBox(bool readOnly)=>new() { IsReadOnly=readOnly,AcceptsReturn=true,AcceptsTab=true,FontFamily=new FontFamily("Consolas"),FontSize=13,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Background=new SolidColorBrush(Color.FromRgb(24,24,27)),Foreground=Brushes.Gainsboro,TextWrapping=TextWrapping.NoWrap };
    public SourceEditorWindow(string projectRoot,string initialModule="mario")
    {
        root=Directory.Exists(Path.Combine(projectRoot,"src")) ? Path.GetFullPath(projectRoot) : ProjectObjectIndex.Root(projectRoot);
        var panel=ProjectToolsUi.Setup(this,"Source editor · visual C blocks"); Width=1400; Height=900; MinWidth=1050; MinHeight=650;
        var targets=new WrapPanel(); DockPanel.SetDock(targets,Dock.Top); panel.Children.Add(targets);
        targets.Children.Add(new TextBlock { Text="Module",VerticalAlignment=VerticalAlignment.Center }); targets.Children.Add(module);
        fileSearch=ProjectToolsUi.Input(targets,"Filter files","",125); targets.Children.Add(files); targets.Children.Add(functions); targets.Children.Add(blockView);
        ProjectToolsUi.Button(targets,"Reload source",()=>OpenSelected(true));
        var actions=new WrapPanel(); DockPanel.SetDock(actions,Dock.Top); panel.Children.Add(actions);
        ProjectToolsUi.Button(actions,"Load blocks",LoadDesign); ProjectToolsUi.Button(actions,"Save blocks",SaveDesign);
        ProjectToolsUi.Button(actions,"Preview block changes",()=>Prepare(false)); ProjectToolsUi.Button(actions,"Preview source changes",()=>Prepare(true));
        commit=ProjectToolsUi.Button(actions,"Save previewed changes",Commit); commit.IsEnabled=false;
        var notice=new TextBlock { Text="Game code blocks edits the existing function. Select a block, change fields and click Update block; use the palette to insert at the chosen position, or Delete to remove it. Preview block changes before saving. Protected C remains available through Source text.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(5) };
        DockPanel.SetDock(notice,Dock.Top); panel.Children.Add(notice); DockPanel.SetDock(status,Dock.Bottom); panel.Children.Add(status);
        var layout=new Grid(); layout.RowDefinitions.Add(new RowDefinition { Height=new GridLength(3,GridUnitType.Star) }); layout.RowDefinitions.Add(new RowDefinition { Height=new GridLength(5) }); layout.RowDefinitions.Add(new RowDefinition { Height=new GridLength(2,GridUnitType.Star) }); panel.Children.Add(layout);
        var upper=new Grid(); upper.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(200) }); upper.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(1,GridUnitType.Star) }); upper.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(310) }); layout.Children.Add(upper);
        var palette=new StackPanel(); var paletteScroll=new ScrollViewer { Content=palette,VerticalScrollBarVisibility=ScrollBarVisibility.Auto }; upper.Children.Add(paletteScroll);
        palette.Children.Add(new TextBlock { Text="BLOCK PALETTE",Margin=new Thickness(6),FontWeight=FontWeights.Bold });
        foreach(var kind in Enum.GetValues<SourceBlockKind>())
        {
            var button=ProjectToolsUi.Button(palette,kind switch { SourceBlockKind.Set=>"Set variable / field",SourceBlockKind.Add=>"Change by",SourceBlockKind.Multiply=>"Multiply by",SourceBlockKind.Call=>"Call function",SourceBlockKind.Declare=>"Local variable",_=>kind.ToString() },()=>Add(SourceCodeBlock.Create(kind)));
            button.Background=BlockBrush(kind); button.Foreground=Brushes.White;
            button.PreviewMouseLeftButtonDown+=(_,e)=>dragStart=e.GetPosition(button);
            button.PreviewMouseMove+=(_,e)=> { if(e.LeftButton==MouseButtonState.Pressed && (e.GetPosition(button)-dragStart).Length>6) DragDrop.DoDragDrop(button,kind,DragDropEffects.Copy); };
        }
        palette.Children.Add(new TextBlock { Text="MODULE STARTERS",Margin=new Thickness(6),FontWeight=FontWeights.Bold });
        ProjectToolsUi.Button(palette,"Mario: health",()=>Mario("health","0x880"));
        ProjectToolsUi.Button(palette,"Mario: vertical speed",()=>Mario("vel[1]","20.0f"));
        ProjectToolsUi.Button(palette,"Mario: invincibility",()=>Mario("invincTimer","30"));
        ProjectToolsUi.Button(palette,"Goddard: debug message",()=>
        {
            if(!program.TargetFile.StartsWith("src/goddard/",StringComparison.Ordinal)) throw new InvalidOperationException("Select a Goddard source file first");
            if(document==null || !document.Text.Contains("gd_printf")) throw new InvalidOperationException("Select a Goddard file that already declares or uses gd_printf");
            Add(new() { Kind=SourceBlockKind.Call,First="gd_printf",Second="\"randm64 block\\n\"" });
        });
        var stack=new DockPanel { Margin=new Thickness(7,0,7,0) }; Grid.SetColumn(stack,1); upper.Children.Add(stack);
        var edit=new WrapPanel(); DockPanel.SetDock(edit,Dock.Top); stack.Children.Add(edit);
        ProjectToolsUi.Button(edit,"Up",()=>Move(-1)); ProjectToolsUi.Button(edit,"Down",()=>Move(1));
        ProjectToolsUi.Button(edit,"Nest",Nest); ProjectToolsUi.Button(edit,"Outdent",Outdent);
        ProjectToolsUi.Button(edit,"Duplicate",Duplicate); ProjectToolsUi.Button(edit,"Delete",Delete);
        ProjectToolsUi.Button(edit,"Root",()=> { if(workspace.SelectedItem is TreeViewItem item) item.IsSelected=false; });
        var hookControls=edit.Children.OfType<Button>().Where(b=>b.Content is "Up" or "Down" or "Nest" or "Outdent" or "Duplicate").ToList();
        foreach(var control in hookControls) control.IsEnabled=false;
        edit.Children.Add(gamePlacement);
        ProjectToolsUi.Button(edit,"Undo game edit",()=>GameUndo(false)); ProjectToolsUi.Button(edit,"Redo",()=>GameUndo(true));
        stack.Children.Add(workspace);
        var inspector=new StackPanel(); Grid.SetColumn(inspector,2); upper.Children.Add(inspector);
        inspector.Children.Add(new TextBlock { Text="SELECTED BLOCK",Margin=new Thickness(6),FontWeight=FontWeights.Bold });
        var row1=new WrapPanel(); inspector.Children.Add(row1); first=ProjectToolsUi.Input(row1,"Target / condition / count","",280);
        var row2=new WrapPanel(); inspector.Children.Add(row2); second=ProjectToolsUi.Input(row2,"Value / call arguments","",280);
        var row3=new WrapPanel(); inspector.Children.Add(row3); dataType=ProjectToolsUi.Input(row3,"Local variable type","int",180);
        updateBlock=ProjectToolsUi.Button(inspector,"Update block",UpdateBlock); updateBlock.IsEnabled=false; inspector.Children.Add(fieldHelp);
        foreach(var input in new[]{first,second,dataType}) input.TextChanged+=(_,_)=>
        {
            if(inspecting) return;
            if(blockView.SelectedIndex==0 && gameInspector!=null) { gameDraft=true; Invalidate(); status.Text="Block fields changed. Click Update block, or Preview block changes to apply them."; return; }
            if(selected is not {} block) return;
            block.First=first.Text; block.Second=second.Text; block.DataType=dataType.Text.Trim();
            if(workspace.SelectedItem is TreeViewItem { Header:Border { Child:TextBlock label } }) label.Text=block.Label;
            designDirty=true; Invalidate(); RefreshGenerated();
        };
        var splitter=new GridSplitter { Height=5,HorizontalAlignment=HorizontalAlignment.Stretch,Background=Brushes.DimGray }; Grid.SetRow(splitter,1); layout.Children.Add(splitter);
        views.Items.Add(new TabItem { Header="Block C / selected source",Content=generated }); views.Items.Add(new TabItem { Header="Source text",Content=source }); views.Items.Add(new TabItem { Header="Change preview",Content=preview }); Grid.SetRow(views,2); layout.Children.Add(views);
        module.SelectionChanged+=async (_,_)=> { if(!loading) await ScanFiles(); };
        fileSearch.TextChanged+=(_,_)=>FilterFiles(); files.SelectionChanged+=(_,_)=> { if(!loading) ProjectToolsUi.Guard(()=>OpenSelected()); };
        functions.SelectionChanged+=(_,_)=> { if(!loading) ProjectToolsUi.Guard(FunctionChanged); };
        source.TextChanged+=(_,_)=> { if(!loading) { gameUndo.Clear(); gameRedo.Clear(); gameInspector=null; gameDraft=false; Invalidate(); gameRefresh.Stop(); if(blockView.SelectedIndex==0) gameRefresh.Start(); } };
        gameRefresh.Tick+=(_,_)=> { gameRefresh.Stop(); if(blockView.SelectedIndex==0) Render(); };
        blockView.SelectionChanged+=(_,_)=>
        {
            if(gameSwitching) return;
            try { if(gameDraft) UpdateGameInspector(); }
            catch(Exception ex) { status.Text=ex.Message; gameSwitching=true; blockView.SelectedIndex=0; gameSwitching=false; return; }
            workspace.AllowDrop=true; foreach(var control in hookControls) control.IsEnabled=blockView.SelectedIndex==1; updateBlock.IsEnabled=true; Render();
        };
        workspace.SelectedItemChanged+=(_,_)=>Inspect();
        workspace.PreviewMouseLeftButtonDown+=(_,e)=>dragStart=e.GetPosition(workspace);
        workspace.PreviewMouseMove+=(_,e)=> { if(e.LeftButton==MouseButtonState.Pressed && selected is {} block && (e.GetPosition(workspace)-dragStart).Length>6) DragDrop.DoDragDrop(workspace,block,DragDropEffects.Move); };
        workspace.Drop+=Drop;
        workspace.DragOver+=(_,e)=> { e.Effects=e.Data.GetDataPresent(typeof(SourceBlockKind)) ? DragDropEffects.Copy : e.Data.GetDataPresent(typeof(SourceCodeBlock)) ? DragDropEffects.Move : DragDropEffects.None; e.Handled=true; };
        Loaded+=async (_,_)=> { loading=true; module.SelectedItem=SourceEditorCatalog.Modules.Contains(initialModule) ? initialModule : "mario"; loading=false; await ScanFiles(); };
        Closing+=(_,e)=>e.Cancel=!CanLeave();
        Closed+=(_,_)=>gameRefresh.Stop();
        workspace.AllowDrop=true;
    }
    private static Brush BlockBrush(SourceBlockKind kind)=>new SolidColorBrush(kind switch { SourceBlockKind.If or SourceBlockKind.Else or SourceBlockKind.Repeat or SourceBlockKind.While=>Color.FromRgb(161,104,22),SourceBlockKind.Call=>Color.FromRgb(110,65,160),SourceBlockKind.Declare=>Color.FromRgb(35,126,90),_=>Color.FromRgb(37,105,175) });
    private bool CanLeave()=>!gameDraft && (document==null && !designDirty || !designDirty && source.Text==document?.Text) || MessageBox.Show("Discard unsaved changes? Preview and save source edits, or use Save blocks for My block program.","Unsaved source editor",MessageBoxButton.YesNo,MessageBoxImage.Question)==MessageBoxResult.Yes;
    private void Invalidate() { pending=null; previewStamp=null; commit.IsEnabled=false; }
    private string Stamp()=>program.ToJson()+"\n"+source.Text;
    private async Task ScanFiles()
    {
        int generation=++scanGeneration; string group=module.SelectedItem as string ?? "all source";
        try { var found=await Task.Run(()=>SourceEditorCatalog.Files(root,group)); if(generation!=scanGeneration || !IsLoaded) return; available=found; FilterFiles(); status.Text=$"{found.Count} source files in {group}"; }
        catch(Exception ex) { status.Text=ex.Message; }
    }
    private void FilterFiles()
    {
        if(fileSearch==null) return; var previous=files.SelectedItem as string;
        loading=true; files.ItemsSource=available.Where(p=>p.Contains(fileSearch.Text,StringComparison.OrdinalIgnoreCase)).ToArray(); files.SelectedItem=previous; loading=false;
    }
    private void OpenSelected(bool reload=false)
    {
        if(files.SelectedItem is not string relative) return;
        if(!reload && document!=null && relative==program.TargetFile) return;
        if(!CanLeave()) { loading=true; files.SelectedItem=program.TargetFile; loading=false; return; }
        var opened=ProjectFileDocument.Open(SourceEditorCatalog.Resolve(root,relative));
        document=opened; program=new() { TargetFile=relative }; designDirty=false;
        gameUndo.Clear(); gameRedo.Clear(); gameInspector=null; gameDraft=false;
        loading=true;
        try { source.Text=opened.Text; functions.ItemsSource=SourceBlocks.Functions(opened.Text); functions.SelectedIndex=functions.Items.Count>0 ? 0 : -1; }
        finally { loading=false; }
        FunctionChanged(); status.Text="Loaded "+relative+". Select a function for visual blocks, or use Source text for manual edits.";
    }
    private void FunctionChanged()
    {
        if(document==null) return;
        if(gameDraft)
        {
            try { UpdateGameInspector(); }
            catch
            {
                loading=true; functions.SelectedItem=functions.Items.OfType<SourceFunctionTarget>().FirstOrDefault(f=>f.Name==program.Function); loading=false;
                throw;
            }
        }
        string next=(functions.SelectedItem as SourceFunctionTarget)?.Name ?? "";
        if(next==program.Function) return;
        if(designDirty && MessageBox.Show("Discard block changes for the previous function?","Unsaved blocks",MessageBoxButton.YesNo)==MessageBoxResult.No)
        {
            loading=true; functions.SelectedItem=functions.Items.OfType<SourceFunctionTarget>().FirstOrDefault(f=>f.Name==program.Function); loading=false; return;
        }
        program.Function=next; program.Blocks=new(); designDirty=false; Invalidate();
        try { if(next.Length>0) program.Blocks=SourceBlocks.ReadManaged(document.Text,next) ?? new(); }
        finally { Render(); }
    }
    private (List<SourceCodeBlock> List,SourceCodeBlock? Parent)? Locate(SourceCodeBlock target,List<SourceCodeBlock>? list=null,SourceCodeBlock? parent=null)
    {
        list ??= program.Blocks;
        if(list.Contains(target)) return (list,parent);
        foreach(var block in list) if(Locate(target,block.Children,block) is {} found) return found;
        return null;
    }
    private void Changed(SourceCodeBlock? select=null) { designDirty=true; Invalidate(); Render(select); }
    private void Render(SourceCodeBlock? select=null)
    {
        if(blockView.SelectedIndex==0) { RenderGameCode(); return; }
        workspace.Items.Clear();
        void AddItems(ItemCollection items,List<SourceCodeBlock> blocks)
        {
            foreach(var block in blocks)
            {
                var header=new Border { Background=BlockBrush(block.Kind),CornerRadius=new CornerRadius(7,7,7,2),Padding=new Thickness(10,7,10,7),Margin=new Thickness(2),Child=new TextBlock { Text=block.Label,Foreground=Brushes.White,TextWrapping=TextWrapping.Wrap,MaxWidth=530 } };
                var item=new TreeViewItem { Header=header,Tag=block,IsExpanded=true,Foreground=Brushes.White }; items.Add(item); AddItems(item.Items,block.Children); if(ReferenceEquals(block,select)) item.IsSelected=true;
            }
        }
        AddItems(workspace.Items,program.Blocks);
        RefreshGenerated();
    }
    private void RefreshGenerated()
    {
        try { generated.Text=SourceBlocks.Generate(program.Blocks); status.Text="Blocks use a separate scope before the original body. Original function locals are not visible to them."; }
        catch(Exception ex) { generated.Text="/* Fix block fields to generate C. */"; status.Text=ex.Message; }
    }
    private void Add(SourceCodeBlock block)
    {
        if(document==null || program.Function.Length==0) throw new InvalidOperationException("Select a source file and a unique function first");
        if(blockView.SelectedIndex==0) { AddGameBlock(block); return; }
        if(selected is {} target && Locate(target) is {} location)
        {
            if(target.IsContainer) target.Children.Add(block); else location.List.Insert(location.List.IndexOf(target)+1,block);
        }
        else program.Blocks.Add(block);
        Changed(block);
    }
    private void Inspect()
    {
        if(blockView.SelectedIndex==0) { InspectGameBlock(); return; }
        if(workspace.SelectedItem is TreeViewItem { Tag:GameCodeBlock game })
        {
            first.IsEnabled=false; second.IsEnabled=false; dataType.IsEnabled=false;
            if(game.End>source.Text.Length) { RenderGameCode(); return; }
            generated.Text=game.Text(source.Text); views.SelectedIndex=0;
            source.Select(game.Start,game.Length);
            int line=source.Text[..game.Start].Count(c=>c=='\n')+1;
            fieldHelp.Text=$"Existing game code · {game.Kind}\n{program.TargetFile}:{line}\n\nThis block view is read-only. Source text edits refresh it automatically. Unsupported C remains visible as source blocks.";
            status.Text=$"Selected existing {game.Kind} block at line {line}. No source changes are made by browsing.";
            return;
        }
        if(selected is not {} block) { first.IsEnabled=false; second.IsEnabled=false; dataType.IsEnabled=false; fieldHelp.Text="Select a block to edit its fields."; return; }
        inspecting=true;
        first.Text=block.First; second.Text=block.Second; dataType.Text=block.DataType;
        inspecting=false;
        second.IsEnabled=block.Kind is SourceBlockKind.Set or SourceBlockKind.Add or SourceBlockKind.Multiply or SourceBlockKind.Call or SourceBlockKind.Declare;
        first.IsEnabled=block.Kind!=SourceBlockKind.Else; dataType.IsEnabled=block.Kind==SourceBlockKind.Declare;
        fieldHelp.Text=block.Kind switch { SourceBlockKind.Call=>"Target is a C function name; value contains comma-separated arguments. The selected source must declare the function.",SourceBlockKind.Declare=>"Use int, float, short, unsigned int, s16, s32, u16, u32, or f32. Declarations are emitted before statements in each scope; initialization stays at this block position.",SourceBlockKind.Repeat or SourceBlockKind.While=>"Loops execute within one function call, not over multiple game frames. Repeat evaluates its count once. Use a terminating condition for while.",SourceBlockKind.Else=>"Place else as the final child of an if block. Drop statements inside it.",_=>"Use C expressions, for example m->health, gMarioState->vel[1], or value > 0. Names and types depend on the selected source." };
    }
    private void UpdateBlock() { if(blockView.SelectedIndex==0) { UpdateGameInspector(); return; } if(selected is not {} block) return; block.First=first.Text; block.Second=second.Text; block.DataType=dataType.Text.Trim(); Changed(block); }
    private void Move(int delta) { if(selected is {} block && Locate(block) is {} location) { int from=location.List.IndexOf(block),to=from+delta; if(to<0 || to>=location.List.Count) return; location.List.RemoveAt(from); location.List.Insert(to,block); Changed(block); } }
    private void Delete() { if(blockView.SelectedIndex==0) { DeleteGameBlock(); return; } if(selected is {} block && Locate(block) is {} location) { location.List.Remove(block); Changed(); } }
    private void Duplicate() { if(selected is {} block && Locate(block) is {} location) { var copy=JsonSerializer.Deserialize<SourceCodeBlock>(JsonSerializer.Serialize(block))!; location.List.Insert(location.List.IndexOf(block)+1,copy); Changed(copy); } }
    private void Nest() { if(selected is {} block && Locate(block) is {} location) { int index=location.List.IndexOf(block); if(index==0 || !location.List[index-1].IsContainer) throw new InvalidOperationException("Move this block after a control block to nest it"); var parent=location.List[index-1]; location.List.Remove(block); parent.Children.Add(block); Changed(block); } }
    private void Outdent() { if(selected is {} block && Locate(block) is { Parent:not null } location && Locate(location.Parent) is {} outer) { location.List.Remove(block); outer.List.Insert(outer.List.IndexOf(location.Parent)+1,block); Changed(block); } }
    private void Drop(object sender,DragEventArgs e)
    {
        if(blockView.SelectedIndex==0) { DropGameBlock(e); return; }
        ProjectToolsUi.Guard(()=>
        {
            TreeViewItem? item=null; for(DependencyObject? node=e.OriginalSource as DependencyObject;node!=null && node!=workspace;node=VisualTreeHelper.GetParent(node)) if(node is TreeViewItem found) { item=found; break; }
            var target=item?.Tag as SourceCodeBlock;
            var block=e.Data.GetData(typeof(SourceCodeBlock)) as SourceCodeBlock;
            if(block!=null)
            {
                bool Contains(SourceCodeBlock parent,SourceCodeBlock child)=>ReferenceEquals(parent,child) || parent.Children.Any(b=>Contains(b,child));
                if(target!=null && Contains(block,target)) throw new InvalidOperationException("A block cannot be dropped inside itself or a descendant");
                var location=Locate(block) ?? throw new InvalidOperationException("The dragged block no longer exists"); location.List.Remove(block);
            }
            else if(e.Data.GetData(typeof(SourceBlockKind)) is SourceBlockKind kind) block=SourceCodeBlock.Create(kind);
            if(block==null) return;
            if(document==null || program.Function.Length==0) throw new InvalidOperationException("Select a source file and function first");
            if(target==null) program.Blocks.Add(block); else if(target.IsContainer) target.Children.Add(block); else if(Locate(target) is {} at) at.List.Insert(at.List.IndexOf(target)+1,block);
            Changed(block);
        }); e.Handled=true;
    }
    private void Mario(string field,string value)
    {
        if(document==null || !program.TargetFile.StartsWith("src/game/mario",StringComparison.Ordinal)) throw new InvalidOperationException("Select a Mario source file first");
        string signature=(functions.SelectedItem as SourceFunctionTarget)?.Signature ?? "";
        string target=Regex.IsMatch(signature,@"\bMarioState\s*\*\s*m\b") ? "m" : document.Text.Contains("gMarioState") ? "gMarioState" : throw new InvalidOperationException("Select a Mario function with a MarioState *m parameter or a declared gMarioState global");
        Add(new() { Kind=SourceBlockKind.If,First=target+" != NULL",Children=new(){new() { Kind=SourceBlockKind.Set,First=target+"->"+field,Second=value }} });
    }
    private void Prepare(bool raw)
    {
        if(!raw && blockView.SelectedIndex==0) { if(gameDraft) UpdateGameInspector(); raw=true; }
        Invalidate();
        if(document==null) throw new InvalidOperationException("Select a source file first");
        if(!File.ReadAllBytes(document.Path).SequenceEqual(document.Snapshot)) throw new IOException("Source changed externally. Reload before previewing changes.");
        if(!raw && source.Text!=document.Text) throw new InvalidOperationException("Save or discard manual source changes before applying blocks");
        if(!raw && selected is {} block) { block.First=first.Text; block.Second=second.Text; block.DataType=dataType.Text.Trim(); }
        string updated=raw ? source.Text : SourceBlocks.Apply(document.Text,program.Function,program.Blocks);
        var plan=new SourceEditPlan(); plan.Read(document.Path);
        if(plan.ExpectedVersions[document.Path]?.SequenceEqual(document.Snapshot)!=true) throw new IOException("Source changed while preparing the preview. Reload before saving.");
        plan.Write(document.Path,updated);
        preview.Text=plan.Writes.Count==0 ? "No source changes." : plan.Preview(root);
        pending=plan; pendingRaw=raw; previewStamp=Stamp(); commit.IsEnabled=plan.Writes.Count>0; views.SelectedIndex=2;
        status.Text="Review the preview, then save. Source saves keep first backups and reject external edits. Build the game to validate C syntax, symbols and runtime behavior.";
    }
    private void Commit()
    {
        if(pending==null || previewStamp!=Stamp()) throw new InvalidOperationException("Preview the current changes before saving");
        pending.Commit(); document=ProjectFileDocument.Open(document!.Path);
        gameUndo.Clear(); gameRedo.Clear(); gameDraft=false; gameInspector=null;
        loading=true;
        try { source.Text=document.Text; functions.ItemsSource=SourceBlocks.Functions(document.Text); functions.SelectedItem=functions.Items.OfType<SourceFunctionTarget>().FirstOrDefault(f=>f.Name==program.Function); }
        finally { loading=false; }
        if(!pendingRaw) designDirty=false; Invalidate(); status.Text="Source saved with conflict checks and first backup. Build the game to validate the changes.";
        if(blockView.SelectedIndex==0) RenderGameCode();
    }
    private void SaveDesign()
    {
        if(blockView.SelectedIndex==0) throw new InvalidOperationException("Use Preview block changes and Save previewed changes to save game code. Save blocks exports My block program.");
        if(selected is {} block) { block.First=first.Text; block.Second=second.Text; block.DataType=dataType.Text.Trim(); Invalidate(); }
        SourceBlocks.Generate(program.Blocks);
        var dialog=new SaveFileDialog { Filter="Randm64 blocks (*.randblocks.json)|*.randblocks.json",FileName="source.randblocks.json" };
        if(dialog.ShowDialog(this)!=true) return;
        File.WriteAllText(dialog.FileName,program.ToJson()); designDirty=false; status.Text="Block project saved: "+dialog.FileName;
    }
    private void LoadDesign()
    {
        var dialog=new OpenFileDialog { Filter="Randm64 blocks (*.randblocks.json)|*.randblocks.json|JSON (*.json)|*.json" };
        if(dialog.ShowDialog(this)!=true || !CanLeave()) return;
        var loaded=SourceBlockProject.Parse(File.ReadAllText(dialog.FileName)); string path=SourceEditorCatalog.Resolve(root,loaded.TargetFile);
        var opened=ProjectFileDocument.Open(path);
        if(SourceBlocks.Functions(opened.Text).Count(f=>f.Name==loaded.Function)!=1) throw new InvalidDataException("The saved target function is missing or ambiguous in this project");
        document=opened; program=loaded; designDirty=false; loading=true;
        try { files.ItemsSource=new[]{loaded.TargetFile}; files.SelectedItem=loaded.TargetFile; source.Text=opened.Text; functions.ItemsSource=SourceBlocks.Functions(opened.Text); functions.SelectedItem=functions.Items.OfType<SourceFunctionTarget>().First(f=>f.Name==loaded.Function); }
        finally { loading=false; }
        blockView.SelectedIndex=1; Invalidate(); Render(); status.Text="Loaded block design. Preview before applying it to this project.";
    }
    private void RenderGameCode()
    {
        workspace.Items.Clear();
        first.IsEnabled=false; second.IsEnabled=false; dataType.IsEnabled=false;
        if(document==null || program.Function.Length==0) { generated.Text="Select a source file and function to see its existing code as blocks."; return; }
        try
        {
            gameSession=new GameCodeEditSession(source.Text,program.Function);
            var blocks=gameSession.Blocks;
            int total=0;
            void AddItems(ItemCollection items,IReadOnlyList<GameCodeBlock> children)
            {
                foreach(var block in children)
                {
                    if(string.IsNullOrWhiteSpace(block.Text(source.Text))) continue;
                    total++;
                    Color color=block.Kind switch { "if" or "else" or "while" or "for" or "do" or "switch" or "case"=>Color.FromRgb(161,104,22),"call"=>Color.FromRgb(110,65,160),"assignment"=>Color.FromRgb(37,105,175),"declaration"=>Color.FromRgb(35,126,90),"flow"=>Color.FromRgb(150,60,80),_=>Color.FromRgb(75,80,95) };
                    string label=block.Label.Length>180 ? block.Label[..180]+"…" : block.Label;
                    var card=new Border { Background=new SolidColorBrush(color),CornerRadius=new CornerRadius(8,8,8,2),Padding=new Thickness(10,7,10,7),Margin=new Thickness(2),Child=new TextBlock { Text=label,Foreground=Brushes.White,TextWrapping=TextWrapping.Wrap,MaxWidth=530 },ToolTip="Select to inspect the original C source" };
                    var item=new TreeViewItem { Header=card,Tag=block,IsExpanded=true,Foreground=Brushes.White }; items.Add(item); AddItems(item.Items,block.Children);
                }
            }
            AddItems(workspace.Items,blocks);
            generated.Text="Select any game-code block to inspect its exact source.\n\n"+program.Function+" · "+total+" blocks";
            fieldHelp.Text="Select a supported block to change its fields. Use the palette and insertion position to add blocks, or Delete to remove one. Unsupported source and regional blocks remain protected.";
            status.Text=$"{program.Function}: {total} game-code blocks. Edits are staged in Source text; preview before saving.";
        }
        catch(Exception ex) { generated.Text="Game-code block view unavailable: "+ex.Message+"\nThe Source text view remains available."; status.Text=ex.Message; }
    }
}
