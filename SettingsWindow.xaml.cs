using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using RightClickAppLauncher.Managers;
using RightClickAppLauncher.Models;
using RightClickAppLauncher.Properties;
using RightClickAppLauncher.UI;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace RightClickAppLauncher;

public partial class SettingsWindow : Window
{
    public ObservableCollection<LauncherItem> LauncherItems { get; set; }
    readonly LauncherConfigManager _configManager;

    public SettingsWindow()
    {
        InitializeComponent();
        _configManager = new LauncherConfigManager();
        LauncherItems = new ObservableCollection<LauncherItem>();
        DataContext = this;
        LoadSettings();
        SetupPreviewHandlers();
    }

    void LoadSettings()
    {
        LaunchOnStartupCheckBox.IsChecked = Settings.Default.LaunchOnStartup;
        LoadHotkeySettings();

        // Load icon size and spacing settings
        IconSizeSlider.Value = Settings.Default.IconSize;
        IconSpacingSlider.Value = Settings.Default.IconSpacing;

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

        // Save icon size and spacing settings
        Settings.Default.IconSize = IconSizeSlider.Value;
        Settings.Default.IconSpacing = IconSpacingSlider.Value;

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

    void SetupPreviewHandlers()
    {
        IconSizeSlider.ValueChanged += UpdatePreview;
        IconSpacingSlider.ValueChanged += UpdatePreview;

        // Initial preview update
        UpdatePreview(null, null);
    }

    void UpdatePreview(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if(PreviewIcon2Border == null || PreviewIcon3Border == null) return;

        double iconSize = IconSizeSlider.Value;
        double spacing = IconSpacingSlider.Value;
        double totalSize = iconSize + 10; // 10 is for border padding (5 * 2)

        // Position second icon (to the right of first icon)
        Canvas.SetLeft(PreviewIcon2Border, 10 + totalSize + spacing);
        Canvas.SetTop(PreviewIcon2Border, 10);

        // Position third icon (below first icon)
        Canvas.SetLeft(PreviewIcon3Border, 10);
        Canvas.SetTop(PreviewIcon3Border, 10 + totalSize + spacing);
    }

    void AddButton_Click(object sender, RoutedEventArgs e)
    {
        var newItem = new LauncherItem();
        var editor = new LauncherItemEditorWindow(newItem) { Owner = this };
        if(editor.ShowDialog() == true)
        {
            LauncherItems.Add(editor.Item);
        }
    }

    void AutoAddMultipleButton_Click(object sender, RoutedEventArgs e)
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
                string iconPath = filePath;

                if(Path.GetExtension(filePath).Equals(".lnk", StringComparison.OrdinalIgnoreCase))
                {
                    string targetPath = Utils.ShortcutResolver.ResolveShortcut(filePath);
                    if(!string.IsNullOrEmpty(targetPath) && File.Exists(targetPath))
                    {
                        executablePath = targetPath;
                        displayName = Path.GetFileNameWithoutExtension(filePath);
                    }
                    else
                    {
                        displayName = Path.GetFileNameWithoutExtension(filePath) + " (Shortcut)";
                    }
                }

                double newY = 10;
                if(LauncherItems.Any())
                {
                    newY = LauncherItems.Max(item => item.Y) + 40;
                }
                double newX = 10;

                var newItem = new LauncherItem
                {
                    DisplayName = displayName,
                    ExecutablePath = executablePath,
                    IconPath = iconPath,
                    Arguments = string.Empty,
                    WorkingDirectory = string.Empty,
                    X = newX,
                    Y = newY
                };
                LauncherItems.Add(newItem);
            }
        }
    }

    void EditButton_Click(object sender, RoutedEventArgs e)
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

    void RemoveButton_Click(object sender, RoutedEventArgs e)
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

    void MoveUpButton_Click(object sender, RoutedEventArgs e)
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

    void MoveDownButton_Click(object sender, RoutedEventArgs e)
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