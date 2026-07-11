using System.Windows;

namespace Sm64DecompLevelViewer
{
    public partial class MusicEditorHelpWindow : Window
    {
        public MusicEditorHelpWindow()
        {
            InitializeComponent();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
