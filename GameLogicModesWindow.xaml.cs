using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Sm64DecompLevelViewer
{
    public partial class GameLogicModesWindow : Window
    {
        public class GameLogicModeItem : INotifyPropertyChanged
        {
            private bool _isEnabled;
            public int Id { get; set; }
            public string Name { get; set; }
            public string Description { get; set; }

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

        public ObservableCollection<GameLogicModeItem> Modes { get; } = new();
        public List<int> SelectedModeIds { get; private set; } = new();
        public bool IsSimultaneous { get; private set; } = true;

        public GameLogicModesWindow(List<int> initialSelectedIds, bool initialIsSimultaneous)
        {
            InitializeComponent();

            SimultaneousRadio.IsChecked = initialIsSimultaneous;
            CycleRadio.IsChecked = !initialIsSimultaneous;

            // Populate all 20 game logic modes
            var allModes = new List<GameLogicModeItem>
            {
                new GameLogicModeItem { Id = 1, Name = "Looking Stuck (Head Itch)", Description = "Forces Mario to periodically pause and look around scratching his head for a few seconds." },
                new GameLogicModeItem { Id = 2, Name = "Freeze GFX Knockback", Description = "Mario's visual model remains completely frozen during movement/knockback, only snapping/teleporting to his real physical location once he returns to a standing/stationary state." },
                new GameLogicModeItem { Id = 3, Name = "Ice Slide Frictionless", Description = "Removes ground friction, forcing Mario to continuously slide around on all flat surfaces." },
                new GameLogicModeItem { Id = 4, Name = "Moon Gravity (Floaty)", Description = "Reduces gravity, letting Mario float gently downward and jump higher." },
                new GameLogicModeItem { Id = 5, Name = "Heavy Gravity (Fast Fall)", Description = "Increases gravity, causing Mario to fall much faster and jump lower." },
                new GameLogicModeItem { Id = 6, Name = "Reverse Controls", Description = "Inverts the horizontal and vertical joystick directional inputs." },
                new GameLogicModeItem { Id = 7, Name = "Hyper Speed (2.5x)", Description = "Multiplies Mario's movement speed and velocities by 2.5x." },
                new GameLogicModeItem { Id = 8, Name = "Giant Mario (3x)", Description = "Scales Mario's visual model to 3x his normal size." },
                new GameLogicModeItem { Id = 9, Name = "Tiny Mario (0.3x)", Description = "Scales Mario's visual model to 0.3x his normal size." },
                new GameLogicModeItem { Id = 10, Name = "Paper Mario (Thin)", Description = "Scales Mario's model thickness to 0.1x, making him look paper-thin." },
                new GameLogicModeItem { Id = 11, Name = "Blind Camera (Zoom Jitter)", Description = "Causes the camera perspective to shake and jitter randomly." },
                new GameLogicModeItem { Id = 12, Name = "Continuous Twirl/Spin", Description = "Forces Mario into a twirling twister rotation whenever he is in mid-air." },
                new GameLogicModeItem { Id = 13, Name = "Periodical Up-Warp", Description = "Periodically warps Mario straight up into the air every few seconds." },
                new GameLogicModeItem { Id = 14, Name = "Super Bouncy Floors", Description = "Mario bounces high into the air whenever he touches a surface/ground." },
                new GameLogicModeItem { Id = 15, Name = "Flame Trail Spawner", Description = "Spawns a trail of red-hot fire particles directly underneath Mario's feet." },
                new GameLogicModeItem { Id = 16, Name = "Cap Roulette (Cap Swaps)", Description = "Automatically cycles Mario's power-up caps (Wing, Metal, Vanish) every 3 seconds." },
                new GameLogicModeItem { Id = 17, Name = "Health Drain Poison", Description = "Slowly depletes Mario's health over time, simulating poison." },
                new GameLogicModeItem { Id = 18, Name = "Invisibility Cloak", Description = "Makes Mario's visual model completely invisible." },
                new GameLogicModeItem { Id = 19, Name = "Forced Butt Slide", Description = "Forces Mario into a continuous slide on his butt when moving on ground." },
                new GameLogicModeItem { Id = 20, Name = "Random Warp Forward", Description = "Periodically teleports Mario 400 units forward in his facing direction." }
            };

            foreach (var item in allModes)
            {
                item.IsEnabled = initialSelectedIds.Contains(item.Id);
                item.PropertyChanged += Item_PropertyChanged;
                Modes.Add(item);
            }

            ModesListBox.ItemsSource = Modes;
            UpdateStatusText();
        }

        private void Item_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(GameLogicModeItem.IsEnabled))
            {
                UpdateStatusText();
            }
        }

        private void UpdateStatusText()
        {
            int count = Modes.Count(m => m.IsEnabled);
            StatusTextBlock.Text = $"{count} of {Modes.Count} modes enabled";
        }

        private void ModesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selectedItem = ModesListBox.SelectedItem as GameLogicModeItem;
            if (selectedItem != null)
            {
                DescriptionTextBlock.Text = selectedItem.Description;
            }
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var mode in Modes) mode.IsEnabled = true;
        }

        private void DeselectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var mode in Modes) mode.IsEnabled = false;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            SelectedModeIds = Modes.Where(m => m.IsEnabled).Select(m => m.Id).ToList();
            IsSimultaneous = SimultaneousRadio.IsChecked == true;

            if (SelectedModeIds.Count == 0)
            {
                MessageBox.Show("Please select at least one active game logic mode.", "Selection Empty", MessageBoxButton.OK, MessageBoxImage.Warning);
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
