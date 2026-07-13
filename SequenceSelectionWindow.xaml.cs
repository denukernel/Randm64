using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Sm64DecompLevelViewer
{
    public class SequenceSelectItem
    {
        public string DisplayName { get; set; } = "";
        public string FilePath { get; set; } = "";
        
        public override string ToString()
        {
            return DisplayName;
        }
    }

    public partial class SequenceSelectionWindow : Window
    {
        private List<SequenceSelectItem> _allItems;
        public SequenceSelectItem? SelectedItem { get; private set; }

        public SequenceSelectionWindow(List<SequenceSelectItem> items, string? currentPath)
        {
            InitializeComponent();
            _allItems = items;
            SequenceListBox.ItemsSource = _allItems;
            
            if (!string.IsNullOrEmpty(currentPath))
            {
                var currentItem = _allItems.FirstOrDefault(i => i.FilePath.Equals(currentPath, StringComparison.OrdinalIgnoreCase));
                if (currentItem != null)
                {
                    SequenceListBox.SelectedItem = currentItem;
                    SequenceListBox.ScrollIntoView(currentItem);
                }
            }
            
            SearchTextBox.Focus();
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string filter = SearchTextBox.Text.ToLower();
            if (string.IsNullOrWhiteSpace(filter))
            {
                SequenceListBox.ItemsSource = _allItems;
            }
            else
            {
                SequenceListBox.ItemsSource = _allItems
                    .Where(i => i.DisplayName.ToLower().Contains(filter))
                    .ToList();
            }
        }

        private void SelectButton_Click(object sender, RoutedEventArgs e)
        {
            ConfirmSelection();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void SequenceListBox_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            DependencyObject? dep = e.OriginalSource as DependencyObject;
            while (dep != null && !(dep is ListBoxItem))
            {
                dep = System.Windows.Media.VisualTreeHelper.GetParent(dep);
            }

            if (dep is ListBoxItem)
            {
                ConfirmSelection();
            }
        }

        private void ConfirmSelection()
        {
            if (SequenceListBox.SelectedItem is SequenceSelectItem selected)
            {
                SelectedItem = selected;
                DialogResult = true;
                Close();
            }
        }
    }
}
