using RightClickAppLauncher.Managers;
using RightClickAppLauncher.Models;
using RightClickAppLauncher.Utils;
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
using Cursors = System.Windows.Input.Cursors;
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

        private Point _mouseDragStartPoint_CanvasRelative;
        private FrameworkElement _draggedItemVisual;
        private LauncherItem _draggedLauncherItemModel;
        private Point _originalItemPositionBeforeDrag;

        private bool _isCurrentlyDragging = false;
        private bool _leftMouseDownOnIcon = false;

        private bool _isOpeningSettings = false;

        private readonly LauncherConfigManager _configManager;
        private Canvas _iconCanvasInstance;
        private readonly DragHistoryManager _dragHistory;

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public LauncherMenuWindow(ObservableCollection<LauncherItem> items, Point position, LauncherConfigManager configManager)
        {
            InitializeComponent();
            DataContext = this; // Window's DataContext
            _configManager = configManager;
            _dragHistory = new DragHistoryManager(ApplyItemPosition);

            LauncherItemsOnCanvas = items ?? new ObservableCollection<LauncherItem>();
            UpdateNoItemsMessage();

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
            this.Focus();
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
            if(!_isCurrentlyDragging && !_isOpeningSettings)
            {
                try { this.Close(); } catch(Exception ex) { Debug.WriteLine($"Error closing on deactivate: {ex.Message}"); }
            }
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            SaveAllLauncherItemPositions();
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
                _configManager.SaveLauncherItems(new System.Collections.Generic.List<LauncherItem>(LauncherItemsOnCanvas));
            }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if(e.Key == Key.Escape) { this.Close(); return; }
            bool ctrlPressed = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);
            if(ctrlPressed && e.Key == Key.Z) { _dragHistory.Undo(FindItemById); e.Handled = true; }
            else if(ctrlPressed && e.Key == Key.Y) { _dragHistory.Redo(FindItemById); e.Handled = true; }
        }

        private void LaunchItem(LauncherItem item)
        {
            Debug.WriteLine($"LaunchItem called for: {item?.DisplayName ?? "NULL ITEM"}");
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
                    else { string exeDir = Path.GetDirectoryName(startInfo.FileName); if(Directory.Exists(exeDir)) startInfo.WorkingDirectory = exeDir; }
                }
                else { string exeDir = Path.GetDirectoryName(startInfo.FileName); if(Directory.Exists(exeDir)) startInfo.WorkingDirectory = exeDir; }
                Process.Start(startInfo);
                Debug.WriteLine($"Successfully started: {item.DisplayName}");
            }
            catch(Exception ex)
            {
                MessageBox.Show($"Failed to launch '{item.DisplayName}':\n{ex.Message}", "Launch Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Debug.WriteLine($"Launch Error for {item.ExecutablePath}: {ex.ToString()}");
            }
        }

        // --- Icon Dragging Logic & Single Click ---
        private void Icon_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            Debug.WriteLine("Icon_PreviewMouseLeftButtonDown");
            if(sender is FrameworkElement fe && fe.DataContext is LauncherItem launcherItem)
            {
                _draggedItemVisual = fe;
                _draggedLauncherItemModel = launcherItem;

                if(_iconCanvasInstance == null)
                {
                    _iconCanvasInstance = FindVisualChild<Canvas>(LauncherItemsHostControl);
                    if(_iconCanvasInstance == null) { Debug.WriteLine("CRITICAL: IconCanvas still not found in MouseDown!"); return; }
                }
                _mouseDragStartPoint_CanvasRelative = e.GetPosition(_iconCanvasInstance);
                _originalItemPositionBeforeDrag = new Point(_draggedLauncherItemModel.X, _draggedLauncherItemModel.Y);

                _leftMouseDownOnIcon = true;
            }
        }

        private void Icon_MouseMove(object sender, MouseEventArgs e)
        {
            if(_leftMouseDownOnIcon && e.LeftButton == MouseButtonState.Pressed)
            {
                if(!_isCurrentlyDragging)
                {
                    if(_iconCanvasInstance == null) return;
                    Point currentPositionOnCanvas = e.GetPosition(_iconCanvasInstance);
                    if(Math.Abs(currentPositionOnCanvas.X - _mouseDragStartPoint_CanvasRelative.X) > SystemParameters.MinimumHorizontalDragDistance ||
                        Math.Abs(currentPositionOnCanvas.Y - _mouseDragStartPoint_CanvasRelative.Y) > SystemParameters.MinimumVerticalDragDistance)
                    {
                        Debug.WriteLine("Starting drag");
                        _isCurrentlyDragging = true;
                        _draggedItemVisual?.CaptureMouse();
                        if(_draggedItemVisual != null) _draggedItemVisual.Cursor = Cursors.Hand;
                    }
                }

                if(_isCurrentlyDragging)
                {
                    if(_iconCanvasInstance == null || _draggedLauncherItemModel == null || _draggedItemVisual == null) return;

                    Point currentMousePositionOnCanvas = e.GetPosition(_iconCanvasInstance);
                    double offsetX = currentMousePositionOnCanvas.X - _mouseDragStartPoint_CanvasRelative.X;
                    double offsetY = currentMousePositionOnCanvas.Y - _mouseDragStartPoint_CanvasRelative.Y;

                    double newX = _originalItemPositionBeforeDrag.X + offsetX;
                    double newY = _originalItemPositionBeforeDrag.Y + offsetY;

                    double itemWidth = _draggedItemVisual.ActualWidth;
                    double itemHeight = _draggedItemVisual.ActualHeight;

                    if(double.IsNaN(itemWidth) || itemWidth <= 0) itemWidth = 20 + 10; // Icon 20 + 5*2 padding
                    if(double.IsNaN(itemHeight) || itemHeight <= 0) itemHeight = 20 + 10;

                    newX = Math.Max(0, Math.Min(newX, _iconCanvasInstance.ActualWidth - itemWidth));
                    newY = Math.Max(0, Math.Min(newY, _iconCanvasInstance.ActualHeight - itemHeight));

                    _draggedLauncherItemModel.X = newX;
                    _draggedLauncherItemModel.Y = newY;
                }
            }
        }

        private void Icon_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            Debug.WriteLine($"Icon_PreviewMouseLeftButtonUp. _isCurrentlyDragging: {_isCurrentlyDragging}, _leftMouseDownOnIcon: {_leftMouseDownOnIcon}");

            bool wasDragging = _isCurrentlyDragging;
            LauncherItem itemModelForClick = null;

            if(_leftMouseDownOnIcon && sender is FrameworkElement fe)
            {
                itemModelForClick = fe.DataContext as LauncherItem;

                if(_isCurrentlyDragging)
                {
                    if(_draggedLauncherItemModel != null && _draggedItemVisual != null)
                    {
                        if(Math.Abs(_draggedLauncherItemModel.X - _originalItemPositionBeforeDrag.X) > 0.1 ||
                            Math.Abs(_draggedLauncherItemModel.Y - _originalItemPositionBeforeDrag.Y) > 0.1)
                        {
                            _dragHistory.RecordDrag(_draggedLauncherItemModel, _originalItemPositionBeforeDrag.X, _originalItemPositionBeforeDrag.Y);
                            Debug.WriteLine($"Drag recorded for {_draggedLauncherItemModel.DisplayName}");
                        }
                    }
                    _draggedItemVisual?.ReleaseMouseCapture();
                    if(_draggedItemVisual != null) _draggedItemVisual.Cursor = null;
                }
            }

            _isCurrentlyDragging = false;
            bool wasLeftMouseDownOnIcon = _leftMouseDownOnIcon;
            _leftMouseDownOnIcon = false;
            _draggedItemVisual = null;
            _draggedLauncherItemModel = null;

            if(wasLeftMouseDownOnIcon && !wasDragging && itemModelForClick != null)
            {
                Debug.WriteLine($"Attempting single click launch for: {itemModelForClick.DisplayName}");
                LaunchItem(itemModelForClick);
                this.Close();
            }
        }

        // --- Options and Settings Logic ---
        private void OptionsButton_Click(object sender, RoutedEventArgs e) { OpenSettingsWindow(); }
        private void OpenSettingsWindow()
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
            ReloadItemsFromConfig();
            if(sender is SettingsWindow sw) { sw.Closed -= SettingsWindow_Closed; }
            this.Show(); this.Activate(); this.Focus();
        }
        private void ReloadItemsFromConfig()
        {
            var updatedItems = new ObservableCollection<LauncherItem>(_configManager.LoadLauncherItems());
            LauncherItemsOnCanvas = updatedItems;
            UpdateNoItemsMessage();
            _dragHistory.ClearHistory();
        }

        // --- Window Dragging and Resizing ---
        private void MenuBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if(e.ButtonState == MouseButtonState.Pressed)
            {
                var originalSourceElement = e.OriginalSource as DependencyObject;
                bool canDragWindow = false;
                if(originalSourceElement == MenuBorder) { canDragWindow = true; }
                else
                {
                    DependencyObject current = originalSourceElement;
                    while(current != null && current != MenuBorder)
                    {
                        if(current is Grid titleBarGrid && Grid.GetRow(titleBarGrid) == 0 && VisualTreeHelper.GetParent(titleBarGrid) == MenuBorder)
                        {
                            if(!(e.OriginalSource is System.Windows.Controls.Button btn && btn.Name == "OptionsButton") &&
                                !(e.OriginalSource is System.Windows.Shapes.Path path && VisualTreeHelper.GetParent(path) is System.Windows.Controls.Button parentBtn && parentBtn.Name == "OptionsButton"))
                            {
                                canDragWindow = true;
                            }
                            break;
                        }
                        current = VisualTreeHelper.GetParent(current);
                    }
                }
                if(canDragWindow) { try { this.DragMove(); } catch(InvalidOperationException) { /* Can happen */ } }
            }
        }
        private void ResizeDragDelta(object sender, DragDeltaEventArgs e)
        {
            double newWidth = this.Width + e.HorizontalChange; double newHeight = this.Height + e.VerticalChange;
            if(newWidth >= this.MinWidth) this.Width = newWidth; if(newHeight >= this.MinHeight) this.Height = newHeight;
        }

        // --- Icon Context Menu Handlers ---
        private void IconBorder_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            Debug.WriteLine("IconBorder_ContextMenuOpening");
            if(sender is FrameworkElement fe && fe.DataContext is LauncherItem item)
            {
                if(fe.ContextMenu != null)
                {
                    fe.ContextMenu.DataContext = item;
                    Debug.WriteLine($"ContextMenu DataContext set to: {item.DisplayName}");
                }
                else { Debug.WriteLine("ContextMenu on IconBorder is null!"); e.Handled = true; }
            }
            else { Debug.WriteLine("Sender is not FrameworkElement or DataContext is not LauncherItem in ContextMenuOpening."); e.Handled = true; }
        }

        private void IconBorder_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            Debug.WriteLine("IconBorder_PreviewMouseRightButtonUp");
            if(e.LeftButton == MouseButtonState.Pressed && _leftMouseDownOnIcon)
            {
                Debug.WriteLine("ContextMenu skipped because left button is also pressed on icon.");
                return;
            }
            if(_isCurrentlyDragging)
            {
                Debug.WriteLine("ContextMenu skipped because _isCurrentlyDragging is true.");
                return;
            }

            // The ContextMenuOpening event should have set the DataContext.
            // WPF will typically open the menu automatically if e.Handled was not set to true
            // in ContextMenuOpening AND a ContextMenu is defined.
            // Explicitly opening it here can be a fallback or if more control is needed.
            if(sender is FrameworkElement fe && fe.ContextMenu != null)
            {
                // If you want to be absolutely sure it opens, uncomment:
                // fe.ContextMenu.IsOpen = true;
                // e.Handled = true; // if you uncomment IsOpen = true
            }
        }

        private LauncherItem GetLauncherItemFromContextMenu(object sender)
        {
            Debug.WriteLine($"GetLauncherItemFromContextMenu called by: {sender?.GetType().FullName}");
            if(sender is MenuItem menuItem)
            {
                // The MenuItem should inherit its DataContext from its parent ContextMenu
                // which we set in IconBorder_ContextMenuOpening
                if(menuItem.DataContext is LauncherItem itemFromMenuItemDC)
                {
                    Debug.WriteLine($"Found LauncherItem '{itemFromMenuItemDC.DisplayName}' from MenuItem's DataContext.");
                    return itemFromMenuItemDC;
                }
                else
                {
                    Debug.WriteLine($"MenuItem's DataContext is NOT LauncherItem. It is: {menuItem.DataContext?.GetType().FullName}. Trying parent ContextMenu.");
                    if(menuItem.Parent is ContextMenu parentContextMenu && parentContextMenu.DataContext is LauncherItem itemFromParentDC)
                    {
                        Debug.WriteLine($"Found LauncherItem '{itemFromParentDC.DisplayName}' from PARENT ContextMenu's DataContext.");
                        return itemFromParentDC;
                    }
                    else
                    {
                        Debug.WriteLine($"Parent ContextMenu's DataContext also not LauncherItem. Parent DC: {(menuItem.Parent as ContextMenu)?.DataContext?.GetType().FullName}");
                    }
                }
            }
            Debug.WriteLine("Could not get LauncherItem from ContextMenu sender.");
            return null;
        }

        private void IconContextMenu_Launch_Click(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("IconContextMenu_Launch_Click");
            var item = GetLauncherItemFromContextMenu(sender);
            if(item != null) { LaunchItem(item); this.Close(); }
            else { Debug.WriteLine("Launch_Click: Item was null."); }
        }

        private void IconContextMenu_OpenFileLocation_Click(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("IconContextMenu_OpenFileLocation_Click");
            var item = GetLauncherItemFromContextMenu(sender);
            if(item != null && !string.IsNullOrWhiteSpace(item.ExecutablePath))
            {
                try
                {
                    string expandedPath = Environment.ExpandEnvironmentVariables(item.ExecutablePath);
                    if(File.Exists(expandedPath)) Process.Start("explorer.exe", $"/select,\"{expandedPath}\"");
                    else if(Directory.Exists(expandedPath)) Process.Start("explorer.exe", $"\"{expandedPath}\"");
                    else
                    {
                        string dir = Path.GetDirectoryName(expandedPath);
                        if(Directory.Exists(dir)) Process.Start("explorer.exe", $"\"{dir}\"");
                        else MessageBox.Show("Cannot determine file location.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                catch(Exception ex) { MessageBox.Show($"Error opening file location: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
            }
            else { Debug.WriteLine("OpenFileLocation_Click: Item or path was null/empty."); }
        }

        private void IconContextMenu_Properties_Click(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("IconContextMenu_Properties_Click");
            var item = GetLauncherItemFromContextMenu(sender);
            if(item != null)
            {
                _isOpeningSettings = true;
                var editor = new LauncherItemEditorWindow(item) { Owner = this };
                if(editor.ShowDialog() == true)
                {
                    var originalItem = LauncherItemsOnCanvas.FirstOrDefault(i => i.Id == item.Id);
                    int index = (originalItem != null) ? LauncherItemsOnCanvas.IndexOf(originalItem) : -1;

                    if(index != -1)
                    {
                        LauncherItemsOnCanvas[index] = editor.Item;
                        SaveAllLauncherItemPositions();
                        Debug.WriteLine($"Properties updated for {editor.Item.DisplayName}");
                    }
                    else
                    {
                        Debug.WriteLine($"Could not find original item {item.DisplayName} to update properties.");
                    }
                }
                _isOpeningSettings = false; this.Focus();
            }
            else { Debug.WriteLine("Properties_Click: Item was null."); }
        }

        private void IconContextMenu_Remove_Click(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("IconContextMenu_Remove_Click");
            var item = GetLauncherItemFromContextMenu(sender);
            if(item != null)
            {
                var result = MessageBox.Show($"Are you sure you want to remove '{item.DisplayName}' from the menu?", "Confirm Removal", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if(result == MessageBoxResult.Yes)
                {
                    LauncherItemsOnCanvas.Remove(item);
                    UpdateNoItemsMessage();
                    _dragHistory.ClearHistory();
                    SaveAllLauncherItemPositions();
                    Debug.WriteLine($"Removed item: {item.DisplayName}");
                }
            }
            else { Debug.WriteLine("Remove_Click: Item was null."); }
        }
        private void BackgroundContextMenu_AddItem_Click(object sender, RoutedEventArgs e) { OpenSettingsWindow(); }
    }
}