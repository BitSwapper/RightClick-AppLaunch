// File: SettingsWindow.xaml.cs
using RightClickAppLauncher.Managers;
using RightClickAppLauncher.Models;
using RightClickAppLauncher.Properties;
using RightClickAppLauncher.UI;
using System;
using System.Collections.ObjectModel; // For ObservableCollection
using System.Windows;
using MessageBox = System.Windows.MessageBox;

namespace RightClickAppLauncher
{
    public partial class SettingsWindow : Window
    {
        public ObservableCollection<LauncherItem> LauncherItems { get; set; }
        private readonly LauncherConfigManager _configManager;

        public SettingsWindow()
        {
            InitializeComponent();
            _configManager = new LauncherConfigManager();
            LauncherItems = new ObservableCollection<LauncherItem>(); // Initialize here
            DataContext = this; // Set DataContext for binding LauncherItems
            LoadSettings();
        }

        void LoadSettings()
        {
            LaunchOnStartupCheckBox.IsChecked = Settings.Default.LaunchOnStartup;
            LoadHotkeySettings();

            var loadedItems = _configManager.LoadLauncherItems();
            LauncherItems.Clear(); // Clear before loading
            foreach(var item in loadedItems)
            {
                LauncherItems.Add(item);
            }
        }

        void LoadHotkeySettings()
        {
            HotkeyCtrlCheckBox.IsChecked = Settings.Default.Hotkey_Ctrl;
            HotkeyAltCheckBox.IsChecked = Settings.Default.Hotkey_Alt;
            HotkeyShiftCheckBox.IsChecked = Settings.Default.Hotkey_Shift;
            HotkeyWinCheckBox.IsChecked = Settings.Default.Hotkey_Win;
        }

        bool ValidateSettings()
        {
            if(!(HotkeyCtrlCheckBox.IsChecked ?? false) &&
                !(HotkeyAltCheckBox.IsChecked ?? false) &&
                !(HotkeyShiftCheckBox.IsChecked ?? false) &&
                !(HotkeyWinCheckBox.IsChecked ?? false))
            {
                MessageBox.Show("Please select at least one modifier key for the activation hotkey.", "Invalid Hotkey", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            return true;
        }

        void SaveAppSettings() // Renamed from SaveSettings to avoid conflict
        {
            SaveStartupSetting();
            SaveHotkeySettings();
            _configManager.SaveLauncherItems(new System.Collections.Generic.List<LauncherItem>(LauncherItems));

            try { Settings.Default.Save(); }
            catch(Exception ex) { MessageBox.Show($"Error saving settings: {ex.Message}", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        void SaveStartupSetting()
        {
            bool wantsStartup = LaunchOnStartupCheckBox.IsChecked ?? false;
            if(Settings.Default.LaunchOnStartup == wantsStartup) return;

            Settings.Default.LaunchOnStartup = wantsStartup;
            try
            {
                if(wantsStartup) Native.OS_StartupManager.AddToStartup();
                else Native.OS_StartupManager.RemoveFromStartup();
            }
            catch(Exception ex)
            {
                MessageBox.Show($"Error updating startup setting: {ex.Message}", "Startup Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        void SaveHotkeySettings()
        {
            Settings.Default.Hotkey_Ctrl = HotkeyCtrlCheckBox.IsChecked ?? false;
            Settings.Default.Hotkey_Alt = HotkeyAltCheckBox.IsChecked ?? false;
            Settings.Default.Hotkey_Shift = HotkeyShiftCheckBox.IsChecked ?? false;
            Settings.Default.Hotkey_Win = HotkeyWinCheckBox.IsChecked ?? false;
        }

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            var newItem = new LauncherItem();
            var editor = new RightClickAppLauncher.UI.LauncherItemEditorWindow(newItem) { Owner = this };
            if(editor.ShowDialog() == true)
            {
                LauncherItems.Add(editor.Item);
            }
        }

        private void EditButton_Click(object sender, RoutedEventArgs e)
        {
            if(LauncherItemsListView.SelectedItem is LauncherItem selectedItem)
            {
                var editor = new RightClickAppLauncher.UI.LauncherItemEditorWindow(selectedItem) { Owner = this };
                if(editor.ShowDialog() == true)
                {
                    // Replace the item in the collection to reflect changes
                    int index = LauncherItems.IndexOf(selectedItem);
                    if(index != -1)
                    {
                        LauncherItems[index] = editor.Item;
                    }
                }
            }
            else
            {
                MessageBox.Show("Please select an item to edit.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void RemoveButton_Click(object sender, RoutedEventArgs e)
        {
            if(LauncherItemsListView.SelectedItem is LauncherItem selectedItem)
            {
                var result = MessageBox.Show($"Are you sure you want to remove '{selectedItem.DisplayName}'?", "Confirm Removal", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if(result == MessageBoxResult.Yes)
                {
                    LauncherItems.Remove(selectedItem);
                }
            }
            else
            {
                MessageBox.Show("Please select an item to remove.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void MoveUpButton_Click(object sender, RoutedEventArgs e)
        {
            if(LauncherItemsListView.SelectedItem is LauncherItem selectedItem)
            {
                int index = LauncherItems.IndexOf(selectedItem);
                if(index > 0)
                {
                    LauncherItems.Move(index, index - 1);
                    LauncherItemsListView.SelectedIndex = index - 1;
                }
            }
        }

        private void MoveDownButton_Click(object sender, RoutedEventArgs e)
        {
            if(LauncherItemsListView.SelectedItem is LauncherItem selectedItem)
            {
                int index = LauncherItems.IndexOf(selectedItem);
                if(index < LauncherItems.Count - 1 && index != -1)
                {
                    LauncherItems.Move(index, index + 1);
                    LauncherItemsListView.SelectedIndex = index + 1;
                }
            }
        }

        void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if(ValidateSettings())
            {
                SaveAppSettings();
                DialogResult = true; // Inform App.xaml.cs that settings were saved
                this.Close();
            }
        }

        void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            this.Close();
        }
    }
}