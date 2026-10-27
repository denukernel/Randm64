using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Sm64DecompLevelViewer.Services;

namespace Sm64DecompLevelViewer;
    public class ChannelSelectionWindow : Window
    {
        public HashSet<byte> SelectedChannels { get; } = new();

        public ChannelSelectionWindow(HashSet<byte> initiallySelected)
        {
            Title = "Select Channels";
            Width = 280;
            Height = 400;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(45, 45, 48));
            Foreground = Brushes.White;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;

            var grid = new Grid { Margin = new Thickness(15) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var title = new TextBlock 
            { 
                Text = "Select Audible Channels:", 
                FontWeight = FontWeights.Bold, 
                Margin = new Thickness(0, 0, 0, 10),
                Foreground = Brushes.White
            };
            grid.Children.Add(title);

            var list = new ListBox 
            { 
                Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)), 
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromRgb(63, 63, 70)),
                Margin = new Thickness(0, 0, 0, 10)
            };
            Grid.SetRow(list, 1);
            grid.Children.Add(list);

            var checkBoxes = new List<CheckBox>();
            for (byte i = 0; i < 16; i++)
            {
                var cb = new CheckBox 
                { 
                    Content = $"Channel {i}", 
                    Foreground = Brushes.White,
                    Margin = new Thickness(6, 4, 6, 4),
                    IsChecked = initiallySelected.Contains(i)
                };
                checkBoxes.Add(cb);
                list.Items.Add(cb);
            }

            var buttons = new StackPanel 
            { 
                Orientation = Orientation.Horizontal, 
                HorizontalAlignment = HorizontalAlignment.Right 
            };
            Grid.SetRow(buttons, 2);
            grid.Children.Add(buttons);

            var okBtn = new Button 
            { 
                Content = "OK", 
                Width = 75, 
                Height = 26, 
                Margin = new Thickness(0, 0, 8, 0), 
                Background = new SolidColorBrush(Color.FromRgb(0, 122, 204)), 
                Foreground = Brushes.White, 
                BorderThickness = new Thickness(0) 
            };
            okBtn.Click += (s, e) =>
            {
                for (byte i = 0; i < 16; i++)
                {
                    if (checkBoxes[i].IsChecked == true)
                    {
                        SelectedChannels.Add(i);
                    }
                }
                DialogResult = true;
                Close();
            };
            buttons.Children.Add(okBtn);

            var cancelBtn = new Button 
            { 
                Content = "Cancel", 
                Width = 75, 
                Height = 26, 
                Background = new SolidColorBrush(Color.FromRgb(62, 62, 66)), 
                Foreground = Brushes.White, 
                BorderThickness = new Thickness(0) 
            };
            cancelBtn.Click += (s, e) => { DialogResult = false; Close(); };
            buttons.Children.Add(cancelBtn);

            Content = grid;
        }
    }
