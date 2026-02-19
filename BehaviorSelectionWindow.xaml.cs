using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Sm64DecompLevelViewer
{
    public partial class BehaviorSelectionWindow : Window
    {
        private List<string> _allBehaviors;
        public string? SelectedBehavior { get; private set; }

        public BehaviorSelectionWindow(List<string> behaviors, string? currentBehavior)
        {
            InitializeComponent();
            _allBehaviors = behaviors;
            BehaviorListBox.ItemsSource = _allBehaviors;
            
            if (!string.IsNullOrEmpty(currentBehavior))
            {
                SearchTextBox.Text = currentBehavior;
                BehaviorListBox.SelectedItem = _allBehaviors.FirstOrDefault(b => b == currentBehavior);
                if (BehaviorListBox.SelectedItem != null)
                {
                    BehaviorListBox.ScrollIntoView(BehaviorListBox.SelectedItem);
                }
            }
            
            SearchTextBox.Focus();
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string filter = SearchTextBox.Text.ToLower();
            if (string.IsNullOrWhiteSpace(filter))
            {
                BehaviorListBox.ItemsSource = _allBehaviors;
            }
            else
            {
                BehaviorListBox.ItemsSource = _allBehaviors
                    .Where(b => b.ToLower().Contains(filter))
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

        private void BehaviorListBox_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            ConfirmSelection();
        }

        private void ConfirmSelection()
        {
            if (BehaviorListBox.SelectedItem != null)
            {
                SelectedBehavior = BehaviorListBox.SelectedItem.ToString();
                DialogResult = true;
                Close();
            }
            else if (!string.IsNullOrEmpty(SearchTextBox.Text))
            {
                // Allow custom behavior names
                SelectedBehavior = SearchTextBox.Text;
                DialogResult = true;
                Close();
            }
        }
    }
}
