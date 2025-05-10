// File: UI/LauncherMenuWindow.xaml.cs
using RightClickAppLauncher.Managers;
using RightClickAppLauncher.Models;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MessageBox = System.Windows.MessageBox;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;

namespace RightClickAppLauncher.UI
{
    public partial class LauncherMenuWindow : Window, INotifyPropertyChanged
    {
        private ObservableCollection<LauncherItem> _launcherItemsOnCanvas;
        public ObservableCollection<LauncherItem> LauncherItemsOnCanvas
        {
            get => _launcherItemsOnCanvas;
            set { _launcherItemsOnCanvas = value; OnPropertyChanged(nameof(LauncherItemsOnCanvas)); }
        }

        public string MenuTitle { get; set; } = "App Launcher";

        private bool _showNoItemsMessage;
        public bool ShowNoItemsMessage
        {
            get => _showNoItemsMessage;
            set { _showNoItemsMessage = value; OnPropertyChanged(nameof(ShowNoItemsMessage)); }
        }

        private Point _originalMousePosition;
        private FrameworkElement _draggedItem;
        private LauncherItem _draggedLauncherItem;
        private Point _originalItemPosition;
        private bool _isDragging = false;
        private bool _isOpeningSettings = false; // Flag to manage settings window opening

        private readonly LauncherConfigManager _configManager;
        private Canvas _iconCanvasInstance;

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public LauncherMenuWindow(ObservableCollection<LauncherItem> items, Point position, LauncherConfigManager configManager)
        {
            InitializeComponent();
            DataContext = this;
            _configManager = configManager;

            LauncherItemsOnCanvas = items ?? new ObservableCollection<LauncherItem>();
            ShowNoItemsMessage = !LauncherItemsOnCanvas.Any() || LauncherItemsOnCanvas.All(it => it.ExecutablePath == "NO_ACTION");

            this.Left = Properties.Settings.Default.LauncherMenuX != 0 ? Properties.Settings.Default.LauncherMenuX : position.X;
            this.Top = Properties.Settings.Default.LauncherMenuY != 0 ? Properties.Settings.Default.LauncherMenuY : position.Y;
            this.Width = Properties.Settings.Default.LauncherMenuWidth > 0 ? Properties.Settings.Default.LauncherMenuWidth : this.Width;
            this.Height = Properties.Settings.Default.LauncherMenuHeight > 0 ? Properties.Settings.Default.LauncherMenuHeight : this.Height;

            EnsureWindowIsOnScreen();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Assuming your ItemsControl in XAML has x:Name="LauncherItemsHostControl"
            // If not, FindVisualChild<Canvas>(this) might be used but is less specific.
            _iconCanvasInstance = FindVisualChild<Canvas>(LauncherItemsHostControl);

            if(_iconCanvasInstance == null)
            {
                Debug.WriteLine("WARNING: IconCanvas instance not found in Window_Loaded!");
            }

            this.Activate();
            if(ShowNoItemsMessage)
            {
                MenuBorder.Focus();
            }
        }

        public static T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if(parent == null) return null;
            for(int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                if(child != null && child is T)
                    return (T)child;
                else
                {
                    T childOfChild = FindVisualChild<T>(child);
                    if(childOfChild != null)
                        return childOfChild;
                }
            }
            return null;
        }

        private void EnsureWindowIsOnScreen()
        {
            double screenWidth = SystemParameters.VirtualScreenWidth;
            double screenHeight = SystemParameters.VirtualScreenHeight;

            if(this.Left + this.Width > screenWidth) this.Left = screenWidth - this.Width;
            if(this.Top + this.Height > screenHeight) this.Top = screenHeight - this.Height;
            if(this.Left < 0) this.Left = 0;
            if(this.Top < 0) this.Top = 0;
        }

        private void Window_Deactivated(object sender, EventArgs e)
        {
            if(!_isDragging && !_isOpeningSettings) // Check the new flag
            {
                try { this.Close(); } catch(Exception ex) { Debug.WriteLine($"Error closing on deactivate: {ex.Message}"); }
            }
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            if(LauncherItemsOnCanvas != null && _configManager != null)
            {
                _configManager.SaveLauncherItems(new System.Collections.Generic.List<LauncherItem>(LauncherItemsOnCanvas));
            }

            Properties.Settings.Default.LauncherMenuX = this.Left;
            Properties.Settings.Default.LauncherMenuY = this.Top;
            Properties.Settings.Default.LauncherMenuWidth = this.ActualWidth;
            Properties.Settings.Default.LauncherMenuHeight = this.ActualHeight;
            Properties.Settings.Default.Save();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if(e.Key == Key.Escape)
            {
                this.Close();
            }
        }

