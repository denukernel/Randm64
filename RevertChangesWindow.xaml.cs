using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Sm64DecompLevelViewer
{
    public partial class RevertChangesWindow : Window
    {
        public class BackupItem : INotifyPropertyChanged
        {
            private bool _isEnabled = true;
            public string FullPath { get; set; } = string.Empty;
            public string RelativePath { get; set; } = string.Empty;

            public bool IsEnabled
            {
                get => _isEnabled;
                set
                {
                    if (_isEnabled != value)
                    {
                        _isEnabled = value;
                        OnPropertyChanged(nameof(IsEnabled));
                    }
                }
            }

            public event PropertyChangedEventHandler? PropertyChanged;
            protected void OnPropertyChanged(string propertyName)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        private readonly string _projectRoot;
        public ObservableCollection<BackupItem> Backups { get; } = new();

        public List<string> SelectedBackupFiles { get; private set; } = new();
        public bool RunGitRevert { get; private set; }

        public RevertChangesWindow(string projectRoot)
        {
            InitializeComponent();
            _projectRoot = projectRoot;

            BackupsListBox.ItemsSource = Backups;
            ScanForBackups();
        }

        private void ScanForBackups()
        {
            Backups.Clear();
            try
            {
                if (Directory.Exists(_projectRoot))
                {
                    var bakFiles = Directory.GetFiles(_projectRoot, "*.bak", SearchOption.AllDirectories);
                    foreach (var file in bakFiles)
                    {
                        string rel = Path.GetRelativePath(_projectRoot, file).Replace("\\", "/");
                        var item = new BackupItem
                        {
                            FullPath = file,
                            RelativePath = rel,
                            IsEnabled = true
                        };
                        item.PropertyChanged += Item_PropertyChanged;
                        Backups.Add(item);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error scanning backups: {ex.Message}", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            UpdateStatusText();
        }

        private void Item_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(BackupItem.IsEnabled))
            {
                UpdateStatusText();
            }
        }

        private void UpdateStatusText()
        {
            if (StatusTextBlock == null || GitRevertCheck == null) return;
            int backupsCount = Backups.Count(b => b.IsEnabled);
            StatusTextBlock.Text = $"{backupsCount} backup files selected, Git clean: {(GitRevertCheck.IsChecked == true ? "YES" : "NO")}";
        }

        private void GitRevertCheck_Toggle(object sender, RoutedEventArgs e)
        {
            UpdateStatusText();
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var b in Backups) b.IsEnabled = true;
            UpdateStatusText();
        }

        private void DeselectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var b in Backups) b.IsEnabled = false;
            UpdateStatusText();
        }

        private void RevertButton_Click(object sender, RoutedEventArgs e)
        {
            SelectedBackupFiles = Backups.Where(b => b.IsEnabled).Select(b => b.FullPath).ToList();
            RunGitRevert = GitRevertCheck.IsChecked == true;

            if (SelectedBackupFiles.Count == 0 && !RunGitRevert)
            {
                MessageBox.Show("Please select at least one backup file to restore or enable Git Revert.", "Nothing Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
