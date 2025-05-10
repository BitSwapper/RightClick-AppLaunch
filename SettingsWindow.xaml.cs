// File: SettingsWindow.xaml.cs
using Microsoft.Win32; // For OpenFileDialog
using RightClickAppLauncher.Managers;
using RightClickAppLauncher.Models;
using RightClickAppLauncher.Properties;
using RightClickAppLauncher.UI;
using System;
using System.Collections.ObjectModel;
using System.IO; // For Path operations
using System.Windows;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

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
            LauncherItems = new ObservableCollection<LauncherItem>();
            DataContext = this;
            LoadSettings();
        }

        void LoadSettings()
        {
            LaunchOnStartupCheckBox.IsChecked = Settings.Default.LaunchOnStartup;
            LoadHotkeySettings();

            var loadedItems = _configManager.LoadLauncherItems();
            LauncherItems.Clear();
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

        void SaveAppSettings()
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

        private void AddButton_Click(object sender, RoutedEventArgs e) // This is "Add Manually..."
        {
            var newItem = new LauncherItem();
            // Set default X, Y for new items added manually if desired, 
            // or let them default to 0,0 and be draggable in the menu.
            // For simplicity, we'll let them default as per LauncherItem constructor.
            // newItem.X = 10; 
            // newItem.Y = (LauncherItems.Count * 40) + 10; // Basic stacking

            var editor = new LauncherItemEditorWindow(newItem) { Owner = this };
            if(editor.ShowDialog() == true)
            {
                LauncherItems.Add(editor.Item);
            }
        }

        // NEW EVENT HANDLER
        private void AutoAddMultipleButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Title = "Select Files to Add to Launcher",
                Filter = "Applications (*.exe)|*.exe|Shortcuts (*.lnk)|*.lnk|All Files (*.*)|*.*",
                Multiselect = true
            };

            if(openFileDialog.ShowDialog() == true)
            {
                foreach(string filePath in openFileDialog.FileNames)
                {
                    string displayName = Path.GetFileNameWithoutExtension(filePath);
                    string executablePath = filePath;
                    string iconPath = filePath; // Default to using the file itself for icon extraction

                    // For .lnk files, we should try to resolve the target
                    if(Path.GetExtension(filePath).Equals(".lnk", StringComparison.OrdinalIgnoreCase))
                    {
                        string targetPath = Utils.ShortcutResolver.ResolveShortcut(filePath);
                        if(!string.IsNullOrEmpty(targetPath) && File.Exists(targetPath))
                        {
                            executablePath = targetPath; // Use the resolved target as the executable
                            // IconPath can still be the .lnk file as it often has a custom icon,
                            // or you could set it to targetPath too.
                            // iconPath = targetPath; // Option: use target's icon
                            displayName = Path.GetFileNameWithoutExtension(filePath); // Keep .lnk file name as display name
                        }
                        else
                        {
                            // If .lnk can't be resolved, still add the .lnk itself.
                            // Windows ShellExecute can often run .lnk files directly.
                            displayName = Path.GetFileNameWithoutExtension(filePath) + " (Shortcut)";
                        }
                    }

                    // Basic stacking for new items. Adjust as needed.
                    double newY = 10;
                    if(LauncherItems.Any())
                    {
                        newY = LauncherItems.Max(item => item.Y) + 40; // 40 pixels below the lowest current item
                                                                       // Ensure it doesn't go too far down, maybe reset X every few items.
                    }
                    double newX = 10; // Or some other logic for X positioning


                    var newItem = new LauncherItem
                    {
                        DisplayName = displayName,
                        ExecutablePath = executablePath,
                        IconPath = iconPath,
                        Arguments = string.Empty, // User can edit later
                        WorkingDirectory = string.Empty, // User can edit later
                        X = newX,
                        Y = newY
                    };
                    LauncherItems.Add(newItem);
                }
            }
        }


        private void EditButton_Click(object sender, RoutedEventArgs e)
        {
            if(LauncherItemsListView.SelectedItem is LauncherItem selectedItem)
            {
                var editor = new LauncherItemEditorWindow(selectedItem) { Owner = this };
                if(editor.ShowDialog() == true)
                {
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
                DialogResult = true;
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