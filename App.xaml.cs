using System.Configuration;
using System.Data;
using System.Windows;
using Sm64DecompLevelViewer.Services;

namespace Sm64DecompLevelViewer
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            AppearanceService.LoadAndApplyAppearance();
            base.OnStartup(e);
        }
    }

}
