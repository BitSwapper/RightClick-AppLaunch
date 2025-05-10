// File: UI/LauncherMenuWindow.xaml.cs
using RightClickAppLauncher.Managers;
using RightClickAppLauncher.Models;
using RightClickAppLauncher.Utils; // For DragHistoryManager
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
using Button = System.Windows.Controls.Button;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MessageBox = System.Windows.MessageBox;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;

namespace RightClickAppLauncher.UI
{
    public partial class LauncherMenuWindow : Window, INotifyPropertyChanged
    {
        // ... (Existing fields: _launcherItemsOnCanvas, MenuTitle, _showNoItemsMessage, etc.) ...
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
        private Point _originalItemPositionBeforeDrag; // Renamed for clarity for undo
        private bool _isDragging = false;
        private bool _isOpeningSettings = false;

        private readonly LauncherConfigManager _configManager;
        private Canvas _iconCanvasInstance;
        private readonly DragHistoryManager _dragHistory; // New field for undo/redo

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

            // Initialize DragHistoryManager
            _dragHistory = new DragHistoryManager(ApplyItemPosition);


            LauncherItemsOnCanvas = items ?? new ObservableCollection<LauncherItem>();
            UpdateNoItemsMessage(); // Use a helper

            this.Left = Properties.Settings.Default.LauncherMenuX != 0 ? Properties.Settings.Default.LauncherMenuX : position.X;
            this.Top = Properties.Settings.Default.LauncherMenuY != 0 ? Properties.Settings.Default.LauncherMenuY : position.Y;
            this.Width = Properties.Settings.Default.LauncherMenuWidth > 0 ? Properties.Settings.Default.LauncherMenuWidth : this.Width;
            this.Height = Properties.Settings.Default.LauncherMenuHeight > 0 ? Properties.Settings.Default.LauncherMenuHeight : this.Height;

            EnsureWindowIsOnScreen();
        }

        private void UpdateNoItemsMessage()
        {
            ShowNoItemsMessage = !LauncherItemsOnCanvas.Any() || LauncherItemsOnCanvas.All(it => it.ExecutablePath == "NO_ACTION");
        }

        private void ApplyItemPosition(LauncherItem item, double x, double y)
        {
            if(item != null)
            {
                item.X = x;
                item.Y = y;
                // UI should update via binding. If not, might need OnPropertyChanged for item.X/Y if LauncherItem doesn't implement INPC
            }
        }

        private LauncherItem FindItemById(Guid id)
        {
            return LauncherItemsOnCanvas.FirstOrDefault(item => item.Id == id);
        }


        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _iconCanvasInstance = FindVisualChild<Canvas>(LauncherItemsHostControl);
            if(_iconCanvasInstance == null)
            {
                Debug.WriteLine("WARNING: IconCanvas instance not found in Window_Loaded!");
            }
            this.Focus(); // Set focus to the window to receive KeyDown events
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
            if(!_isDragging && !_isOpeningSettings)
            {
                try { this.Close(); } catch(Exception ex) { Debug.WriteLine($"Error closing on deactivate: {ex.Message}"); }
            }
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            SaveAllLauncherItemPositions(); // Call helper to save

