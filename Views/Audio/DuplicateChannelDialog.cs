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
    public class DuplicateChannelDialog : Window
    {
        public byte SelectedChannel { get; private set; }
        private ComboBox _comboBox;
        
        public DuplicateChannelDialog(List<byte> activeChannels)
        {
            Title = "Duplicate Channel";
            Width = 300;
            Height = 150;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(45, 45, 48));
            Foreground = Brushes.White;
            ResizeMode = ResizeMode.NoResize;
            
            var stackPanel = new StackPanel { Margin = new Thickness(15) };
            
            var label = new TextBlock 
            { 
                Text = "Select target channel to copy notes to:", 
                Margin = new Thickness(0, 0, 0, 10),
                Foreground = Brushes.LightGray
            };
            stackPanel.Children.Add(label);
            
            _comboBox = new ComboBox 
            { 
                Height = 25, 
                Margin = new Thickness(0, 0, 0, 15),
                Background = new SolidColorBrush(Color.FromRgb(51, 51, 55)),
                Foreground = Brushes.Black
            };
            
            for (byte ch = 0; ch < 16; ch++)
            {
                string suffix = activeChannels.Contains(ch) ? " (Active)" : "";
                _comboBox.Items.Add(new ComboBoxItem { Content = $"{ch}{suffix}", Tag = ch });
            }
            _comboBox.SelectedIndex = 0;
            stackPanel.Children.Add(_comboBox);
            
            var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            
            var okButton = new Button 
            { 
                Content = "Duplicate", 
                Width = 80, 
                Height = 25, 
                IsDefault = true,
                Background = new SolidColorBrush(Color.FromRgb(0, 122, 204)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Margin = new Thickness(0, 0, 10, 0)
            };
            okButton.Click += (s, e) => 
            {
                if (_comboBox.SelectedItem is ComboBoxItem item && item.Tag is byte ch)
                {
                    SelectedChannel = ch;
                    DialogResult = true;
                }
            };
            buttonPanel.Children.Add(okButton);
            
            var cancelButton = new Button 
            { 
                Content = "Cancel", 
                Width = 80, 
                Height = 25, 
                IsCancel = true,
                Background = new SolidColorBrush(Color.FromRgb(107, 107, 107)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0)
            };
            buttonPanel.Children.Add(cancelButton);
            stackPanel.Children.Add(buttonPanel);
            
            Content = stackPanel;
        }
    }
