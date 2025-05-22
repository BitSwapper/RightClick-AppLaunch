using System.ComponentModel;
using System.IO;
using System.Windows;
using RightClickAppLauncher.Models;
using DialogResultForms = System.Windows.Forms.DialogResult;
using FolderBrowserDialog = System.Windows.Forms.FolderBrowserDialog;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace RightClickAppLauncher.UI;

public partial class LauncherItemEditorWindow : Window, INotifyPropertyChanged
{
    LauncherItem _item;
    public LauncherItem Item
    {
        get => _item;
        set { _item = value; OnPropertyChanged(nameof(Item)); }
    }

    public event PropertyChangedEventHandler PropertyChanged;

    protected void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public LauncherItemEditorWindow(LauncherItem itemToEdit)
    {
        InitializeComponent();
        Item = new LauncherItem
        {
            Id = itemToEdit.Id,
            DisplayName = itemToEdit.DisplayName,
            ExecutablePath = itemToEdit.ExecutablePath,
            Arguments = itemToEdit.Arguments,
            IconPath = itemToEdit.IconPath,
            WorkingDirectory = itemToEdit.WorkingDirectory,
            X = itemToEdit.X,
            Y = itemToEdit.Y
        };
        DataContext = this;
    }

    void BrowseExecutable_Click(object sender, RoutedEventArgs e)
    {
        OpenFileDialog openFileDialog = new OpenFileDialog
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

    void BrowseIcon_Click(object sender, RoutedEventArgs e)
    {
        OpenFileDialog openFileDialog = new OpenFileDialog
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

    void BrowseWorkingDirectory_Click(object sender, RoutedEventArgs e)
    {
        using FolderBrowserDialog dialog = new FolderBrowserDialog();
        dialog.Description = "Select Working Directory";
        dialog.ShowNewFolderButton = true;

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

        DialogResultForms result = dialog.ShowDialog();

        if(result == DialogResultForms.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            Item.WorkingDirectory = dialog.SelectedPath;
            OnPropertyChanged(nameof(Item));
        }
    }

    void OkButton_Click(object sender, RoutedEventArgs e)
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

    void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}