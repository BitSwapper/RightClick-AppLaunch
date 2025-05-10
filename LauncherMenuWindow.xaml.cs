using System;
using System.Collections.Generic; // Added for List
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

        // --- NEW FIELDS for selection and multi-drag ---
        private List<LauncherItem> _selectedLauncherItems = new List<LauncherItem>();
        // Stores original positions of ALL selected items at the start of a drag
        private Dictionary<Guid, Point> _dragStartPositionsSelectedItems = new Dictionary<Guid, Point>();
        DateTime _lastMouseDownTime = DateTime.MinValue; // Not strictly needed if using e.ClickCount
        DateTime _lastMouseDownTimeOnIcon = DateTime.MinValue; // Not strictly needed if using e.ClickCount
        FrameworkElement _lastMouseDownItemVisual = null; // Not strictly needed if using e.ClickCount
        const int DoubleClickThresholdMs = 250;
        // ------------------------------------------------

        private Point _mouseDragStartPoint_CanvasRelative; // Mouse position on canvas at drag start
        private FrameworkElement _draggedItemVisual; // The specific visual element clicked to initiate drag
        private LauncherItem _draggedLauncherItemModel; // The data item of the _draggedItemVisual (primary reference)
        private Point _originalItemPositionBeforeDrag; // Original X,Y of _draggedLauncherItemModel (primary reference)
        private bool _isCurrentlyDragging = false;
        private bool _leftMouseDownOnIcon = false; // Flag to track if mouse down originated on an icon
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

        private double GridCellSize => this.CurrentIconSize + 12 + Settings.Default.IconSpacing;
        private const double StackPadding = 5.0;

        public LauncherMenuWindow(ObservableCollection<LauncherItem> items, Point position, LauncherConfigManager configManager)
        {
            InitializeComponent();
            DataContext = this;
            _configManager = configManager;
            _dragHistory = new DragHistoryManager(ApplyItemPosition);

            var initialItems = items ?? new ObservableCollection<LauncherItem>();
            foreach(var item in initialItems) item.IsSelected = false; // Ensure IsSelected is false
            LauncherItemsOnCanvas = initialItems;

            UpdateNoItemsMessage();
            CurrentIconSize = Settings.Default.IconSize;

            if(Settings.Default.SavedLayouts == null)
            {
                Settings.Default.SavedLayouts = new StringCollection();
            }

            Point cursorPosition = GetCursorPosition();
            this.Left = cursorPosition.X;
            this.Top = cursorPosition.Y;

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

        private void DeselectAllItems(LauncherItem exceptThis = null)
        {
            // ToList() creates a copy, allowing modification of _selectedLauncherItems within the loop
            foreach(var item in _selectedLauncherItems.ToList()) // Iterate over a copy
            {
                if(item != exceptThis)
                {
                    item.IsSelected = false;
                }
            }
            _selectedLauncherItems.Clear();
            if(exceptThis != null && exceptThis.IsSelected) // If exceptThis was (re)selected, add it
            {
                _selectedLauncherItems.Add(exceptThis);
            }
        }

        private void ToggleItemSelected(LauncherItem item)
        {
            item.IsSelected = !item.IsSelected;
            if(item.IsSelected)
            {
                if(!_selectedLauncherItems.Contains(item))
                    _selectedLauncherItems.Add(item);
            }
            else
            {
                _selectedLauncherItems.Remove(item);
            }
        }

        private void Icon_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            Debug.WriteLine("Icon_PreviewMouseLeftButtonDown");
            if(!(sender is FrameworkElement fe && fe.DataContext is LauncherItem clickedItem))
                return;

            bool ctrlPressed = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);

            // --- Manual Double-Click Detection ---
            bool isDoubleClick = false;
            DateTime currentClickTime = DateTime.Now;
            if(_lastMouseDownItemVisual == fe && // Click is on the same item
                (currentClickTime - _lastMouseDownTimeOnIcon).TotalMilliseconds < DoubleClickThresholdMs)
            {
                isDoubleClick = true;
                // Reset for next potential double click sequence, or to prevent triple/quadruple clicks
                // from being treated as part of the same double click.
                _lastMouseDownTimeOnIcon = DateTime.MinValue;
                _lastMouseDownItemVisual = null;
            }
            else
            {
                // This is a first click (or too slow for a double click, or on a different item)
                _lastMouseDownTimeOnIcon = currentClickTime;
                _lastMouseDownItemVisual = fe;
            }
            // We can still check e.ClickCount as a secondary measure or for debugging, but 'isDoubleClick' will be primary
            // Debug.WriteLine($"WPF e.ClickCount: {e.ClickCount}, Manual isDoubleClick: {isDoubleClick}");

            if(isDoubleClick)
            {
                Debug.WriteLine($"Double click (manual) on: {clickedItem.DisplayName}");
                LaunchItem(clickedItem);
                this.Close();
                e.Handled = true;
                return;
            }

            // --- Single Click / Drag Start Logic (if not a double click) ---
            _leftMouseDownOnIcon = true;

            if(ctrlPressed) // Ctrl IS HELD during MouseDown
            {
                ToggleItemSelected(clickedItem);
            }
            else // Ctrl IS NOT HELD during MouseDown
            {
                if(!clickedItem.IsSelected)
                {
                    DeselectAllItems();
                    clickedItem.IsSelected = true;
                    _selectedLauncherItems.Add(clickedItem);
                }
            }

            if(_selectedLauncherItems.Any())
            {
                _draggedItemVisual = fe;
                _draggedLauncherItemModel = clickedItem;

                if(_iconCanvasInstance == null)
                {
                    _iconCanvasInstance = FindVisualChild<Canvas>(LauncherItemsHostControl);
                    if(_iconCanvasInstance == null) { Debug.WriteLine("CRITICAL: IconCanvas not found!"); return; }
                }
                _mouseDragStartPoint_CanvasRelative = e.GetPosition(_iconCanvasInstance);

                _dragStartPositionsSelectedItems.Clear();
                foreach(var selItem in _selectedLauncherItems)
                {
                    _dragStartPositionsSelectedItems[selItem.Id] = new Point(selItem.X, selItem.Y);
                }

                _originalItemPositionBeforeDrag = new Point(clickedItem.X, clickedItem.Y);
            }

            e.Handled = true;
        }

        private void Icon_MouseMove(object sender, MouseEventArgs e)
        {
            if(!_leftMouseDownOnIcon || e.LeftButton != MouseButtonState.Pressed || !_selectedLauncherItems.Any())
                return;

            if(!_isCurrentlyDragging)
            {
                if(_iconCanvasInstance == null) return;
                Point currentCanvasPos = e.GetPosition(_iconCanvasInstance);
                if(Math.Abs(currentCanvasPos.X - _mouseDragStartPoint_CanvasRelative.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(currentCanvasPos.Y - _mouseDragStartPoint_CanvasRelative.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    Debug.WriteLine("Starting group icon drag");
                    _isCurrentlyDragging = true;
                    _draggedItemVisual?.CaptureMouse();
                    if(_draggedItemVisual != null) _draggedItemVisual.Cursor = Cursors.Hand;
                }
            }

            if(_isCurrentlyDragging)
            {
                if(_iconCanvasInstance == null || _draggedLauncherItemModel == null) return;

                Point currentMousePosOnCanvas = e.GetPosition(_iconCanvasInstance);

                double primaryTargetX = _originalItemPositionBeforeDrag.X + (currentMousePosOnCanvas.X - _mouseDragStartPoint_CanvasRelative.X);
                double primaryTargetY = _originalItemPositionBeforeDrag.Y + (currentMousePosOnCanvas.Y - _mouseDragStartPoint_CanvasRelative.Y);

                bool shiftPressed = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);

                if(shiftPressed)
                {
                    double fullGridStep = this.GridCellSize;
                    if(fullGridStep > 0)
                    {
                        double snapStep = fullGridStep / 2.0;
                        if(snapStep > 0)
                        {
                            const double stackPaddingValue = StackPadding;
                            primaryTargetX = stackPaddingValue + Math.Round((primaryTargetX - stackPaddingValue) / snapStep) * snapStep;
                            primaryTargetY = stackPaddingValue + Math.Round((primaryTargetY - stackPaddingValue) / snapStep) * snapStep;
                        }
                    }
                }

                double actualDeltaX = primaryTargetX - _originalItemPositionBeforeDrag.X;
                double actualDeltaY = primaryTargetY - _originalItemPositionBeforeDrag.Y;

                foreach(var selectedItem in _selectedLauncherItems)
                {
                    if(!_dragStartPositionsSelectedItems.TryGetValue(selectedItem.Id, out Point originalPosOfThisItem))
                    {
                        Debug.WriteLine($"ERROR: Could not find original position for {selectedItem.DisplayName} during drag.");
                        originalPosOfThisItem = new Point(selectedItem.X - actualDeltaX, selectedItem.Y - actualDeltaY); // Try to reconstruct
                    }

                    double newX = originalPosOfThisItem.X + actualDeltaX;
                    double newY = originalPosOfThisItem.Y + actualDeltaY;

                    double itemWidth = this.CurrentIconSize + 12;
                    double itemHeight = this.CurrentIconSize + 12;

                    newX = Math.Max(0, Math.Min(newX, _iconCanvasInstance.ActualWidth - itemWidth));
                    newY = Math.Max(0, Math.Min(newY, _iconCanvasInstance.ActualHeight - itemHeight));

                    if(shiftPressed && StackPadding > 0)
                    {
                        newX = Math.Max(StackPadding, newX);
                        newY = Math.Max(StackPadding, newY);
                    }

                    selectedItem.X = newX;
                    selectedItem.Y = newY;
                }
            }
        }

        private void Icon_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            Debug.WriteLine($"IconUp. Dragging: {_isCurrentlyDragging}, MouseDownOnIcon: {_leftMouseDownOnIcon}");

            if(_leftMouseDownOnIcon)
            {
                if(_isCurrentlyDragging)
                {
                    Debug.WriteLine("Drag ended for selected items.");
                    foreach(var item in _selectedLauncherItems)
                    {
                        if(_dragStartPositionsSelectedItems.TryGetValue(item.Id, out Point originalPos))
                        {
                            if(Math.Abs(item.X - originalPos.X) > 0.1 || Math.Abs(item.Y - originalPos.Y) > 0.1)
                            {
                                _dragHistory.RecordDrag(item, originalPos.X, originalPos.Y);
                                Debug.WriteLine($"Drag recorded for: {item.DisplayName}");
                            }
                        }
                    }
                    _draggedItemVisual?.ReleaseMouseCapture();
                    if(_draggedItemVisual != null) _draggedItemVisual.Cursor = null;
                }
            }

            _isCurrentlyDragging = false;
            _leftMouseDownOnIcon = false;
            _dragStartPositionsSelectedItems.Clear();
            // Do not null out _draggedItemVisual or _draggedLauncherItemModel here, as context menus might rely on them.
            // e.Handled should have been set in PreviewMouseLeftButtonDown if the icon was clicked.
        }

        private void MenuBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if(e.Handled)
            {
                Debug.WriteLine("MenuBorder LBtnDown: Handled by icon.");
                return;
            }

            Debug.WriteLine("MenuBorder LBtnDown for window drag OR deselect all.");
            DeselectAllItems();

            if(e.ButtonState == MouseButtonState.Pressed)
            {
                try { this.DragMove(); } catch(InvalidOperationException) { /* Can happen */ }
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _iconCanvasInstance = FindVisualChild<Canvas>(LauncherItemsHostControl);
            if(_iconCanvasInstance == null) Debug.WriteLine("WARNING: IconCanvas instance not found!");

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
            if(LauncherItemsHostControl == null) return false;
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
            DeselectAllItems();
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
            else if(ctrl && e.Key == Key.A) // Ctrl+A to select all
            {
                foreach(var item in LauncherItemsOnCanvas)
                {
                    if(!item.IsSelected) ToggleItemSelected(item); // Use Toggle to add to _selectedLauncherItems
                }
                e.Handled = true;
            }
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

        private void OptionsButton_Click(object sender, RoutedEventArgs e) => OpenSettingsWindow();

        private void OpenSettingsWindow()
        {
            _isOpeningSettings = true;
            DeselectAllItems();
            SaveCurrentLayoutAsDefault();
            Debug.WriteLine("Saved current layout before opening settings.");
            var s = new SettingsWindow { Owner = this };
            s.Closed += SettingsWindow_Closed;
            this.Hide();
            s.ShowDialog();
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
                // Ensure the right-clicked item is selected
                if(!item.IsSelected)
                {
                    DeselectAllItems();
                    ToggleItemSelected(item); // Select only this item
                }

                if(fe.ContextMenu != null)
                {
                    fe.ContextMenu.DataContext = item;
                    // Adjust context menu items if multiple items are selected
                    bool multipleSelected = _selectedLauncherItems.Count > 1;
                    foreach(var menuItemBase in fe.ContextMenu.Items.OfType<MenuItem>())
                    {
                        // Example: Disable "Edit Settings" or "File Properties" if multiple are selected
                        string header = menuItemBase.Header as string;
                        if(header == "Edit Launcher Settings..." || header == "File Properties..." || header == "Open File Location")
                        {
                            menuItemBase.IsEnabled = !multipleSelected;
                        }
                    }
                    Debug.WriteLine($"CtxMenu DC set: {item.DisplayName}, MultiSelected: {multipleSelected}");
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
            // No special handling needed here now, ContextMenuOpening handles selection.
            // Prevent context menu if dragging was in progress (though unlikely for right button)
            if(_isCurrentlyDragging)
            {
                Debug.WriteLine("Ctx skipped: dragging.");
                e.Handled = true; // Prevent context menu if a drag was just completed.
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
            foreach(var itemToLaunch in _selectedLauncherItems.ToList()) // ToList in case collection changes
            {
                LaunchItem(itemToLaunch);
            }
            if(_selectedLauncherItems.Any()) Close();
            else Debug.WriteLine("LaunchClick: No items were selected for launch via context menu.");
        }

        private void IconContextMenu_OpenFileLocation_Click(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("CtxMenuOpenLocation");
            var item = GetLauncherItemFromContextMenu(sender); // This should be the primary item
            if(item != null && !string.IsNullOrWhiteSpace(item.ExecutablePath) && _selectedLauncherItems.Count <= 1) // Only if one item is selected
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
            else Debug.WriteLine("OpenLocationClick: Null item/path or multiple items selected.");
        }

        private void IconContextMenu_EditSettings_Click(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("CtxMenuEditSettings");
            var i = GetLauncherItemFromContextMenu(sender); // Primary item
            if(i != null && _selectedLauncherItems.Count <= 1) // Only if one item is selected
            {
                _isOpeningSettings = true;
                var ed = new LauncherItemEditorWindow(i) { Owner = this };
                if(ed.ShowDialog() == true)
                {
                    var oI = LauncherItemsOnCanvas.FirstOrDefault(x => x.Id == i.Id);
                    int idx = oI != null ? LauncherItemsOnCanvas.IndexOf(oI) : -1;
                    if(idx != -1)
                    {
                        if(ed.Item.X == 0 && ed.Item.Y == 0 && (oI.X != 0 || oI.Y != 0))
                        {
                            ed.Item.X = oI.X;
                            ed.Item.Y = oI.Y;
                        }
                        LauncherItemsOnCanvas[idx] = ed.Item;
                        ed.Item.IsSelected = true; // Ensure it remains selected visually after edit
                        _selectedLauncherItems.Remove(i); // Remove old instance
                        _selectedLauncherItems.Add(ed.Item); // Add new instance
                        SaveCurrentLayoutAsDefault();
                        Debug.WriteLine($"EditSettings updated: {ed.Item.DisplayName}");
                    }
                    else Debug.WriteLine($"EditSettings: Cannot find original {i.DisplayName}");
                }
                _isOpeningSettings = false;
                Focus();
            }
            else Debug.WriteLine("EditSettingsClick: Null item or multiple items selected.");
        }

        private void IconContextMenu_FileProperties_Click(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("CtxMenuFileProps");
            var i = GetLauncherItemFromContextMenu(sender); // Primary item
            if(i != null && !string.IsNullOrWhiteSpace(i.ExecutablePath) && _selectedLauncherItems.Count <= 1) // Only if one item selected
            {
                string fp = Environment.ExpandEnvironmentVariables(i.ExecutablePath);
                if(File.Exists(fp) || Directory.Exists(fp))
                {
                    // ... (SHELLEXECUTEINFO logic) ...
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
            else Debug.WriteLine("FilePropsClick: Null item/path or multiple items selected.");
        }

        private void IconContextMenu_Remove_Click(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("CtxMenuRemove");
            _isShowingInputDialog = true; // Prevent window close during message box

            if(!_selectedLauncherItems.Any())
            {
                Debug.WriteLine("RemoveClick: No items selected.");
                _isShowingInputDialog = false;
                return;
            }

            string message = _selectedLauncherItems.Count == 1
                ? $"Remove '{_selectedLauncherItems.First().DisplayName}'?"
                : $"Remove {_selectedLauncherItems.Count} selected items?";

            var result = MessageBox.Show(message, "Confirm Removal", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if(result == MessageBoxResult.Yes)
            {
                foreach(var itemToRemove in _selectedLauncherItems.ToList()) // Iterate over a copy
                {
                    LauncherItemsOnCanvas.Remove(itemToRemove);
                    Debug.WriteLine($"Removed: {itemToRemove.DisplayName}");
                }
                _selectedLauncherItems.Clear(); // Clear the selection list
                UpdateNoItemsMessage();
                _dragHistory.ClearHistory();
                SaveCurrentLayoutAsDefault();
                if(LauncherItemsHostControl != null) LauncherItemsHostControl.Items.Refresh();
            }
            _isShowingInputDialog = false;
            this.Activate();
            this.Focus();
        }

        private void SaveLayoutAs_Click(object sender, RoutedEventArgs e)
        {
            DeselectAllItems();
            _isShowingInputDialog = true;
            InputDialog inputDialog = new InputDialog("Enter name for this layout:", "My Layout " + DateTime.Now.ToString("yyyy-MM-dd HHmm"))
            { Owner = this };
            if(inputDialog.ShowDialog() == true)
            {
                // ... (rest of SaveLayoutAs_Click logic) ...
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
                    var res = MessageBox.Show($"A layout named '{layoutName}' already exists. Overwrite it?", "Confirm Overwrite", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    if(res == MessageBoxResult.No)
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

        private void ReloadItemsFromConfig(bool loadDefaultLayout = true, NamedLayout layoutToLoad = null)
        {
            DeselectAllItems();
            Debug.WriteLine($"ReloadItemsFromConfig. LoadDefault: {loadDefaultLayout}, Specific Layout: {layoutToLoad?.Name ?? "N/A"}");
            System.Collections.Generic.List<LauncherItem> itemsToLoad = null;
            if(!loadDefaultLayout && layoutToLoad != null)
            {
                // ... (logic to load specific layout) ...
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

                        if(layoutToLoad.WindowWidth > 0 && layoutToLoad.WindowHeight > 0)
                        {
                            this.Width = layoutToLoad.WindowWidth;
                            this.Height = layoutToLoad.WindowHeight;
                        }
                        if(layoutToLoad.IconSize > 0) Settings.Default.IconSize = layoutToLoad.IconSize;
                        if(layoutToLoad.IconSpacing >= 0) Settings.Default.IconSpacing = layoutToLoad.IconSpacing;

                        Settings.Default.Save();
                        ApplyIconSize(); // This will also update CurrentIconSize
                        Debug.WriteLine($"Applied icon size: {Settings.Default.IconSize}, spacing: {Settings.Default.IconSpacing}");
                    }
                }
                catch(Exception ex)
                {
                    MessageBox.Show($"Error deserializing layout '{layoutToLoad.Name}': {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    itemsToLoad = _configManager.LoadLauncherItems(); // Fallback
                }
            }
            else
            {
                itemsToLoad = _configManager.LoadLauncherItems();
                ApplyIconSize(); // Ensure icon size is applied for default load too
            }

            // Set IsSelected to false for all items being loaded
            if(itemsToLoad != null)
            {
                foreach(var item in itemsToLoad) item.IsSelected = false;
            }

            LauncherItemsOnCanvas = new ObservableCollection<LauncherItem>(itemsToLoad ?? new System.Collections.Generic.List<LauncherItem>());
            UpdateNoItemsMessage();
            _dragHistory.ClearHistory();
        }

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
                // ... (logic for GetSavedNamedLayouts) ...
                if(string.IsNullOrWhiteSpace(layoutEntryJson)) continue;
                try
                {
                    NamedLayout namedLayout = JsonConvert.DeserializeObject<NamedLayout>(layoutEntryJson);
                    if(namedLayout != null)
                    {
                        if(namedLayout.IconSize <= 0) namedLayout.IconSize = Settings.Default.IconSize;
                        if(namedLayout.IconSpacing < 0) namedLayout.IconSpacing = Settings.Default.IconSpacing;
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
            CurrentIconSize = Settings.Default.IconSize; // Update the bound property
            if(LauncherItemsOnCanvas != null)
            {
                foreach(var item in LauncherItemsOnCanvas) item.OnPropertyChanged("IconPath");
            }
            this.UpdateLayout();
            this.InvalidateVisual();
        }

        private void SettingsWindow_Closed(object sender, EventArgs e)
        {
            _isOpeningSettings = false;
            ApplyIconSize(); // Apply new icon size
            ReloadItemsFromConfig(true); // Reload default layout which uses current settings
            if(sender is SettingsWindow sw) sw.Closed -= SettingsWindow_Closed;
            this.Show();
            this.Activate();
            this.Focus();
        }

        private void PersistAllNamedLayouts(System.Collections.Generic.List<NamedLayout> layoutsToSave)
        {
            if(Settings.Default.SavedLayouts == null) Settings.Default.SavedLayouts = new StringCollection();
            Settings.Default.SavedLayouts.Clear();
            foreach(var namedLayout in layoutsToSave)
            {
                try
                {
                    string layoutEntryJson = JsonConvert.SerializeObject(namedLayout, Formatting.None);
                    Settings.Default.SavedLayouts.Add(layoutEntryJson);
                }
                catch(Exception ex) { Debug.WriteLine($"Error serializing NamedLayout '{namedLayout.Name}': {ex.Message}"); }
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
            DeselectAllItems();
            _isShowingInputDialog = true;
            System.Collections.Generic.List<NamedLayout> savedLayouts = GetSavedNamedLayouts();
            // ... (rest of ManageLayouts_Click logic) ...
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
                if(manageWindow.LayoutsModified)
                {
                    PersistAllNamedLayouts(manageWindow.GetUpdatedLayouts());
                    MessageBox.Show("Layout changes saved.", "Manage Layouts", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                if(manageWindow.SelectedLayoutToLoad != null)
                {
                    ReloadItemsFromConfig(false, manageWindow.SelectedLayoutToLoad);
                }
            }
            else if(manageWindow.LayoutsModified)
            {
                var saveResult = MessageBox.Show("Save changes to layouts?", "Save Changes", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if(saveResult == MessageBoxResult.Yes) PersistAllNamedLayouts(manageWindow.GetUpdatedLayouts());
            }
            _isShowingInputDialog = false;
        }

        private void Organize_AlignToGrid_Click(object sender, RoutedEventArgs e)
        {
            DeselectAllItems();
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
                if(currentX + cellSize > maxRowWidth && maxRowWidth > cellSize)
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
            DeselectAllItems();
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
            DeselectAllItems();
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