using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Sm64DecompLevelViewer.Services;

namespace Sm64DecompLevelViewer
{
    public partial class ScanDebuggerWindow : Window
    {
        public ScanDebuggerWindow()
        {
            InitializeComponent();
            RefreshLogText();
            LogService.OnLogAdded += OnLogAdded;
        }

        private void OnLogAdded(string logEntry)
        {
            Dispatcher.Invoke(() => RefreshLogText());
        }

        private void RefreshLogText()
        {
            string filter = SearchTextBox?.Text?.Trim() ?? "";
            var logs = LogService.History;

            if (!string.IsNullOrEmpty(filter))
            {
                logs = logs.Where(l => l.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            LogTextBox.Text = string.Join(Environment.NewLine, logs);
            LogScrollViewer.ScrollToEnd();
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            RefreshLogText();
        }

        private void CopyLogs_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(LogTextBox.Text);
                MessageBox.Show("Logs copied to clipboard!", "Scan Debugger", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to copy logs: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenLogFile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string path = LogService.GetLogPath();
                if (File.Exists(path))
                {
                    Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open log file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ClearLogs_Click(object sender, RoutedEventArgs e)
        {
            LogService.Clear();
            RefreshLogText();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            LogService.OnLogAdded -= OnLogAdded;
            base.OnClosed(e);
        }
    }
}