            Properties.Settings.Default.LauncherMenuX = this.Left;
            Properties.Settings.Default.LauncherMenuY = this.Top;
            Properties.Settings.Default.LauncherMenuWidth = this.ActualWidth;
            Properties.Settings.Default.LauncherMenuHeight = this.ActualHeight;
            Properties.Settings.Default.Save();
        }

        private void SaveAllLauncherItemPositions()
        {
            if(LauncherItemsOnCanvas != null && _configManager != null)
            {
                // Directly save the current state of LauncherItemsOnCanvas.
                // This collection should already reflect all user's drags and undo/redo operations.
                _configManager.SaveLauncherItems(new System.Collections.Generic.List<LauncherItem>(LauncherItemsOnCanvas));
            }
        }


        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if(e.Key == Key.Escape)
            {
                this.Close();
                return;
            }

            bool ctrlPressed = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);

            if(ctrlPressed && e.Key == Key.Z)
            {
                _dragHistory.Undo(FindItemById);
                e.Handled = true;
            }
            else if(ctrlPressed && e.Key == Key.Y)
            {
                _dragHistory.Redo(FindItemById);
                e.Handled = true;
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

        // --- Icon Dragging Logic ---

        private void Icon_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 'sender' will be the Border from the IconItemTemplate
            if(e.ClickCount == 1 && sender is FrameworkElement fe && fe.DataContext is LauncherItem launcherItem)
            {
                _draggedItem = fe; // The Border is our dragged item's visual
                _draggedLauncherItem = launcherItem;

                if(_iconCanvasInstance == null)
                {
                    _iconCanvasInstance = FindVisualChild<Canvas>(LauncherItemsHostControl);
                    if(_iconCanvasInstance == null)
                    {
                        Debug.WriteLine("CRITICAL: IconCanvas not found in Icon_PreviewMouseLeftButtonDown!");
                        return;
                    }
                }
                _originalMousePosition = e.GetPosition(_iconCanvasInstance);
                _originalItemPositionBeforeDrag = new Point(_draggedLauncherItem.X, _draggedLauncherItem.Y);

                _draggedItem.CaptureMouse();
                _isDragging = true;
                e.Handled = true; // Important to prevent other actions like context menu opening on drag start
            }
        }

        private void Icon_MouseMove(object sender, MouseEventArgs e)
        {
            if(_isDragging && _draggedItem != null && e.LeftButton == MouseButtonState.Pressed)
            {
                if(_iconCanvasInstance == null || _draggedLauncherItem == null) return;

                Point currentMousePosition = e.GetPosition(_iconCanvasInstance);
                double offsetX = currentMousePosition.X - _originalMousePosition.X;
                double offsetY = currentMousePosition.Y - _originalMousePosition.Y;

                double newX = _originalItemPositionBeforeDrag.X + offsetX;
                double newY = _originalItemPositionBeforeDrag.Y + offsetY;

                // Get the ContentPresenter for accurate width/height if needed for clamping
                // For now, ActualWidth/Height of the border (_draggedItem) should be okay.
                double itemWidth = _draggedItem.ActualWidth;
                double itemHeight = _draggedItem.ActualHeight;

                // Ensure itemWidth and itemHeight are valid
                if(double.IsNaN(itemWidth) || itemWidth <= 0) itemWidth = 32 + 10; // Approx icon + padding
                if(double.IsNaN(itemHeight) || itemHeight <= 0) itemHeight = 32 + 10;


                newX = Math.Max(0, Math.Min(newX, _iconCanvasInstance.ActualWidth - itemWidth));
                newY = Math.Max(0, Math.Min(newY, _iconCanvasInstance.ActualHeight - itemHeight));

                _draggedLauncherItem.X = newX;
                _draggedLauncherItem.Y = newY;
            }
        }

        private void Icon_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if(_isDragging)
            {
                // 'sender' is the Border
                if(_draggedLauncherItem != null && _draggedItem != null) // Check if an item was actually being dragged
                {
                    if(Math.Abs(_draggedLauncherItem.X - _originalItemPositionBeforeDrag.X) > 0.1 ||
                        Math.Abs(_draggedLauncherItem.Y - _originalItemPositionBeforeDrag.Y) > 0.1)
                    {
                        _dragHistory.RecordDrag(_draggedLauncherItem, _originalItemPositionBeforeDrag.X, _originalItemPositionBeforeDrag.Y);
                    }
                }

                _draggedItem?.ReleaseMouseCapture(); // Release capture from the border
                _isDragging = false;
                _draggedItem = null;
                _draggedLauncherItem = null;
                // e.Handled = true; // Not strictly necessary to handle here, but can be.
            }
        }

      

        private void OptionsButton_Click(object sender, RoutedEventArgs e)
        {
            OpenSettingsWindow();
        }

        private void OpenSettingsWindow() // Helper method
        {
            _isOpeningSettings = true;
            var settingsWindow = new SettingsWindow { Owner = this };
            settingsWindow.Closed += SettingsWindow_Closed;
            this.Hide();
            settingsWindow.ShowDialog();
        }


        private void SettingsWindow_Closed(object sender, EventArgs e)
        {
            _isOpeningSettings = false;
            ReloadItemsFromConfig(); // Use a helper

            if(sender is SettingsWindow sw)
            {
                sw.Closed -= SettingsWindow_Closed;
            }

            this.Show();
            this.Activate();
            this.Focus(); // Ensure window can receive key events again
        }

        private void ReloadItemsFromConfig()
        {
            var updatedItems = new ObservableCollection<LauncherItem>(_configManager.LoadLauncherItems());
            LauncherItemsOnCanvas = updatedItems;
            UpdateNoItemsMessage();
            _dragHistory.ClearHistory(); // Clear history as items might have changed significantly
        }

        // --- Window Dragging and Resizing ---
        // In UI/LauncherMenuWindow.xaml.cs

        private void MenuBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if(e.ButtonState == MouseButtonState.Pressed)
            {
                // Get the element that was actually clicked
                var originalSourceElement = e.OriginalSource as DependencyObject;
                bool canDragWindow = false;

                if(originalSourceElement == MenuBorder) // Clicked directly on the outer border
                {
                    canDragWindow = true;
                }
                else
                {
                    // Check if the click was within the title bar grid (Grid.Row="0")
                    // Walk up the visual tree from the original source
                    DependencyObject current = originalSourceElement;
                    while(current != null && current != MenuBorder)
                    {
                        if(current is Grid titleBarGrid && Grid.GetRow(titleBarGrid) == 0 && VisualTreeHelper.GetParent(titleBarGrid) == MenuBorder)
                        {
                            // Make sure it's not the options button itself
                            if(!(e.OriginalSource is Button) && !(e.OriginalSource is Path)) // Path is inside the button
                            {
                                canDragWindow = true;
                            }
                            break;
                        }
                        current = VisualTreeHelper.GetParent(current);
                    }
                }

                if(canDragWindow)
                {
                    try
                    {
                        this.DragMove();
                    }
                    catch(InvalidOperationException) { /* Can happen if not left button, or other state issues */ }
                }
            }
        }

        private void IconBorder_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if(_isDragging) // If a drag operation was in progress, don't show context menu
            {
                // The PreviewMouseLeftButtonUp should handle releasing capture and resetting _isDragging
                // This check is an extra precaution.
                return;
            }

            if(sender is FrameworkElement fe && fe.ContextMenu != null)
            {
                // The DataContext for the ContextMenu should be set by:
                // DataContext="{Binding PlacementTarget.DataContext, RelativeSource={RelativeSource Self}}"
                // which means it will inherit the LauncherItem from the Border.
                // If it's not, you might need to set it explicitly:
                // fe.ContextMenu.DataContext = fe.DataContext; 

                fe.ContextMenu.IsOpen = true;
                e.Handled = true; // Prevent any other default right-click behavior
            }
        }

        private void ResizeDragDelta(object sender, DragDeltaEventArgs e)
        {
            double newWidth = this.Width + e.HorizontalChange;
            double newHeight = this.Height + e.VerticalChange;

            if(newWidth >= this.MinWidth) this.Width = newWidth;
            if(newHeight >= this.MinHeight) this.Height = newHeight;
        }

        // --- Icon Context Menu Handlers ---
        private LauncherItem GetLauncherItemFromContextMenu(object sender)
        {
            if(sender is MenuItem menuItem && menuItem.DataContext is LauncherItem item)
            {
                return item;
            }
            return null;
        }

        private void IconContextMenu_Launch_Click(object sender, RoutedEventArgs e)
        {
            var item = GetLauncherItemFromContextMenu(sender);
            if(item != null)
            {
                LaunchItem(item);
                this.Close();
            }
        }

        private void IconContextMenu_OpenFileLocation_Click(object sender, RoutedEventArgs e)
        {
            var item = GetLauncherItemFromContextMenu(sender);
            if(item != null && !string.IsNullOrWhiteSpace(item.ExecutablePath))
            {
                try
                {
                    string expandedPath = Environment.ExpandEnvironmentVariables(item.ExecutablePath);
                    if(File.Exists(expandedPath))
                    {
                        Process.Start("explorer.exe", $"/select,\"{expandedPath}\"");
                    }
                    else if(Directory.Exists(expandedPath)) // If path itself is a directory
                    {
                        Process.Start("explorer.exe", $"\"{expandedPath}\"");
                    }
                    else
                    {
                        string dir = Path.GetDirectoryName(expandedPath);
                        if(Directory.Exists(dir))
                        {
                            Process.Start("explorer.exe", $"\"{dir}\"");
                        }
                        else
                        {
                            MessageBox.Show("Cannot determine file location.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                }
                catch(Exception ex)
                {
                    MessageBox.Show($"Error opening file location: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void IconContextMenu_Properties_Click(object sender, RoutedEventArgs e)
        {
            var item = GetLauncherItemFromContextMenu(sender);
            if(item != null)
            {
                _isOpeningSettings = true; // Treat this like opening main settings to prevent menu auto-close
                var editor = new LauncherItemEditorWindow(item) { Owner = this }; // Make sure this is the correct editor
                if(editor.ShowDialog() == true)
                {
                    // Update the item in the collection
                    int index = LauncherItemsOnCanvas.IndexOf(item);
                    if(index != -1)
                    {
                        LauncherItemsOnCanvas[index] = editor.Item; // Replace with edited item
                        // Force a save because only positions are saved on close, not item details
                        SaveAllLauncherItemPositions();
                    }
                }
                _isOpeningSettings = false;
                this.Focus(); // Refocus main menu
            }
        }

        private void IconContextMenu_Remove_Click(object sender, RoutedEventArgs e)
        {
            var item = GetLauncherItemFromContextMenu(sender);
            if(item != null)
            {
                var result = MessageBox.Show($"Are you sure you want to remove '{item.DisplayName}' from the menu?",
                                             "Confirm Removal", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if(result == MessageBoxResult.Yes)
                {
                    LauncherItemsOnCanvas.Remove(item);
                    UpdateNoItemsMessage();
                    _dragHistory.ClearHistory(); // Or selectively remove operations for this item
                    SaveAllLauncherItemPositions(); // Save changes immediately
                }
            }
        }

        // --- Background Context Menu Handler ---
        private void BackgroundContextMenu_AddItem_Click(object sender, RoutedEventArgs e)
        {
            OpenSettingsWindow(); // Just open the main settings window to add items
        }
    }
}