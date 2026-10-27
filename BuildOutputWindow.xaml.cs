using Sm64DecompLevelViewer.Services.Build;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Sm64DecompLevelViewer.Services;

namespace Sm64DecompLevelViewer
{
    public partial class BuildOutputWindow : Window
    {
        private readonly string _projectRoot;
        private readonly AppSettings? _settings;
        private readonly string? _gitUrlToClone;
        private readonly bool _isRevertMode = false;
        private readonly List<string>? _bakFilesToRestore;
        private readonly bool _runGitRevert = false;
        private BuildWorkflow? _workflow;
        private readonly System.Collections.Concurrent.ConcurrentQueue<BuildProgress> _updates = new();
        private int _flushScheduled;
        private bool _isBuildSuccessful => _workflow?.IsSuccessful == true;


        public bool IsSuccessful => _isBuildSuccessful;

        // Constructor for compilation mode
        public BuildOutputWindow(string projectRoot, AppSettings settings)
        {
            InitializeComponent();
            _projectRoot = projectRoot;
            _settings = settings;
            _gitUrlToClone = null;
            
            Loaded += async (s, e) => await StartBuildAsync();
        }

        // Constructor for clean & clone mode
        public BuildOutputWindow(string projectRoot, string gitUrlToClone)
        {
            InitializeComponent();
            _projectRoot = projectRoot;
            _settings = null;
            _gitUrlToClone = gitUrlToClone;
            
            Loaded += async (s, e) => await StartCleanAndCloneAsync();
        }

        // Constructor for revert mode
        public BuildOutputWindow(string projectRoot, List<string>? bakFilesToRestore, bool runGitRevert)
        {
            InitializeComponent();
            _projectRoot = projectRoot;
            _settings = null;
            _gitUrlToClone = null;
            _isRevertMode = true;
            _bakFilesToRestore = bakFilesToRestore;
            _runGitRevert = runGitRevert;
            
            Loaded += async (s, e) => await StartRevertSourceAsync();
        }

        // Constructor for revert mode (legacy fallback)
        public BuildOutputWindow(string projectRoot, bool isRevertMode)
            : this(projectRoot, null, true)
        {
        }

        private void CloseButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            Close();
        }

        private Task StartBuildAsync() => Workflow.StartBuildAsync();
        private Task StartCleanAndCloneAsync() => Workflow.StartCleanAndCloneAsync();
        private Task StartRevertSourceAsync() => Workflow.StartRevertSourceAsync();
        private BuildWorkflow Workflow => _workflow ??= new BuildWorkflow(_projectRoot, _settings, _gitUrlToClone, _bakFilesToRestore, _runGitRevert, Report);
        private void Report(BuildProgress update)
        {
            _updates.Enqueue(update);
            if (System.Threading.Interlocked.Exchange(ref _flushScheduled, 1) == 0)
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(FlushUpdates));
        }

        private void FlushUpdates()
        {
            var log = new System.Text.StringBuilder();
            while (_updates.TryDequeue(out var update))
            {
                if (update.Title != null) HeaderTitleText.Text = update.Title;
                if (update.Status != null) StatusText.Text = update.Status;
                if (update.Log != null) log.Append(update.Log);
                if (update.CanClose.HasValue) CloseButton.IsEnabled = update.CanClose.Value;
                if (update.Tone.HasValue) MainStatusBar.Background = new System.Windows.Media.SolidColorBrush(
                    update.Tone == BuildTone.Success ? System.Windows.Media.Color.FromRgb(40, 167, 69) : System.Windows.Media.Color.FromRgb(220, 53, 69));
            }
            if (log.Length > 0) { LogTextBox.AppendText(log.ToString()); LogScrollViewer.ScrollToEnd(); }
            System.Threading.Interlocked.Exchange(ref _flushScheduled, 0);
            if (!_updates.IsEmpty && System.Threading.Interlocked.Exchange(ref _flushScheduled, 1) == 0)
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(FlushUpdates));
        }
    }
}
