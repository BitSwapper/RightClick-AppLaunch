// File: UI/ManageLayoutsWindow.xaml.cs
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Newtonsoft.Json;
using RightClickAppLauncher.Models;
using MessageBox = System.Windows.MessageBox;

namespace RightClickAppLauncher.UI
{
    public partial class ManageLayoutsWindow : Window
    {
        private List<LayoutDisplayInfo> _layoutDisplayInfos;
        public NamedLayout SelectedLayoutToLoad { get; private set; }
        public bool LayoutsModified { get; private set; }

        public ManageLayoutsWindow(List<NamedLayout> layouts)
        {
            InitializeComponent();
            LoadLayoutsForDisplay(layouts);
        }

        private void LoadLayoutsForDisplay(List<NamedLayout> layouts)
        {
            _layoutDisplayInfos = new List<LayoutDisplayInfo>();

            foreach(var layout in layouts.OrderBy(l => l.Name))
            {
                var displayInfo = new LayoutDisplayInfo
                {
                    Layout = layout,
                    Name = layout.Name,
                    SavedDate = layout.SavedDate,
                    WindowSizeDisplay = $"{layout.WindowWidth:0}x{layout.WindowHeight:0}"
                };

                // Parse the JSON to get item count
                try
                {
                    var items = JsonConvert.DeserializeObject<List<LauncherItem>>(layout.LayoutJson);
                    displayInfo.ItemCount = items?.Count ?? 0;
                }
                catch
                {
                    displayInfo.ItemCount = 0;
                }

                _layoutDisplayInfos.Add(displayInfo);
            }

            LayoutsDataGrid.ItemsSource = _layoutDisplayInfos;
        }

        private void RenameLayout_Click(object sender, RoutedEventArgs e) => RenameSelectedLayout();
        private void RenameButton_Click(object sender, RoutedEventArgs e) => RenameSelectedLayout();

        private void RenameSelectedLayout()
        {
            var selected = LayoutsDataGrid.SelectedItem as LayoutDisplayInfo;
            if(selected == null)
            {
                MessageBox.Show("Please select a layout to rename.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var inputDialog = new InputDialog("Enter new name for the layout:", selected.Name) { Owner = this };
            if(inputDialog.ShowDialog() == true)
            {
                string newName = inputDialog.ResponseText.Trim();
                if(string.IsNullOrWhiteSpace(newName))
                {
                    MessageBox.Show("Layout name cannot be empty.", "Invalid Name", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Check for duplicate names
                if(_layoutDisplayInfos.Any(l => l != selected && l.Name.Equals(newName, StringComparison.OrdinalIgnoreCase)))
                {
                    MessageBox.Show("A layout with this name already exists.", "Duplicate Name", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                selected.Name = newName;
                selected.Layout.Name = newName;
                LayoutsModified = true;
                LayoutsDataGrid.Items.Refresh();
            }
        }

        private void DeleteLayout_Click(object sender, RoutedEventArgs e) => DeleteSelectedLayout();
        private void DeleteButton_Click(object sender, RoutedEventArgs e) => DeleteSelectedLayout();

        private void DeleteSelectedLayout()
        {
            var selected = LayoutsDataGrid.SelectedItem as LayoutDisplayInfo;
            if(selected == null)
            {
                MessageBox.Show("Please select a layout to delete.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show($"Are you sure you want to delete the layout '{selected.Name}'?",
                                       "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if(result == MessageBoxResult.Yes)
            {
                _layoutDisplayInfos.Remove(selected);
                LayoutsModified = true;
                LayoutsDataGrid.ItemsSource = null;
                LayoutsDataGrid.ItemsSource = _layoutDisplayInfos;
            }
        }

        private void LoadLayout_Click(object sender, RoutedEventArgs e) => LoadSelectedLayout();
        private void LoadButton_Click(object sender, RoutedEventArgs e) => LoadSelectedLayout();

        private void LoadSelectedLayout()
        {
            var selected = LayoutsDataGrid.SelectedItem as LayoutDisplayInfo;
            if(selected == null)
            {
                MessageBox.Show("Please select a layout to load.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            SelectedLayoutToLoad = selected.Layout;
            DialogResult = true;
            Close();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = LayoutsModified;
            Close();
        }

        public List<NamedLayout> GetUpdatedLayouts()
        {
            return _layoutDisplayInfos.Select(d => d.Layout).ToList();
        }
    }

    public class LayoutDisplayInfo : INotifyPropertyChanged
    {
        private string _name;

        public NamedLayout Layout { get; set; }

        public string Name
        {
            get => _name;
            set
            {
                _name = value;
                OnPropertyChanged(nameof(Name));
            }
        }

        public DateTime SavedDate { get; set; }
        public int ItemCount { get; set; }
        public string WindowSizeDisplay { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}