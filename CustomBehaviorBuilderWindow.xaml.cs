using System.IO;
using System.Linq;
using System.Windows;
using Sm64DecompLevelViewer.Services;

namespace Sm64DecompLevelViewer;

public partial class CustomBehaviorBuilderWindow : Window
{
    private readonly string _projectRoot;
    public CustomBehaviorBuilderWindow(string projectRoot)
    {
        InitializeComponent();
        _projectRoot = projectRoot;
        var models = new ObjectParser().ParseModelIds(Path.Combine(projectRoot, "include", "model_ids.h")).Keys.Append("MODEL_NONE").Distinct().OrderBy(value => value).ToList();
        ActorModelComboBox.ItemsSource = models;
        ActorModelComboBox.SelectedItem = models.Contains("MODEL_GOOMBA") ? "MODEL_GOOMBA" : "MODEL_NONE";
    }
    private SourceEditPlan Prepare()
    {
        int interactions = new[] { IntSolid, IntDamage, IntCollectible }.Count(box => box.IsChecked == true);
        if (interactions > 1) throw new InvalidDataException("select one interaction; interaction types cannot be combined by this template");
        string action = AiPatrol.IsChecked == true ? "patrol" : AiChase.IsChecked == true ? "chase" : AiJump.IsChecked == true ? "jump" : AiSpin.IsChecked == true ? "spin" : AiFloat.IsChecked == true ? "float" : "passive";
        string interaction = IntDamage.IsChecked == true ? "damage" : IntCollectible.IsChecked == true ? "coin" : IntSolid.IsChecked == true ? "solid" : "none";
        return CustomBehaviorGenerator.Prepare(_projectRoot, BehaviorNameTextBox.Text.Trim(), ActorModelComboBox.SelectedItem as string ?? "MODEL_NONE", action, interaction);
    }
    private void NextButton_Click(object sender, RoutedEventArgs e) => MainTabControl.SelectedIndex = 1;
    private void PreviewButton_Click(object sender, RoutedEventArgs e)
    {
        try { GeneratedSource.Text = Prepare().Preview(_projectRoot); MainTabControl.SelectedIndex = 2; }
        catch (System.Exception ex) { StatusTextBlock.Text = ex.Message; }
    }
    private void CreateBehavior_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var plan = Prepare();
            plan.Commit();
            StatusTextBlock.Text = "Behavior sources registered; build the ROM to validate the generated code";
            MessageBox.Show("Behavior sources registered with first backups. choose the preferred model in a level that loads it, then build the ROM.", "Behavior registered");
        }
        catch (System.Exception ex) { StatusTextBlock.Text = ex.Message; MessageBox.Show(ex.Message, "Behavior was not registered"); }
    }
    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();
}
