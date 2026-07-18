using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;

namespace Sm64DecompLevelViewer
{
    public partial class M64DiagnosticsWindow : Window
    {
        public M64DiagnosticsWindow(string filename, List<string> lines)
        {
            InitializeComponent();
            TitleLabel.Text = $"M64 Diagnostic Report - {filename}";
            DiagnosticsTextBox.Text = string.Join(Environment.NewLine, lines);
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(DiagnosticsTextBox.Text);
                MessageBox.Show("Diagnostic report copied to clipboard!", "Copied", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to copy text: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
