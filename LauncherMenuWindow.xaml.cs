using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Newtonsoft.Json;
using RightClickAppLauncher.Managers;
using RightClickAppLauncher.Models;
using RightClickAppLauncher.Properties;
using RightClickAppLauncher.Utils;
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

        private double _currentIconSize;
        public double CurrentIconSize
        {
            get => _currentIconSize;
            set
            {
                _currentIconSize = value;
                OnPropertyChanged(nameof(CurrentIconSize));
            }
        }



        private Point _mouseDragStartPoint_CanvasRelative;
        private FrameworkElement _draggedItemVisual;
        private LauncherItem _draggedLauncherItemModel;
        private Point _originalItemPositionBeforeDrag;
        private bool _isCurrentlyDragging = false;
        private bool _leftMouseDownOnIcon = false;
        private bool _isOpeningSettings = false;
        private bool _isShowingInputDialog = false;
        private readonly LauncherConfigManager _configManager;
        private Canvas _iconCanvasInstance;
        private readonly DragHistoryManager _dragHistory;

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct SHELLEXECUTEINFO
        {
            public int cbSize; public uint fMask; public IntPtr hwnd;
            [MarshalAs(UnmanagedType.LPTStr)] public string lpVerb;
            [MarshalAs(UnmanagedType.LPTStr)] public string lpFile;
            [MarshalAs(UnmanagedType.LPTStr)] public string lpParameters;
            [MarshalAs(UnmanagedType.LPTStr)] public string lpDirectory;
            public int nShow; public IntPtr hInstApp; public IntPtr lpIDList;
            [MarshalAs(UnmanagedType.LPTStr)] public string lpClass;
            public IntPtr hkeyClass; public uint dwHotKey; public IntPtr hIcon; public IntPtr hProcess;
        }

        private const uint SEE_MASK_INVOKEIDLIST = 12;
        private const int SW_SHOWNORMAL = 1;
        [DllImport("shell32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO lpExecInfo);

        // Properties for dynamic sizing based on settings
        private double GridCellSize => Settings.Default.IconSize + 10 + Settings.Default.IconSpacing; // Icon size + border padding + user-defined spacing
        private const double StackPadding = 5.0;

        public LauncherMenuWindow(ObservableCollection<LauncherItem> items, Point position, LauncherConfigManager configManager)
        {
            InitializeComponent();
            DataContext = this;
            _configManager = configManager;
            _dragHistory = new DragHistoryManager(ApplyItemPosition);
            LauncherItemsOnCanvas = items ?? new ObservableCollection<LauncherItem>();
            UpdateNoItemsMessage();

            // Initialize the icon size
            CurrentIconSize = Settings.Default.IconSize;

            if(Settings.Default.SavedLayouts == null)
            {
                Settings.Default.SavedLayouts = new StringCollection();
            }

            // Always use cursor position
            Point cursorPosition = GetCursorPosition();
            this.Left = cursorPosition.X;
            this.Top = cursorPosition.Y;

            // Still use saved window dimensions
            try
            {
                this.Width = Settings.Default.LauncherMenuWidth > 0 ? Settings.Default.LauncherMenuWidth : this.Width;
                this.Height = Settings.Default.LauncherMenuHeight > 0 ? Settings.Default.LauncherMenuHeight : this.Height;
            }
            catch(System.Configuration.SettingsPropertyNotFoundException ex)
            {
                Debug.WriteLine($"SETTINGS PROPERTY NOT FOUND in constructor: {ex} - {ex.Message}. Using defaults.");
            }

            EnsureWindowIsOnScreen();
        }

        // Update the SaveLayoutAs_Click method
        private void SaveLayoutAs_Click(object sender, RoutedEventArgs e)
        {
            _isShowingInputDialog = true;

            InputDialog inputDialog = new InputDialog("Enter name for this layout:", "My Layout " + DateTime.Now.ToString("yyyy-MM-dd HHmm"))
            { Owner = this };

            if(inputDialog.ShowDialog() == true)
            {
                string layoutName = inputDialog.ResponseText.Trim();
                if(string.IsNullOrWhiteSpace(layoutName))
                {
                    MessageBox.Show("Layout name cannot be empty.", "Invalid Name", MessageBoxButton.OK, MessageBoxImage.Warning);
                    _isShowingInputDialog = false;
                    return;
                }

                System.Collections.Generic.List<LauncherItem> currentItemsToSave = new System.Collections.Generic.List<LauncherItem>(LauncherItemsOnCanvas);
                string currentLayoutItemsJson = JsonConvert.SerializeObject(currentItemsToSave, Formatting.Indented);

                System.Collections.Generic.List<NamedLayout> allSavedLayouts = GetSavedNamedLayouts();

                NamedLayout existingLayout = allSavedLayouts.FirstOrDefault(L => L.Name.Equals(layoutName, StringComparison.OrdinalIgnoreCase));
                if(existingLayout != null)
                {
                    var result = MessageBox.Show($"A layout named '{layoutName}' already exists. Overwrite it?", "Confirm Overwrite", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    if(result == MessageBoxResult.No)
                    {
                        _isShowingInputDialog = false;
                        return;
                    }
                    existingLayout.LayoutJson = currentLayoutItemsJson;
                    existingLayout.SavedDate = DateTime.UtcNow;
                    existingLayout.WindowWidth = this.ActualWidth;
                    existingLayout.WindowHeight = this.ActualHeight;
                    existingLayout.IconSize = Settings.Default.IconSize;
                    existingLayout.IconSpacing = Settings.Default.IconSpacing;
                }
                else
                {
                    allSavedLayouts.Add(new NamedLayout
                    {
                        Name = layoutName,
                        LayoutJson = currentLayoutItemsJson,
                        WindowWidth = this.ActualWidth,
                        WindowHeight = this.ActualHeight,
                        IconSize = Settings.Default.IconSize,
                        IconSpacing = Settings.Default.IconSpacing
                    });
                }

                PersistAllNamedLayouts(allSavedLayouts);
                MessageBox.Show($"Layout '{layoutName}' saved with icon settings.", "Layout Saved", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            _isShowingInputDialog = false;
        }

        // Update the ReloadItemsFromConfig method
        private void ReloadItemsFromConfig(bool loadDefaultLayout = true, NamedLayout layoutToLoad = null)
        {
            Debug.WriteLine($"ReloadItemsFromConfig. LoadDefault: {loadDefaultLayout}, Specific Layout: {layoutToLoad?.Name ?? "N/A"}");
            System.Collections.Generic.List<LauncherItem> itemsToLoad = null;

            if(!loadDefaultLayout && layoutToLoad != null)
            {
                try
                {
                    if(string.IsNullOrWhiteSpace(layoutToLoad.LayoutJson))
                    {
                        Debug.WriteLine($"Layout '{layoutToLoad.Name}' has empty JSON. Loading default.");
                        itemsToLoad = _configManager.LoadLauncherItems();
                    }
                    else
                    {
                        itemsToLoad = JsonConvert.DeserializeObject<System.Collections.Generic.List<LauncherItem>>(layoutToLoad.LayoutJson);
                        Debug.WriteLine($"Loaded specific layout: {layoutToLoad.Name}");

                        // Restore window dimensions if available
                        if(layoutToLoad.WindowWidth > 0 && layoutToLoad.WindowHeight > 0)
                        {
                            this.Width = layoutToLoad.WindowWidth;
                            this.Height = layoutToLoad.WindowHeight;
                        }

                        // Restore icon settings if available (check for non-zero values for backward compatibility)
                        if(layoutToLoad.IconSize > 0)
                        {
                            Settings.Default.IconSize = layoutToLoad.IconSize;
                            CurrentIconSize = layoutToLoad.IconSize;
                        }

                        if(layoutToLoad.IconSpacing >= 0) // IconSpacing can be 0, so we check for >= 0
                        {
                            Settings.Default.IconSpacing = layoutToLoad.IconSpacing;
                        }

                        // Save the settings so they persist
                        Settings.Default.Save();

                        // Apply the icon size to refresh the UI
                        ApplyIconSize();

                        Debug.WriteLine($"Applied icon size: {layoutToLoad.IconSize}, spacing: {layoutToLoad.IconSpacing}");
                    }
                }
                catch(Exception ex)
                {
                    MessageBox.Show($"Error deserializing layout '{layoutToLoad.Name}': {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    itemsToLoad = _configManager.LoadLauncherItems();
                }
            }
            else
            {
                itemsToLoad = _configManager.LoadLauncherItems();
            }

            LauncherItemsOnCanvas = new ObservableCollection<LauncherItem>(itemsToLoad ?? new System.Collections.Generic.List<LauncherItem>());
            UpdateNoItemsMessage();
            _dragHistory.ClearHistory();
        }

        // Update the GetSavedNamedLayouts method to handle backward compatibility
        private System.Collections.Generic.List<NamedLayout> GetSavedNamedLayouts()
        {
            if(Settings.Default.SavedLayouts == null)
            {
                Settings.Default.SavedLayouts = new StringCollection();
                return new System.Collections.Generic.List<NamedLayout>();
            }

            System.Collections.Generic.List<NamedLayout> namedLayouts = new System.Collections.Generic.List<NamedLayout>();
            foreach(string layoutEntryJson in Settings.Default.SavedLayouts)
            {
                if(string.IsNullOrWhiteSpace(layoutEntryJson)) continue;
                try
                {
                    NamedLayout namedLayout = JsonConvert.DeserializeObject<NamedLayout>(layoutEntryJson);
                    if(namedLayout != null)
                    {
                        // For backward compatibility: if icon settings are missing (0), use current defaults
                        if(namedLayout.IconSize <= 0)
                            namedLayout.IconSize = Settings.Default.IconSize;

                        // IconSpacing can be 0, so we only check for negative values
                        if(namedLayout.IconSpacing < 0)
                            namedLayout.IconSpacing = Settings.Default.IconSpacing;

                        namedLayouts.Add(namedLayout);
                    }
                }
                catch(Exception ex)
                {
                    Debug.WriteLine($"Error deserializing a NamedLayout entry: {ex.Message} - JSON: {layoutEntryJson}");
                }
            }
            return namedLayouts;
        }

        private void ApplyIconSize()
        {
            CurrentIconSize = Settings.Default.IconSize;

            // Force all items to update their bindings
            if(LauncherItemsOnCanvas != null)
            {
                foreach(var item in LauncherItemsOnCanvas)
                {
                    item.OnPropertyChanged("IconPath"); // Trigger rebinding
                }
            }

            // Force the entire window to update layout
            this.UpdateLayout();
            this.InvalidateVisual();
        }

        // Update the SettingsWindow_Closed method
        private void SettingsWindow_Closed(object sender, EventArgs e)
        {
            _isOpeningSettings = false;

            // Apply the new icon size
            ApplyIconSize();

            ReloadItemsFromConfig(true);

            if(sender is SettingsWindow sw) sw.Closed -= SettingsWindow_Closed;
            this.Show();
            this.Activate();
            this.Focus();
        }

        // Remove RefreshIconSizes method since it's now redundant with ApplyIconSize

        // Update Window_Loaded method
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _iconCanvasInstance = FindVisualChild<Canvas>(LauncherItemsHostControl);
            if(_iconCanvasInstance == null) Debug.WriteLine("WARNING: IconCanvas instance not found!");

            // Apply icon sizes on load
            ApplyIconSize();

            this.Focus();
            this.Activate();
            if(ShowNoItemsMessage) MenuBorder.Focus();
        }


        private Point GetCursorPosition()
        {
            var pos = System.Windows.Forms.Control.MousePosition;
            return new Point(pos.X, pos.Y);
        }

        private void UpdateNoItemsMessage() => ShowNoItemsMessage = !LauncherItemsOnCanvas.Any() || LauncherItemsOnCanvas.All(it => it.ExecutablePath == "NO_ACTION");
        private void ApplyItemPosition(LauncherItem item, double x, double y) { if(item != null) { item.X = x; item.Y = y; } }
        private LauncherItem FindItemById(Guid id) => LauncherItemsOnCanvas.FirstOrDefault(item => item.Id == id);

        public static T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if(parent == null) return null;
            for(int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                if(child is T tChild) return tChild;
                T childOfChild = FindVisualChild<T>(child);
                if(childOfChild != null) return childOfChild;
            }
            return null;
        }

        private void EnsureWindowIsOnScreen()
        {
            double sW = SystemParameters.VirtualScreenWidth, sH = SystemParameters.VirtualScreenHeight;
            if(this.Left + this.Width > sW) this.Left = sW - this.Width;
            if(this.Top + this.Height > sH) this.Top = sH - this.Height;
            if(this.Left < 0) this.Left = 0;
            if(this.Top < 0) this.Top = 0;
        }

        private void Window_Deactivated(object sender, EventArgs e)
        {
            if(!_isCurrentlyDragging && !_isOpeningSettings && !_isShowingInputDialog && !IsAnyContextMenuOpen())
            {
                try { this.Close(); }
                catch(Exception ex) { Debug.WriteLine($"Err closing on deactivate: {ex.Message}"); }
            }
        }

        private bool IsAnyContextMenuOpen()
        {
            if(BackgroundContextMenu.IsOpen) return true;
            foreach(var itemData in LauncherItemsHostControl.Items)
            {
                var c = LauncherItemsHostControl.ItemContainerGenerator.ContainerFromItem(itemData) as ContentPresenter;
                if(c != null)
                {
                    c.ApplyTemplate();
                    var cc = VisualTreeHelper.GetChild(c, 0) as ContentControl;
                    if(cc != null)
                    {
                        cc.ApplyTemplate();
                        var b = cc.Template.FindName("IconBorder", cc) as Border;
                        if(b?.ContextMenu?.IsOpen == true) return true;
                    }
                }
            }
            return false;
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            SaveCurrentLayoutAsDefault();
            try
            {
                Settings.Default.LauncherMenuX = this.Left;
                Settings.Default.LauncherMenuY = this.Top;
                Settings.Default.LauncherMenuWidth = this.ActualWidth;
                Settings.Default.LauncherMenuHeight = this.ActualHeight;
                Settings.Default.Save();
            }
            catch(System.Configuration.SettingsPropertyNotFoundException ex)
            {
                Debug.WriteLine($"SETTINGS NOT FOUND on closing: {ex}");
            }
        }

        private void SaveCurrentLayoutAsDefault()
        {
            if(LauncherItemsOnCanvas != null && _configManager != null)
            {
                Debug.WriteLine("SaveCurrentLayoutAsDefault (LauncherItemsConfig)");
                _configManager.SaveLauncherItems(new System.Collections.Generic.List<LauncherItem>(LauncherItemsOnCanvas));
            }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if(e.Key == Key.Escape) { this.Close(); return; }
            bool ctrl = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);
            if(ctrl && e.Key == Key.Z) { _dragHistory.Undo(FindItemById); e.Handled = true; }
            else if(ctrl && e.Key == Key.Y) { _dragHistory.Redo(FindItemById); e.Handled = true; }
        }

        private void LaunchItem(LauncherItem item)
        {
            Debug.WriteLine($"LaunchItem: {item?.DisplayName ?? "NULL"}");
            if(item == null || string.IsNullOrWhiteSpace(item.ExecutablePath) || item.ExecutablePath == "NO_ACTION")
            {
                if(item?.ExecutablePath != "NO_ACTION") MessageBox.Show("Path not configured.", "Error");
                return;
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = Environment.ExpandEnvironmentVariables(item.ExecutablePath),
                    Arguments = Environment.ExpandEnvironmentVariables(item.Arguments ?? ""),
                    UseShellExecute = true
                };

                if(!string.IsNullOrWhiteSpace(item.WorkingDirectory))
                {
                    string wd = Environment.ExpandEnvironmentVariables(item.WorkingDirectory);
                    if(Directory.Exists(wd)) psi.WorkingDirectory = wd;
                    else
                    {
                        string ed = Path.GetDirectoryName(psi.FileName);
                        if(Directory.Exists(ed)) psi.WorkingDirectory = ed;
                    }
                }
                else
                {
                    string ed = Path.GetDirectoryName(psi.FileName);
                    if(Directory.Exists(ed)) psi.WorkingDirectory = ed;
                }

                Process.Start(psi);
                Debug.WriteLine($"Started: {item.DisplayName}");
            }
            catch(Exception ex)
            {
                MessageBox.Show($"Launch failed for '{item.DisplayName}': {ex.Message}", "Error");
                Debug.WriteLine($"Launch Err: {ex}");
            }
        }

        private void Icon_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            Debug.WriteLine("Icon_PreviewMouseLeftButtonDown");
            if(sender is FrameworkElement fe && fe.DataContext is LauncherItem li)
            {
                _draggedItemVisual = fe;
                _draggedLauncherItemModel = li;
                if(_iconCanvasInstance == null)
                {
                    _iconCanvasInstance = FindVisualChild<Canvas>(LauncherItemsHostControl);
                    if(_iconCanvasInstance == null)
                    {
                        Debug.WriteLine("CRITICAL: IconCanvas not found!");
                        return;
                    }
                }
                _mouseDragStartPoint_CanvasRelative = e.GetPosition(_iconCanvasInstance);
                _originalItemPositionBeforeDrag = new Point(li.X, li.Y);
                _leftMouseDownOnIcon = true;
                e.Handled = true;
            }
        }

        private void Icon_MouseMove(object sender, MouseEventArgs e)
        {
            if(_leftMouseDownOnIcon && e.LeftButton == MouseButtonState.Pressed)
            {
                if(!_isCurrentlyDragging)
                {
                    if(_iconCanvasInstance == null) return;
                    Point cPos = e.GetPosition(_iconCanvasInstance);
                    if(Math.Abs(cPos.X - _mouseDragStartPoint_CanvasRelative.X) > SystemParameters.MinimumHorizontalDragDistance ||
                       Math.Abs(cPos.Y - _mouseDragStartPoint_CanvasRelative.Y) > SystemParameters.MinimumVerticalDragDistance)
                    {
                        Debug.WriteLine("Starting icon drag");
                        _isCurrentlyDragging = true;
                        _draggedItemVisual?.CaptureMouse();
                        if(_draggedItemVisual != null) _draggedItemVisual.Cursor = Cursors.Hand;
                    }
                }

                if(_isCurrentlyDragging)
                {
                    if(_iconCanvasInstance == null || _draggedLauncherItemModel == null || _draggedItemVisual == null) return;
                    Point cMousePos = e.GetPosition(_iconCanvasInstance);
                    double oX = cMousePos.X - _mouseDragStartPoint_CanvasRelative.X;
                    double oY = cMousePos.Y - _mouseDragStartPoint_CanvasRelative.Y;
                    double nX = _originalItemPositionBeforeDrag.X + oX;
                    double nY = _originalItemPositionBeforeDrag.Y + oY;
                    double iW = _draggedItemVisual.ActualWidth;
                    double iH = _draggedItemVisual.ActualHeight;
                    if(double.IsNaN(iW) || iW <= 0) iW = 30;
                    if(double.IsNaN(iH) || iH <= 0) iH = 30;
                    nX = Math.Max(0, Math.Min(nX, _iconCanvasInstance.ActualWidth - iW));
                    nY = Math.Max(0, Math.Min(nY, _iconCanvasInstance.ActualHeight - iH));
                    _draggedLauncherItemModel.X = nX;
                    _draggedLauncherItemModel.Y = nY;
                }
            }
        }

        private void Icon_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            Debug.WriteLine($"IconUp. Dragging: {_isCurrentlyDragging}, MouseDownOnIcon: {_leftMouseDownOnIcon}");
            bool wasDragging = _isCurrentlyDragging;
            LauncherItem itemClick = null;
            if(_leftMouseDownOnIcon && sender is FrameworkElement fe)
            {
                itemClick = fe.DataContext as LauncherItem;
                if(_isCurrentlyDragging)
                {
                    if(_draggedLauncherItemModel != null && _draggedItemVisual != null)
                    {
                        if(Math.Abs(_draggedLauncherItemModel.X - _originalItemPositionBeforeDrag.X) > 0.1 ||
                           Math.Abs(_draggedLauncherItemModel.Y - _originalItemPositionBeforeDrag.Y) > 0.1)
                        {
                            _dragHistory.RecordDrag(_draggedLauncherItemModel, _originalItemPositionBeforeDrag.X, _originalItemPositionBeforeDrag.Y);
                            Debug.WriteLine($"Drag recorded: {_draggedLauncherItemModel.DisplayName}");
                        }
                    }
                    _draggedItemVisual?.ReleaseMouseCapture();
                    if(_draggedItemVisual != null) _draggedItemVisual.Cursor = null;
                }
            }
            _isCurrentlyDragging = false;
            bool wasLMD = _leftMouseDownOnIcon;
            _leftMouseDownOnIcon = false;
            _draggedItemVisual = null;
            _draggedLauncherItemModel = null;
            if(wasLMD && !wasDragging && itemClick != null)
            {
                Debug.WriteLine($"Single click: {itemClick.DisplayName}");
                LaunchItem(itemClick);
                this.Close();
            }
        }

        private void OptionsButton_Click(object sender, RoutedEventArgs e) => OpenSettingsWindow();

        private void OpenSettingsWindow()
        {
            _isOpeningSettings = true;
            SaveCurrentLayoutAsDefault();
            Debug.WriteLine("Saved current layout before opening settings.");
            var s = new SettingsWindow { Owner = this };
            s.Closed += SettingsWindow_Closed;
            this.Hide();
            s.ShowDialog();
        }


        private void RefreshIconSizes()
        {
            // Force the ItemsControl to refresh its template bindings
            if(LauncherItemsHostControl != null)
            {
                LauncherItemsHostControl.UpdateLayout();
                LauncherItemsHostControl.Items.Refresh();
            }

            // Force layout updates
            this.UpdateLayout();
            InvalidateVisual();
        }


        private void MenuBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if(e.Handled) { Debug.WriteLine("MenuBorder LBtnDown: Handled by icon."); return; }
            Debug.WriteLine("MenuBorder LBtnDown for window drag");
            if(e.ButtonState == MouseButtonState.Pressed)
            {
                try { this.DragMove(); } catch(InvalidOperationException) { }
            }
        }

        private void ResizeDragDelta(object sender, DragDeltaEventArgs e)
        {
            double nW = Width + e.HorizontalChange, nH = Height + e.VerticalChange;
            if(nW >= MinWidth) Width = nW;
            if(nH >= MinHeight) Height = nH;
        }

        private void IconBorder_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            Debug.WriteLine("IconBorder_ContextMenuOpening");
            if(sender is FrameworkElement fe && fe.DataContext is LauncherItem item)
            {
                if(fe.ContextMenu != null)
                {
                    fe.ContextMenu.DataContext = item;
                    Debug.WriteLine($"CtxMenu DC set: {item.DisplayName}");
                }
                else
                {
                    Debug.WriteLine("Ctx on IconBorder is null!");
                    e.Handled = true;
                }
            }
            else
            {
                Debug.WriteLine("Sender not FE or DC not LI in CtxMenuOpening.");
                e.Handled = true;
            }
        }

        private void IconBorder_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            Debug.WriteLine("IconBorder_PreviewMouseRightButtonUp");
            if(e.LeftButton == MouseButtonState.Pressed && _leftMouseDownOnIcon)
            {
                Debug.WriteLine("Ctx skipped: LBtn down.");
                return;
            }
            if(_isCurrentlyDragging)
            {
                Debug.WriteLine("Ctx skipped: dragging.");
                return;
            }
        }

        private LauncherItem GetLauncherItemFromContextMenu(object sender)
        {
            Debug.WriteLine($"GetLIFromCtxMenu by: {sender?.GetType().FullName}");
            if(sender is MenuItem mi)
            {
                if(mi.DataContext is LauncherItem iDC)
                {
                    Debug.WriteLine($"Found LI '{iDC.DisplayName}' from MI.DC.");
                    return iDC;
                }
                Debug.WriteLine($"MI.DC not LI: {mi.DataContext?.GetType().FullName}. Trying Parent CtxMenu.");
                if(mi.Parent is ContextMenu pcm && pcm.DataContext is LauncherItem iPDC)
                {
                    Debug.WriteLine($"Found LI '{iPDC.DisplayName}' from PCM.DC.");
                    return iPDC;
                }
                Debug.WriteLine($"PCM.DC also not LI: {(mi.Parent as ContextMenu)?.DataContext?.GetType().FullName}");
            }
            Debug.WriteLine("Could not get LI from CtxMenu sender.");
            return null;
        }

        private void IconContextMenu_Launch_Click(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("CtxMenuLaunch");
            var i = GetLauncherItemFromContextMenu(sender);
            if(i != null) { LaunchItem(i); Close(); }
            else Debug.WriteLine("LaunchClick: Null item");
        }

        private void IconContextMenu_OpenFileLocation_Click(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("CtxMenuOpenLocation");
            var item = GetLauncherItemFromContextMenu(sender);
            if(item != null && !string.IsNullOrWhiteSpace(item.ExecutablePath))
            {
                try
                {
                    string p = Environment.ExpandEnvironmentVariables(item.ExecutablePath);
                    if(File.Exists(p)) Process.Start("explorer.exe", $"/select,\"{p}\"");
                    else if(Directory.Exists(p)) Process.Start("explorer.exe", $"\"{p}\"");
                    else
                    {
                        string d = Path.GetDirectoryName(p);
                        if(Directory.Exists(d)) Process.Start("explorer.exe", $"\"{d}\"");
                        else MessageBox.Show("Cannot find location.", "Error");
                    }
                }
                catch(Exception ex) { MessageBox.Show($"Err: {ex.Message}", "Error"); }
            }
            else Debug.WriteLine("OpenLocationClick: Null item/path");
        }

        private void IconContextMenu_EditSettings_Click(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("CtxMenuEditSettings");
            var i = GetLauncherItemFromContextMenu(sender);
            if(i != null)
            {
                _isOpeningSettings = true;

                // Clone the item to preserve original position data
                var editItem = new LauncherItem
                {
                    Id = i.Id,
                    DisplayName = i.DisplayName,
                    ExecutablePath = i.ExecutablePath,
                    IconPath = i.IconPath,
                    Arguments = i.Arguments,
                    WorkingDirectory = i.WorkingDirectory,
                    X = i.X,  // Preserve position
                    Y = i.Y   // Preserve position
                };

                var ed = new LauncherItemEditorWindow(editItem) { Owner = this };
                if(ed.ShowDialog() == true)
                {
                    var oI = LauncherItemsOnCanvas.FirstOrDefault(x => x.Id == i.Id);
                    int idx = oI != null ? LauncherItemsOnCanvas.IndexOf(oI) : -1;
                    if(idx != -1)
                    {
                        // Preserve the original position when updating
                        ed.Item.X = oI.X;
                        ed.Item.Y = oI.Y;
                        LauncherItemsOnCanvas[idx] = ed.Item;
                        SaveCurrentLayoutAsDefault();
                        Debug.WriteLine($"EditSettings updated: {ed.Item.DisplayName} at position ({ed.Item.X}, {ed.Item.Y})");
                    }
                    else Debug.WriteLine($"EditSettings: Cannot find original {i.DisplayName}");
                }
                _isOpeningSettings = false;
                Focus();
            }
            else Debug.WriteLine("EditSettingsClick: Null item");
        }

        private void IconContextMenu_FileProperties_Click(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("CtxMenuFileProps");
            var i = GetLauncherItemFromContextMenu(sender);
            if(i != null && !string.IsNullOrWhiteSpace(i.ExecutablePath))
            {
                string fp = Environment.ExpandEnvironmentVariables(i.ExecutablePath);
                if(File.Exists(fp) || Directory.Exists(fp))
                {
                    try
                    {
                        SHELLEXECUTEINFO sei = new SHELLEXECUTEINFO
                        {
                            cbSize = Marshal.SizeOf(typeof(SHELLEXECUTEINFO)),
                            fMask = SEE_MASK_INVOKEIDLIST,
                            hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle,
                            lpVerb = "properties",
                            lpFile = fp,
                            nShow = SW_SHOWNORMAL
                        };
                        if(!ShellExecuteEx(ref sei))
                        {
                            int err = Marshal.GetLastWin32Error();
                            MessageBox.Show($"Cannot show file props. Err: {err}", "Error");
                            Debug.WriteLine($"ShellEx Err: {err} for {fp}");
                        }
                        else Debug.WriteLine($"Showing props for {fp}");
                    }
                    catch(Exception ex)
                    {
                        MessageBox.Show($"Err showing file props: {ex.Message}", "Error");
                        Debug.WriteLine($"Ex showing props: {ex}");
                    }
                }
                else MessageBox.Show($"Not found: {fp}", "Error");
            }
            else Debug.WriteLine("FilePropsClick: Null item/path");
        }

        private void IconContextMenu_Remove_Click(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("CtxMenuRemove");

            // Set flag to prevent window from closing
            _isShowingInputDialog = true;

            var i = GetLauncherItemFromContextMenu(sender);
            if(i != null)
            {
                var result = MessageBox.Show($"Remove '{i.DisplayName}'?", "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if(result == MessageBoxResult.Yes)
                {
                    LauncherItemsOnCanvas.Remove(i);
                    UpdateNoItemsMessage();
                    _dragHistory.ClearHistory();
                    SaveCurrentLayoutAsDefault();
                    Debug.WriteLine($"Removed: {i.DisplayName}");

                    // Force the UI to refresh if needed
                    if(LauncherItemsHostControl != null)
                    {
                        LauncherItemsHostControl.Items.Refresh();
                    }
                }
            }
            else
            {
                Debug.WriteLine("RemoveClick: Null item");
            }

            // Reset flag and ensure window stays active
            _isShowingInputDialog = false;
            this.Activate();
            this.Focus();
        }

        private void PersistAllNamedLayouts(System.Collections.Generic.List<NamedLayout> layoutsToSave)
        {
            if(Settings.Default.SavedLayouts == null)
            {
                Settings.Default.SavedLayouts = new StringCollection();
            }
            Settings.Default.SavedLayouts.Clear();
            foreach(var namedLayout in layoutsToSave)
            {
                try
                {
                    string layoutEntryJson = JsonConvert.SerializeObject(namedLayout, Formatting.None);
                    Settings.Default.SavedLayouts.Add(layoutEntryJson);
                }
                catch(Exception ex)
                {
                    Debug.WriteLine($"Error serializing NamedLayout '{namedLayout.Name}': {ex.Message}");
                }
            }
            Settings.Default.Save();
        }

        private void Background_ContextMenuOpening(object sender, ContextMenuEventArgs e) => PopulateLoadLayoutMenuItems();

        private void PopulateLoadLayoutMenuItems()
        {
            LoadLayoutMenuItemHost.Items.Clear();
            System.Collections.Generic.List<NamedLayout> savedLayouts = GetSavedNamedLayouts();

            if(!savedLayouts.Any())
            {
                MenuItem noLayoutsItem = new MenuItem { Header = "(No saved layouts)", IsEnabled = false };
                LoadLayoutMenuItemHost.Items.Add(noLayoutsItem);
                LoadLayoutMenuItemHost.IsEnabled = false;
            }
            else
            {
                LoadLayoutMenuItemHost.IsEnabled = true;
                foreach(var namedLayout in savedLayouts.OrderBy(L => L.Name))
                {
                    MenuItem loadItem = new MenuItem { Header = namedLayout.Name, Tag = namedLayout };
                    loadItem.Click += LoadSpecificLayout_Click;
                    LoadLayoutMenuItemHost.Items.Add(loadItem);
                }
            }
        }
        private void LoadSpecificLayout_Click(object sender, RoutedEventArgs e)
        {
            if(sender is MenuItem menuItem && menuItem.Tag is NamedLayout layoutToLoad)
            {
                Debug.WriteLine($"Loading layout: {layoutToLoad.Name}");
                ReloadItemsFromConfig(false, layoutToLoad);
            }
        }

        private void ManageLayouts_Click(object sender, RoutedEventArgs e)
        {
            _isShowingInputDialog = true;

            System.Collections.Generic.List<NamedLayout> savedLayouts = GetSavedNamedLayouts();
            if(!savedLayouts.Any())
            {
                MessageBox.Show("No layouts have been saved yet.", "Manage Layouts", MessageBoxButton.OK, MessageBoxImage.Information);
                _isShowingInputDialog = false;
                return;
            }

            var manageWindow = new ManageLayoutsWindow(savedLayouts) { Owner = this };
            var result = manageWindow.ShowDialog();

            if(result == true)
            {
                // Either layouts were modified or user wants to load a layout
                if(manageWindow.LayoutsModified)
                {
                    // Save the updated layouts
                    PersistAllNamedLayouts(manageWindow.GetUpdatedLayouts());
                    MessageBox.Show("Layout changes saved.", "Manage Layouts", MessageBoxButton.OK, MessageBoxImage.Information);
                }

                // Check if user wanted to load a specific layout
                if(manageWindow.SelectedLayoutToLoad != null)
                {
                    ReloadItemsFromConfig(false, manageWindow.SelectedLayoutToLoad);
                }
            }
            else if(manageWindow.LayoutsModified)
            {
                // User closed but made changes
                var saveResult = MessageBox.Show("Save changes to layouts?", "Save Changes",
                                           MessageBoxButton.YesNo, MessageBoxImage.Question);
                if(saveResult == MessageBoxResult.Yes)
                {
                    PersistAllNamedLayouts(manageWindow.GetUpdatedLayouts());
                }
            }

            _isShowingInputDialog = false;
        }

        private void BackgroundContextMenu_AddItem_Click(object sender, RoutedEventArgs e) => OpenSettingsWindow();

        private void Organize_AlignToGrid_Click(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("Organize_AlignToGrid_Click");
            if(_iconCanvasInstance == null || !LauncherItemsOnCanvas.Any()) return;

            double cellSize = GridCellSize;
            double currentX = StackPadding;
            double currentY = StackPadding;
            double maxRowWidth = _iconCanvasInstance.ActualWidth > 0 ? _iconCanvasInstance.ActualWidth : this.Width - 20;

            foreach(var item in LauncherItemsOnCanvas.OrderBy(i => i.Y).ThenBy(i => i.X))
            {
                item.X = currentX;
                item.Y = currentY;
                currentX += cellSize;
                if(currentX + cellSize > maxRowWidth)
                {
                    currentX = StackPadding;
                    currentY += cellSize;
                }
            }
            _dragHistory.ClearHistory();
            SaveCurrentLayoutAsDefault();
        }

        private void Organize_StackVertically_Click(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("Organize_StackVertically_Click");
            if(!LauncherItemsOnCanvas.Any()) return;

            double cellSize = GridCellSize;
            double currentY = StackPadding;
            double xPos = StackPadding;

            foreach(var item in LauncherItemsOnCanvas.OrderBy(i => i.DisplayName))
            {
                item.X = xPos;
                item.Y = currentY;
                currentY += cellSize;
            }
            _dragHistory.ClearHistory();
            SaveCurrentLayoutAsDefault();
        }

        private void Organize_StackHorizontally_Click(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("Organize_StackHorizontally_Click");
            if(!LauncherItemsOnCanvas.Any()) return;

            double cellSize = GridCellSize;
            double currentX = StackPadding;
            double yPos = StackPadding;

            foreach(var item in LauncherItemsOnCanvas.OrderBy(i => i.DisplayName))
            {
                item.X = currentX;
                item.Y = yPos;
                currentX += cellSize;
            }
            _dragHistory.ClearHistory();
            SaveCurrentLayoutAsDefault();
        }
    }
}