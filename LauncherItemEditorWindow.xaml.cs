// File: UI/LauncherItemEditorWindow.xaml.cs
using Microsoft.Win32; // Keep for OpenFileDialog
using RightClickAppLauncher.Models;
using System.ComponentModel;
using System.IO;
using System.Windows;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog; // Alias to avoid conflict if Forms.OpenFileDialog is used
using FolderBrowserDialog = System.Windows.Forms.FolderBrowserDialog; // Add this for FolderBrowserDialog
using DialogResultForms = System.Windows.Forms.DialogResult;
using MessageBox = System.Windows.MessageBox;

namespace RightClickAppLauncher.UI
{
    public partial class LauncherItemEditorWindow : Window, INotifyPropertyChanged
    {
        private LauncherItem _item;
        public LauncherItem Item
        {
            get => _item;
            set { _item = value; OnPropertyChanged(nameof(Item)); }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public LauncherItemEditorWindow(LauncherItem itemToEdit)
        {
            InitializeComponent();
            // Clone all properties including X and Y position
            Item = new LauncherItem
            {
                Id = itemToEdit.Id,
                DisplayName = itemToEdit.DisplayName,
                ExecutablePath = itemToEdit.ExecutablePath,
                Arguments = itemToEdit.Arguments,
                IconPath = itemToEdit.IconPath,
                WorkingDirectory = itemToEdit.WorkingDirectory,
                X = itemToEdit.X,  // Preserve X position
                Y = itemToEdit.Y   // Preserve Y position
            };
            DataContext = this;
        }

        private void BrowseExecutable_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog // Using Microsoft.Win32.OpenFileDialog
            {
                Filter = "Executable Files (*.exe)|*.exe|All Files (*.*)|*.*",
                Title = "Select Executable File"
            };
            if(openFileDialog.ShowDialog() == true)
            {
                Item.ExecutablePath = openFileDialog.FileName;
                if(string.IsNullOrWhiteSpace(Item.DisplayName) || Item.DisplayName == "New Application")
                {
                    Item.DisplayName = Path.GetFileNameWithoutExtension(openFileDialog.FileName);
                }
                if(string.IsNullOrWhiteSpace(IconPathTextBox.Text))
                {
                    IconPathTextBox.Text = openFileDialog.FileName;
                }
                OnPropertyChanged(nameof(Item));
            }
        }

        // FIXED: Now accepts all image types
        private void BrowseIcon_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog // Using Microsoft.Win32.OpenFileDialog
            {
                Filter = "All Supported Images|*.ico;*.exe;*.dll;*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.tiff;*.tif|" +
                         "Icon Files (*.ico)|*.ico|" +
                         "Executable Files (*.exe;*.dll)|*.exe;*.dll|" +
                         "Image Files (*.png;*.jpg;*.jpeg;*.gif;*.bmp)|*.png;*.jpg;*.jpeg;*.gif;*.bmp|" +
                         "All Files (*.*)|*.*",
                Title = "Select Icon File or Image"
            };
            if(openFileDialog.ShowDialog() == true)
            {
                Item.IconPath = openFileDialog.FileName;
                OnPropertyChanged(nameof(Item));
            }
        }

        private void BrowseWorkingDirectory_Click(object sender, RoutedEventArgs e)
        {
            using(FolderBrowserDialog dialog = new FolderBrowserDialog()) // Using System.Windows.Forms.FolderBrowserDialog
            {
                dialog.Description = "Select Working Directory";
                dialog.ShowNewFolderButton = true; // Allow creating new folders

                // Set initial path if available
                if(!string.IsNullOrWhiteSpace(Item.WorkingDirectory) && Directory.Exists(Item.WorkingDirectory))
                {
                    dialog.SelectedPath = Item.WorkingDirectory;
                }
                else if(!string.IsNullOrWhiteSpace(Item.ExecutablePath) && File.Exists(Item.ExecutablePath))
                {
                    string exeDir = Path.GetDirectoryName(Item.ExecutablePath);
                    if(Directory.Exists(exeDir))
                    {
                        dialog.SelectedPath = exeDir;
                    }
                }
                // else, it will default to a system path like "My Computer" or "Desktop"

                // Show the dialog. Note: FolderBrowserDialog.ShowDialog() returns a System.Windows.Forms.DialogResult
                DialogResultForms result = dialog.ShowDialog(); // No owner window passed for simplicity

                if(result == DialogResultForms.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
                {
                    Item.WorkingDirectory = dialog.SelectedPath;
                    OnPropertyChanged(nameof(Item)); // Notify UI of changes in the Item object
                }
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if(string.IsNullOrWhiteSpace(Item.DisplayName))
            {
                MessageBox.Show("Display Name cannot be empty.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            if(string.IsNullOrWhiteSpace(Item.ExecutablePath))
            {
                MessageBox.Show("Executable Path cannot be empty.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            if(!File.Exists(System.Environment.ExpandEnvironmentVariables(Item.ExecutablePath ?? "")))
            {
                MessageBox.Show("Executable Path does not point to an existing file.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}