        private void LaunchItem(LauncherItem item)
        {
            if(item == null || string.IsNullOrWhiteSpace(item.ExecutablePath) || item.ExecutablePath == "NO_ACTION")
            {
                if(item?.ExecutablePath != "NO_ACTION")
                {
                    MessageBox.Show("Executable path is not configured for this item.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                return;
            }
            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = Environment.ExpandEnvironmentVariables(item.ExecutablePath),
                    Arguments = Environment.ExpandEnvironmentVariables(item.Arguments ?? string.Empty),
                    UseShellExecute = true
                };

                if(!string.IsNullOrWhiteSpace(item.WorkingDirectory))
                {
                    string workingDir = Environment.ExpandEnvironmentVariables(item.WorkingDirectory);
                    if(Directory.Exists(workingDir)) startInfo.WorkingDirectory = workingDir;
                    else
                    {
                        string exeDir = Path.GetDirectoryName(startInfo.FileName);
                        if(Directory.Exists(exeDir)) startInfo.WorkingDirectory = exeDir;
                    }
                }
                else
                {
                    string exeDir = Path.GetDirectoryName(startInfo.FileName);
                    if(Directory.Exists(exeDir)) startInfo.WorkingDirectory = exeDir;
                }
                Process.Start(startInfo);
            }
            catch(Exception ex)
            {
                MessageBox.Show($"Failed to launch '{item.DisplayName}':\n{ex.Message}", "Launch Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Debug.WriteLine($"Launch Error for {item.ExecutablePath}: {ex.ToString()}");
            }
        }

        private void Icon_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if(e.ClickCount == 1)
            {
                _draggedItem = sender as FrameworkElement;
                if(_draggedItem?.DataContext is LauncherItem launcherItem)
                {
                    _draggedLauncherItem = launcherItem;
                    if(_iconCanvasInstance == null)
                    {
                        // Attempt to find it again if it was null, could happen if Loaded event fires too early.
                        _iconCanvasInstance = FindVisualChild<Canvas>(LauncherItemsHostControl);
                        if(_iconCanvasInstance == null)
                        {
                            Debug.WriteLine("CRITICAL: IconCanvas still not found in Icon_PreviewMouseLeftButtonDown!");
                            return;
                        }
                    }
                    _originalMousePosition = e.GetPosition(_iconCanvasInstance);
                    _originalItemPosition = new Point(_draggedLauncherItem.X, _draggedLauncherItem.Y);
                    _draggedItem.CaptureMouse();
                    _isDragging = true;
                    e.Handled = true;
                }
            }
        }

        private void Icon_MouseMove(object sender, MouseEventArgs e)
        {
            if(_isDragging && _draggedItem != null && e.LeftButton == MouseButtonState.Pressed)
            {
                if(_iconCanvasInstance == null) return;

                Point currentMousePosition = e.GetPosition(_iconCanvasInstance);
                double offsetX = currentMousePosition.X - _originalMousePosition.X;
                double offsetY = currentMousePosition.Y - _originalMousePosition.Y;

                double newX = _originalItemPosition.X + offsetX;
                double newY = _originalItemPosition.Y + offsetY;

                if(_iconCanvasInstance != null)
                {
                    newX = Math.Max(0, Math.Min(newX, _iconCanvasInstance.ActualWidth - _draggedItem.ActualWidth));
                    newY = Math.Max(0, Math.Min(newY, _iconCanvasInstance.ActualHeight - _draggedItem.ActualHeight));
                }

                _draggedLauncherItem.X = newX;
                _draggedLauncherItem.Y = newY;
            }
        }

        private void Icon_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if(_isDragging)
            {
                _draggedItem?.ReleaseMouseCapture();
                _isDragging = false;
                _draggedItem = null;
                _draggedLauncherItem = null;
                e.Handled = true;
            }
        }

        private void Icon_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if(sender is FrameworkElement fe && fe.DataContext is LauncherItem item)
            {
                LaunchItem(item);
                this.Close();
            }
        }

        private void OptionsButton_Click(object sender, RoutedEventArgs e)
        {
            _isOpeningSettings = true; // Set flag

            var settingsWindow = new SettingsWindow { Owner = this };
            settingsWindow.Closed += SettingsWindow_Closed;
            this.Hide();
            settingsWindow.ShowDialog();
            // _isOpeningSettings will be reset in SettingsWindow_Closed
        }

        private void SettingsWindow_Closed(object sender, EventArgs e)
        {
            // This method is called after the SettingsWindow is closed.
            _isOpeningSettings = false; // Reset flag

            var updatedItems = new ObservableCollection<LauncherItem>(_configManager.LoadLauncherItems());
            LauncherItemsOnCanvas = updatedItems;
            ShowNoItemsMessage = !LauncherItemsOnCanvas.Any() || LauncherItemsOnCanvas.All(it => it.ExecutablePath == "NO_ACTION");

            if(sender is SettingsWindow sw)
            {
                sw.Closed -= SettingsWindow_Closed;
            }

            this.Show();
            this.Activate();
        }

        private void MenuBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if(e.ButtonState == MouseButtonState.Pressed)
            {
                if(e.OriginalSource == MenuBorder ||
                    (e.OriginalSource is Grid && VisualTreeHelper.GetParent(e.OriginalSource as DependencyObject) == MenuBorder) ||
                    (e.OriginalSource is TextBlock && (e.OriginalSource as TextBlock).Name != "OptionsButton")) // Check if it's the border itself
                {
                    this.DragMove();
                }
            }
        }

        private void ResizeDragDelta(object sender, DragDeltaEventArgs e)
        {
            double newWidth = this.Width + e.HorizontalChange;
            double newHeight = this.Height + e.VerticalChange;

            if(newWidth >= this.MinWidth) this.Width = newWidth;
            if(newHeight >= this.MinHeight) this.Height = newHeight;
        }
    }
}