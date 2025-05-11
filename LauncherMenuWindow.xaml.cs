using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
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

namespace RightClickAppLauncher.UI;

public partial class LauncherMenuWindow : Window, INotifyPropertyChanged
{
    ObservableCollection<LauncherItem> _launcherItemsOnCanvas;
    public ObservableCollection<LauncherItem> LauncherItemsOnCanvas
    {
        get => _launcherItemsOnCanvas;
        set { _launcherItemsOnCanvas = value; OnPropertyChanged(nameof(LauncherItemsOnCanvas)); }
    }

    public string MenuTitle { get; set; } = "App Launcher";

    bool _showNoItemsMessage;
    public bool ShowNoItemsMessage
    {
        get => _showNoItemsMessage;
        set { _showNoItemsMessage = value; OnPropertyChanged(nameof(ShowNoItemsMessage)); }
    }

    double _currentIconSize;
    public double CurrentIconSize
    {
        get => _currentIconSize;
        set
        {
            _currentIconSize = value;
            OnPropertyChanged(nameof(CurrentIconSize));
        }
    }

    List<LauncherItem> _selectedLauncherItems = new List<LauncherItem>();
    Dictionary<Guid, Point> _dragStartPositionsSelectedItems = new Dictionary<Guid, Point>();
    DateTime _lastMouseDownTime = DateTime.MinValue;
    DateTime _lastMouseDownTimeOnIcon = DateTime.MinValue;
    FrameworkElement _lastMouseDownItemVisual = null;
    const int DoubleClickThresholdMs = 250;

    Point _mouseDragStartPoint_CanvasRelative;
    FrameworkElement _draggedItemVisual;
    LauncherItem _draggedLauncherItemModel;
    Point _originalItemPositionBeforeDrag;
    bool _isCurrentlyDragging = false;
    bool _leftMouseDownOnIcon = false;
    bool _isOpeningSettings = false;
    bool _isShowingInputDialog = false;
    readonly LauncherConfigManager _configManager;
    Canvas _iconCanvasInstance;
    readonly DragHistoryManager _dragHistory;

