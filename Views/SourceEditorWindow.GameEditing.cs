using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Sm64DecompLevelViewer.Services;

namespace Sm64DecompLevelViewer;
public sealed partial class SourceEditorWindow
{
    private readonly ComboBox gamePlacement=new() { Width=140,Margin=new Thickness(3),ItemsSource=new[]{"after selected","before selected","inside selected","function start"},SelectedIndex=0 };
    private readonly Stack<string> gameUndo=new(),gameRedo=new();
    private GameCodeEditSession? gameSession;
    private GameCodeBlock? gameInspector;
    private bool gameDraft,gameSelecting,gameSwitching;
    private GameCodeEditSession GameSession()
    {
        if(gameSession==null || gameSession.Source!=source.Text) throw new InvalidDataException("Source changed since these blocks were displayed. Refresh the game-code view before editing.");
        return gameSession;
    }
    private IEnumerable<TreeViewItem> GameItems(ItemCollection items)=>items.OfType<TreeViewItem>().SelectMany(item=>new[]{item}.Concat(GameItems(item.Items)));
    private void ApplyGameEdit(string updated,GameCodeBlock? selection=null)
    {
        string previous=source.Text;
        if(previous==updated) { gameDraft=false; return; }
        int common=0; while(common<previous.Length && common<updated.Length && previous[common]==updated[common]) common++;
        int suffix=0; while(suffix<previous.Length-common && suffix<updated.Length-common && previous[^(suffix+1)]==updated[^(suffix+1)]) suffix++;
        int selectionStart=selection?.Start ?? -1;
        if(selectionStart>=previous.Length-suffix) selectionStart+=updated.Length-previous.Length;
        else if(selectionStart>common) selectionStart=common;
        gameUndo.Push(previous); gameRedo.Clear(); gameDraft=false; gameInspector=null; gameRefresh.Stop();
        loading=true; try { source.Text=updated; } finally { loading=false; }
        Invalidate(); RenderGameCode();
        if(selection!=null)
        {
            var item=GameItems(workspace.Items).FirstOrDefault(item=>item.Tag is GameCodeBlock block && block.Start==selectionStart && block.Kind==selection.Kind);
            if(item!=null) { item.IsSelected=true; item.BringIntoView(); }
        }
        status.Text="Game-code changes staged. Preview block changes, review the diff, then save. Undo is available before saving.";
    }
    private void InspectGameBlock()
    {
        if(gameSelecting) return;
        var current=(workspace.SelectedItem as TreeViewItem)?.Tag as GameCodeBlock;
        if(gameDraft && gameInspector!=null && !ReferenceEquals(current,gameInspector))
        {
            try { UpdateGameInspector(current); }
            catch(Exception ex)
            {
                status.Text=ex.Message; gameSelecting=true;
                var old=GameItems(workspace.Items).FirstOrDefault(item=>ReferenceEquals(item.Tag,gameInspector)); if(old!=null) old.IsSelected=true;
                gameSelecting=false;
            }
            return;
        }
        gameInspector=current; gameDraft=false;
        if(current==null) { first.IsEnabled=false; second.IsEnabled=false; dataType.IsEnabled=false; updateBlock.IsEnabled=false; return; }
        try
        {
            var session=GameSession(); var fields=session.Fields(current);
            inspecting=true;
            try { first.Text=fields?.First ?? ""; second.Text=fields?.Second ?? ""; dataType.Text=fields?.DataType ?? ""; }
            finally { inspecting=false; }
            first.IsEnabled=fields!=null; second.IsEnabled=fields?.HasSecond==true; dataType.IsEnabled=fields?.HasType==true; updateBlock.IsEnabled=fields!=null;
            generated.Text=current.Text(source.Text); views.SelectedIndex=0; source.Select(current.Start,current.Length);
            int line=source.Text[..current.Start].Count(c=>c=='\n')+1;
            fieldHelp.Text=$"{program.TargetFile}:{line}\n{current.Kind}\n\n"+(fields?.Help ?? "This block has no supported fields. Source, regional sections, case labels, and generated hooks are protected; use Source text or My block program as appropriate.");
            status.Text=fields==null ? "Selected protected or structural game-code block." : "Edit fields and click Update block. Changes stay in the editor until previewed and saved.";
        }
        catch(Exception ex)
        {
            first.IsEnabled=false; second.IsEnabled=false; dataType.IsEnabled=false; updateBlock.IsEnabled=false;
            gameInspector=null; gameDraft=false; status.Text=ex.Message;
        }
    }
    private void UpdateGameInspector(GameCodeBlock? nextSelection=null)
    {
        if(gameInspector==null) return;
        var session=GameSession();
        string updated=session.Update(gameInspector,first.Text,second.Text,dataType.Text);
        var select=nextSelection ?? gameInspector;
        gameDraft=false; ApplyGameEdit(updated,select);
    }
    private void AddGameBlock(SourceCodeBlock block)
    {
        if(gameDraft) UpdateGameInspector();
        var selectedGame=(workspace.SelectedItem as TreeViewItem)?.Tag as GameCodeBlock;
        ApplyGameEdit(GameSession().Insert(selectedGame,new[]{block},gamePlacement.SelectedItem as string ?? "after selected"));
    }
    private void DeleteGameBlock()
    {
        if(gameDraft) UpdateGameInspector();
        if((workspace.SelectedItem as TreeViewItem)?.Tag is not GameCodeBlock game) return;
        ApplyGameEdit(GameSession().Remove(game));
    }
    private void GameUndo(bool redo)
    {
        if(blockView.SelectedIndex!=0) return;
        if(gameDraft) UpdateGameInspector();
        var from=redo ? gameRedo : gameUndo; var to=redo ? gameUndo : gameRedo;
        if(from.Count==0) return;
        to.Push(source.Text); string text=from.Pop(); gameInspector=null; gameDraft=false; gameRefresh.Stop();
        loading=true; try { source.Text=text; } finally { loading=false; }
        Invalidate(); RenderGameCode(); status.Text="Game-code edit "+(redo ? "restored" : "undone")+". Preview before saving.";
    }
    private void DropGameBlock(DragEventArgs e)
    {
        ProjectToolsUi.Guard(()=>
        {
            if(e.Data.GetData(typeof(SourceBlockKind)) is not SourceBlockKind kind) return;
            if(gameDraft) UpdateGameInspector();
            GameCodeBlock? target=null;
            for(DependencyObject? node=e.OriginalSource as DependencyObject;node!=null && node!=workspace;node=VisualTreeHelper.GetParent(node))
                if(node is TreeViewItem { Tag:GameCodeBlock block }) { target=block; break; }
            string placement=target?.Kind is "if" or "else" or "while" or "for" or "do" or "scope" ? "inside selected" : gamePlacement.SelectedItem as string ?? "after selected";
            ApplyGameEdit(GameSession().Insert(target,new[]{SourceCodeBlock.Create(kind)},placement));
        }); e.Handled=true;
    }
}
