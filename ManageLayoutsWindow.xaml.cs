using Microsoft.Win32;
using Newtonsoft.Json;
using RightClickAppLauncher.Models;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace RightClickAppLauncher.UI
{
    public partial class ManageLayoutsWindow : Window
    {
        public ObservableCollection<NamedLayout> Layouts { get; set; }
        public bool LayoutsModified { get; private set; }
        public NamedLayout SelectedLayoutToLoad { get; private set; }

        public ManageLayoutsWindow(System.Collections.Generic.List<NamedLayout> layouts)
        {
            InitializeComponent();
            Layouts = new ObservableCollection<NamedLayout>(layouts);
            DataContext = this;
            LayoutsModified = false;
        }

        public System.Collections.Generic.List<NamedLayout> GetUpdatedLayouts()
        {
            return Layouts.ToList();
        }

        private void LoadButton_Click(object sender, RoutedEventArgs e)
        {
            if(LayoutsListView.SelectedItem is NamedLayout selectedLayout)
            {
                SelectedLayoutToLoad = selectedLayout;
                DialogResult = true;
                Close();
            }
            else
            {
                MessageBox.Show("Please select a layout to load.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void RenameButton_Click(object sender, RoutedEventArgs e)
        {
            if(LayoutsListView.SelectedItem is NamedLayout selectedLayout)
            {
                var inputDialog = new InputDialog("Enter new name for layout:", selectedLayout.Name)
                {
                    Owner = this
                };

                if(inputDialog.ShowDialog() == true)
                {
                    string newName = inputDialog.ResponseText.Trim();
                    if(string.IsNullOrWhiteSpace(newName))
                    {
                        MessageBox.Show("Layout name cannot be empty.", "Invalid Name", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    // Check if name already exists
                    if(Layouts.Any(l => l != selectedLayout && l.Name.Equals(newName, StringComparison.OrdinalIgnoreCase)))
                    {
                        MessageBox.Show($"A layout named '{newName}' already exists.", "Duplicate Name", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    selectedLayout.Name = newName;
                    LayoutsModified = true;

                    // Refresh the ListView to show the new name
                    LayoutsListView.Items.Refresh();
                }
            }
            else
            {
                MessageBox.Show("Please select a layout to rename.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if(LayoutsListView.SelectedItem is NamedLayout selectedLayout)
            {
                var result = MessageBox.Show($"Are you sure you want to delete the layout '{selectedLayout.Name}'?",
                                           "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);

                if(result == MessageBoxResult.Yes)
                {
                    Layouts.Remove(selectedLayout);
                    LayoutsModified = true;
                }
            }
            else
            {
                MessageBox.Show("Please select a layout to delete.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            if(LayoutsListView.SelectedItem is NamedLayout selectedLayout)
            {
                var saveDialog = new SaveFileDialog
                {
                    Title = "Export Layout",
                    Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                    FileName = $"{selectedLayout.Name}.json",
                    DefaultExt = ".json"
                };

                if(saveDialog.ShowDialog() == true)
                {
                    try
                    {
                        string json = JsonConvert.SerializeObject(selectedLayout, Formatting.Indented);
                        File.WriteAllText(saveDialog.FileName, json);
                        MessageBox.Show($"Layout exported successfully to {saveDialog.FileName}", "Export Complete",
                                      MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    catch(Exception ex)
                    {
                        MessageBox.Show($"Error exporting layout: {ex.Message}", "Export Error",
                                      MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            else
            {
                MessageBox.Show("Please select a layout to export.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void ImportButton_Click(object sender, RoutedEventArgs e)
        {
            var openDialog = new OpenFileDialog
            {
                Title = "Import Layout",
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                DefaultExt = ".json"
            };

            if(openDialog.ShowDialog() == true)
            {
                try
                {
                    string json = File.ReadAllText(openDialog.FileName);
                    NamedLayout importedLayout = JsonConvert.DeserializeObject<NamedLayout>(json);

                    if(importedLayout == null)
                    {
                        MessageBox.Show("Invalid layout file.", "Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    // Check if layout name already exists
                    if(Layouts.Any(l => l.Name.Equals(importedLayout.Name, StringComparison.OrdinalIgnoreCase)))
                    {
                        var result = MessageBox.Show($"A layout named '{importedLayout.Name}' already exists. Do you want to overwrite it?",
                                                   "Duplicate Name", MessageBoxButton.YesNo, MessageBoxImage.Warning);

                        if(result == MessageBoxResult.No)
                        {
                            var inputDialog = new InputDialog("Enter a new name for the imported layout:",
                                                            importedLayout.Name + " (Imported)")
                            {
                                Owner = this
                            };

                            if(inputDialog.ShowDialog() == true)
                            {
                                string newName = inputDialog.ResponseText.Trim();
                                if(string.IsNullOrWhiteSpace(newName))
                                {
                                    return;
                                }
                                importedLayout.Name = newName;
                            }
                            else
                            {
                                return;
                            }
                        }
                        else
                        {
                            // Remove existing layout with same name
                            var existingLayout = Layouts.FirstOrDefault(l => l.Name.Equals(importedLayout.Name, StringComparison.OrdinalIgnoreCase));
                            if(existingLayout != null)
                            {
                                Layouts.Remove(existingLayout);
                            }
                        }
                    }

                    // Ensure imported layout has valid icon settings (for backward compatibility)
                    if(importedLayout.IconSize <= 0)
                        importedLayout.IconSize = 20; // Default

                    if(importedLayout.IconSpacing < 0)
                        importedLayout.IconSpacing = 10; // Default

                    Layouts.Add(importedLayout);
                    LayoutsModified = true;

                    MessageBox.Show($"Layout '{importedLayout.Name}' imported successfully.", "Import Complete",
                                  MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch(Exception ex)
                {
                    MessageBox.Show($"Error importing layout: {ex.Message}", "Import Error",
                                  MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = LayoutsModified;
            Close();
        }
    }
}