    public event PropertyChangedEventHandler PropertyChanged;
    protected void OnPropertyChanged(string propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    struct SHELLEXECUTEINFO
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

    const uint SEE_MASK_INVOKEIDLIST = 12;
    const int SW_SHOWNORMAL = 1;
    [DllImport("shell32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO lpExecInfo);

    double GridCellSize => this.CurrentIconSize + 12 + Settings.Default.IconSpacing;
    const double StackPadding = 5.0;

    public LauncherMenuWindow(ObservableCollection<LauncherItem> items, Point position, LauncherConfigManager configManager)
    {
        InitializeComponent();
        DataContext = this;
        _configManager = configManager;
        _dragHistory = new DragHistoryManager(ApplyItemPosition);

        var initialItems = items ?? new ObservableCollection<LauncherItem>();
        foreach(var item in initialItems) item.IsSelected = false;
        LauncherItemsOnCanvas = initialItems;

        UpdateNoItemsMessage();
        CurrentIconSize = Settings.Default.IconSize;

        if(Settings.Default.SavedLayouts == null)
        {
            Settings.Default.SavedLayouts = new StringCollection();
        }


        double effectiveInitialWidth = this.Width;
        double effectiveInitialHeight = this.Height;

        try
        {
            if(Settings.Default.LauncherMenuWidth > 0)
            {
                effectiveInitialWidth = Settings.Default.LauncherMenuWidth;
            }
            if(Settings.Default.LauncherMenuHeight > 0)
            {
                effectiveInitialHeight = Settings.Default.LauncherMenuHeight;
            }
        }
        catch(System.Configuration.SettingsPropertyNotFoundException ex)
        {
            Debug.WriteLine($"SETTINGS PROPERTY 'LauncherMenuWidth' or 'LauncherMenuHeight' NOT FOUND in constructor: {ex.Message}. Using XAML-defined defaults.");
        }

        this.Width = effectiveInitialWidth;
        this.Height = effectiveInitialHeight;

        Point cursorPosition = GetCursorPosition();

        this.Left = cursorPosition.X - this.Width;
        this.Top = cursorPosition.Y;


        EnsureWindowIsOnScreen();
    }

    async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _iconCanvasInstance = FindVisualChild<Canvas>(LauncherItemsHostControl);
        if(_iconCanvasInstance == null) Debug.WriteLine("WARNING: IconCanvas instance not found on Loaded!");

        ApplyIconSize();

        EnsureWindowIsOnScreen();

        if(!this.IsVisible)
            return;

        bool activated = this.Activate();

        IInputElement elementToFocus;
        if(MenuBorder.Focusable)
            elementToFocus = MenuBorder;
        else if(this.Focusable)
            elementToFocus = this;
        else
        {
            elementToFocus = this;
            Debug.WriteLine($"Window_Loaded: MenuBorder is not explicitly focusable. Targeting window for focus.");
        }


        elementToFocus.Focus();
        Keyboard.Focus(elementToFocus);
    }

    void DeselectAllItems(LauncherItem exceptThis = null)
    {
        foreach(var item in _selectedLauncherItems.ToList())
        {
            if(item != exceptThis)
            {
                item.IsSelected = false;
            }
        }
        _selectedLauncherItems.Clear();
        if(exceptThis != null && exceptThis.IsSelected)
        {
            _selectedLauncherItems.Add(exceptThis);
        }
    }

    void ToggleItemSelected(LauncherItem item)
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

    void Icon_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        Debug.WriteLine("Icon_PreviewMouseLeftButtonDown");
        if(!(sender is FrameworkElement fe && fe.DataContext is LauncherItem clickedItem))
            return;

        bool ctrlPressed = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);

        bool isDoubleClick = false;
        DateTime currentClickTime = DateTime.Now;
        if(_lastMouseDownItemVisual == fe &&
            (currentClickTime - _lastMouseDownTimeOnIcon).TotalMilliseconds < DoubleClickThresholdMs)
        {
            isDoubleClick = true;
            _lastMouseDownTimeOnIcon = DateTime.MinValue;
            _lastMouseDownItemVisual = null;
        }
        else
        {
            _lastMouseDownTimeOnIcon = currentClickTime;
            _lastMouseDownItemVisual = fe;
        }

        if(isDoubleClick)
        {
            Debug.WriteLine($"Double click (manual) on: {clickedItem.DisplayName}");
            LaunchItem(clickedItem);
            this.Close();
            e.Handled = true;
            return;
        }

        _leftMouseDownOnIcon = true;

        if(ctrlPressed)
        {
            ToggleItemSelected(clickedItem);
        }
        else
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

    void Icon_MouseMove(object sender, MouseEventArgs e)
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
                    originalPosOfThisItem = new Point(selectedItem.X - actualDeltaX, selectedItem.Y - actualDeltaY);
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

    void Icon_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
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
    }

    void MenuBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
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
            try { this.DragMove(); } catch(InvalidOperationException) { }
        }
    }

    Point GetCursorPosition()
    {
        var pos = System.Windows.Forms.Control.MousePosition;
        return new Point(pos.X, pos.Y);
    }

    void UpdateNoItemsMessage() => ShowNoItemsMessage = !LauncherItemsOnCanvas.Any() || LauncherItemsOnCanvas.All(it => it.ExecutablePath == "NO_ACTION");
    void ApplyItemPosition(LauncherItem item, double x, double y) { if(item != null) { item.X = x; item.Y = y; } }
    LauncherItem FindItemById(Guid id) => LauncherItemsOnCanvas.FirstOrDefault(item => item.Id == id);

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

    void EnsureWindowIsOnScreen()
    {
        double sW = SystemParameters.VirtualScreenWidth, sH = SystemParameters.VirtualScreenHeight;
        if(this.Left + this.Width > sW) this.Left = sW - this.Width;
        if(this.Top + this.Height > sH) this.Top = sH - this.Height;
        if(this.Left < 0) this.Left = 0;
        if(this.Top < 0) this.Top = 0;
    }

    void Window_Deactivated(object sender, EventArgs e)
    {
        if(!_isCurrentlyDragging && !_isOpeningSettings && !_isShowingInputDialog && !IsAnyContextMenuOpen())
        {
            try { this.Close(); }
            catch(Exception ex) { Debug.WriteLine($"Err closing on deactivate: {ex.Message}"); }
        }
    }

    bool IsAnyContextMenuOpen()
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

    void Window_Closing(object sender, CancelEventArgs e)
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

    void SaveCurrentLayoutAsDefault()
    {
        if(LauncherItemsOnCanvas != null && _configManager != null)
        {
            Debug.WriteLine("SaveCurrentLayoutAsDefault (LauncherItemsConfig)");
            _configManager.SaveLauncherItems(new System.Collections.Generic.List<LauncherItem>(LauncherItemsOnCanvas));
        }
    }

    void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if(e.Key == Key.Escape) { this.Close(); return; }
        bool ctrl = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);
        if(ctrl && e.Key == Key.Z) { _dragHistory.Undo(FindItemById); e.Handled = true; }
        else if(ctrl && e.Key == Key.Y) { _dragHistory.Redo(FindItemById); e.Handled = true; }
        else if(ctrl && e.Key == Key.A)
        {
            foreach(var item in LauncherItemsOnCanvas)
            {
                if(!item.IsSelected) ToggleItemSelected(item);
            }
            e.Handled = true;
        }
    }

    void LaunchItem(LauncherItem item)
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

    void OptionsButton_Click(object sender, RoutedEventArgs e) => OpenSettingsWindow();

    void OpenSettingsWindow()
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

    void ResizeDragDelta(object sender, DragDeltaEventArgs e)
    {
        double nW = Width + e.HorizontalChange, nH = Height + e.VerticalChange;
        if(nW >= MinWidth) Width = nW;
        if(nH >= MinHeight) Height = nH;
    }

    void IconBorder_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        Debug.WriteLine("IconBorder_ContextMenuOpening");
        if(sender is FrameworkElement fe && fe.DataContext is LauncherItem item)
        {
            if(!item.IsSelected)
            {
                DeselectAllItems();
                ToggleItemSelected(item);
            }

            if(fe.ContextMenu != null)
            {
                fe.ContextMenu.DataContext = item;
                bool multipleSelected = _selectedLauncherItems.Count > 1;
                foreach(var menuItemBase in fe.ContextMenu.Items.OfType<MenuItem>())
                {
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

    void IconBorder_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        Debug.WriteLine("IconBorder_PreviewMouseRightButtonUp");
        if(_isCurrentlyDragging)
        {
            Debug.WriteLine("Ctx skipped: dragging.");
            e.Handled = true;
            return;
        }
    }

    LauncherItem GetLauncherItemFromContextMenu(object sender)
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

    void IconContextMenu_Launch_Click(object sender, RoutedEventArgs e)
    {
        Debug.WriteLine("CtxMenuLaunch");
        foreach(var itemToLaunch in _selectedLauncherItems.ToList())
        {
            LaunchItem(itemToLaunch);
        }
        if(_selectedLauncherItems.Any()) Close();
        else Debug.WriteLine("LaunchClick: No items were selected for launch via context menu.");
    }

    void IconContextMenu_OpenFileLocation_Click(object sender, RoutedEventArgs e)
    {
        Debug.WriteLine("CtxMenuOpenLocation");
        var item = GetLauncherItemFromContextMenu(sender);
        if(item != null && !string.IsNullOrWhiteSpace(item.ExecutablePath) && _selectedLauncherItems.Count <= 1)
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

    void IconContextMenu_EditSettings_Click(object sender, RoutedEventArgs e)
    {
        Debug.WriteLine("CtxMenuEditSettings");
        var i = GetLauncherItemFromContextMenu(sender);
        if(i != null && _selectedLauncherItems.Count <= 1)
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
                    ed.Item.IsSelected = true;
                    _selectedLauncherItems.Remove(i);
                    _selectedLauncherItems.Add(ed.Item);
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

    void IconContextMenu_FileProperties_Click(object sender, RoutedEventArgs e)
    {
        Debug.WriteLine("CtxMenuFileProps");
        var i = GetLauncherItemFromContextMenu(sender);
        if(i != null && !string.IsNullOrWhiteSpace(i.ExecutablePath) && _selectedLauncherItems.Count <= 1)
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
        else Debug.WriteLine("FilePropsClick: Null item/path or multiple items selected.");
    }

    void IconContextMenu_Remove_Click(object sender, RoutedEventArgs e)
    {
        Debug.WriteLine("CtxMenuRemove");
        _isShowingInputDialog = true;

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
            foreach(var itemToRemove in _selectedLauncherItems.ToList())
            {
                LauncherItemsOnCanvas.Remove(itemToRemove);
                Debug.WriteLine($"Removed: {itemToRemove.DisplayName}");
            }
            _selectedLauncherItems.Clear();
            UpdateNoItemsMessage();
            _dragHistory.ClearHistory();
            SaveCurrentLayoutAsDefault();
            if(LauncherItemsHostControl != null) LauncherItemsHostControl.Items.Refresh();
        }
        _isShowingInputDialog = false;
        this.Activate();
        this.Focus();
    }

    void SaveLayoutAs_Click(object sender, RoutedEventArgs e)
    {
        DeselectAllItems();
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

    void ReloadItemsFromConfig(bool loadDefaultLayout = true, NamedLayout layoutToLoad = null)
    {
        DeselectAllItems();
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

                    if(layoutToLoad.WindowWidth > 0 && layoutToLoad.WindowHeight > 0)
                    {
                        this.Width = layoutToLoad.WindowWidth;
                        this.Height = layoutToLoad.WindowHeight;
                    }
                    if(layoutToLoad.IconSize > 0) Settings.Default.IconSize = layoutToLoad.IconSize;
                    if(layoutToLoad.IconSpacing >= 0) Settings.Default.IconSpacing = layoutToLoad.IconSpacing;

                    Settings.Default.Save();
                    ApplyIconSize();
                    Debug.WriteLine($"Applied icon size: {Settings.Default.IconSize}, spacing: {Settings.Default.IconSpacing}");
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
            ApplyIconSize();
        }

        if(itemsToLoad != null)
        {
            foreach(var item in itemsToLoad) item.IsSelected = false;
        }

        LauncherItemsOnCanvas = new ObservableCollection<LauncherItem>(itemsToLoad ?? new System.Collections.Generic.List<LauncherItem>());
        UpdateNoItemsMessage();
        _dragHistory.ClearHistory();
    }

    System.Collections.Generic.List<NamedLayout> GetSavedNamedLayouts()
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

    void ApplyIconSize()
    {
        CurrentIconSize = Settings.Default.IconSize;
        if(LauncherItemsOnCanvas != null)
        {
            foreach(var item in LauncherItemsOnCanvas) item.OnPropertyChanged("IconPath");
        }
        this.UpdateLayout();
        this.InvalidateVisual();
    }

    void SettingsWindow_Closed(object sender, EventArgs e)
    {
        _isOpeningSettings = false;
        ApplyIconSize();
        ReloadItemsFromConfig(true);
        if(sender is SettingsWindow sw) sw.Closed -= SettingsWindow_Closed;
        this.Show();
        this.Activate();
        this.Focus();
    }

    void PersistAllNamedLayouts(System.Collections.Generic.List<NamedLayout> layoutsToSave)
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

    void Background_ContextMenuOpening(object sender, ContextMenuEventArgs e) => PopulateLoadLayoutMenuItems();

    void PopulateLoadLayoutMenuItems()
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
    void LoadSpecificLayout_Click(object sender, RoutedEventArgs e)
    {
        if(sender is MenuItem menuItem && menuItem.Tag is NamedLayout layoutToLoad)
        {
            Debug.WriteLine($"Loading layout: {layoutToLoad.Name}");
            ReloadItemsFromConfig(false, layoutToLoad);
        }
    }

    void ManageLayouts_Click(object sender, RoutedEventArgs e)
    {
        DeselectAllItems();
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

    void Organize_AlignToGrid_Click(object sender, RoutedEventArgs e)
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

    void Organize_StackVertically_Click(object sender, RoutedEventArgs e)
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

    void Organize_StackHorizontally_Click(object sender, RoutedEventArgs e)
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