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
            OnPropertyChanged(nameof(ItemVisualWidth));
            OnPropertyChanged(nameof(ItemVisualHeight));
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

    const double IconBorderThicknessAndPadding = 4.0;
    public double ItemVisualWidth => CurrentIconSize + IconBorderThicknessAndPadding;
    public double ItemVisualHeight => CurrentIconSize + IconBorderThicknessAndPadding;


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

            _originalItemPositionBeforeDrag = new Point(_draggedLauncherItemModel.X, _draggedLauncherItemModel.Y);
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

            double deltaX = currentMousePosOnCanvas.X - _mouseDragStartPoint_CanvasRelative.X;
            double deltaY = currentMousePosOnCanvas.Y - _mouseDragStartPoint_CanvasRelative.Y;

            double primaryTargetX = _originalItemPositionBeforeDrag.X + deltaX;
            double primaryTargetY = _originalItemPositionBeforeDrag.Y + deltaY;


            bool shiftPressed = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);

            if(shiftPressed)
            {
                double fullGridStep = this.GridCellSize;
                if(fullGridStep > 0)
                {
                    double snapStep = fullGridStep / 2.0;
                    if(snapStep > 0)
                    {
                        primaryTargetX = StackPadding + Math.Round((primaryTargetX - StackPadding) / snapStep) * snapStep;
                        primaryTargetY = StackPadding + Math.Round((primaryTargetY - StackPadding) / snapStep) * snapStep;
                    }
                }
            }

            double actualAppliedDeltaX = primaryTargetX - _originalItemPositionBeforeDrag.X;
            double actualAppliedDeltaY = primaryTargetY - _originalItemPositionBeforeDrag.Y;

            foreach(var selectedItem in _selectedLauncherItems)
            {
                if(!_dragStartPositionsSelectedItems.TryGetValue(selectedItem.Id, out Point originalPosOfThisItem))
                {
                    Debug.WriteLine($"ERROR: Could not find original position for {selectedItem.DisplayName} during drag.");
                    originalPosOfThisItem = new Point(selectedItem.X - actualAppliedDeltaX, selectedItem.Y - actualAppliedDeltaY);
                }

                double newX = originalPosOfThisItem.X + actualAppliedDeltaX;
                double newY = originalPosOfThisItem.Y + actualAppliedDeltaY;

                double currentItemVisualWidth = ItemVisualWidth;
                double currentItemVisualHeight = ItemVisualHeight;

                double canvasActualWidth = _iconCanvasInstance.ActualWidth;
                double canvasActualHeight = _iconCanvasInstance.ActualHeight;

                newX = Math.Max(0, Math.Min(newX, canvasActualWidth - currentItemVisualWidth));
                newY = Math.Max(0, Math.Min(newY, canvasActualHeight - currentItemVisualHeight));

                if(shiftPressed && StackPadding > 0)
                {
                    newX = Math.Max(StackPadding, newX);
                    newY = Math.Max(StackPadding, newY);
                    newX = Math.Min(newX, canvasActualWidth - currentItemVisualWidth - StackPadding);
                    newY = Math.Min(newY, canvasActualHeight - currentItemVisualHeight - StackPadding);
                }

                newX = Math.Max(0, Math.Min(newX, canvasActualWidth - currentItemVisualWidth));
                newY = Math.Max(0, Math.Min(newY, canvasActualHeight - currentItemVisualHeight));


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
                            _dragHistory.RecordDrag(item, originalPos.X, originalPos.Y, item.X, item.Y);
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

        if(LauncherItemsHostControl == null || LauncherItemsHostControl.Items.Count == 0) return false;

        foreach(var itemData in LauncherItemsHostControl.Items)
        {
            var container = LauncherItemsHostControl.ItemContainerGenerator.ContainerFromItem(itemData) as ContentPresenter;
            if(container != null)
            {
                container.ApplyTemplate();

                var contentControl = VisualTreeHelper.GetChild(container, 0) as ContentControl;
                if(contentControl != null)
                {
                    contentControl.ApplyTemplate();
                    var iconBorder = contentControl.Template.FindName("IconBorder", contentControl) as Border;
                    if(iconBorder?.ContextMenu?.IsOpen == true)
                    {
                        return true;
                    }
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
                    string exeDir = Path.GetDirectoryName(psi.FileName);
                    if(Directory.Exists(exeDir)) psi.WorkingDirectory = exeDir;
                }
            }
            else
            {
                string exeDir = Path.GetDirectoryName(psi.FileName);
                if(Directory.Exists(exeDir)) psi.WorkingDirectory = exeDir;
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
        if(_selectedLauncherItems.Any())
        {
            foreach(var itemToLaunch in _selectedLauncherItems.ToList())
            {
                LaunchItem(itemToLaunch);
            }
            Close();
        }
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
                string path = Environment.ExpandEnvironmentVariables(item.ExecutablePath);
                if(File.Exists(path)) Process.Start("explorer.exe", $"/select,\"{path}\"");
                else if(Directory.Exists(path)) Process.Start("explorer.exe", $"\"{path}\"");
                else
                {
                    string dir = Path.GetDirectoryName(path);
                    if(Directory.Exists(dir)) Process.Start("explorer.exe", $"\"{dir}\"");
                    else MessageBox.Show("Cannot find file or its directory.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch(Exception ex) { MessageBox.Show($"Error opening file location: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
        }
        else if(_selectedLauncherItems.Count > 1)
        {
            MessageBox.Show("This action is available for a single selected item only.", "Action Not Available", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else Debug.WriteLine("OpenLocationClick: Null item/path or conditions not met.");
    }


    void IconContextMenu_EditSettings_Click(object sender, RoutedEventArgs e)
    {
        Debug.WriteLine("CtxMenuEditSettings");
        var itemToEdit = GetLauncherItemFromContextMenu(sender);
        if(itemToEdit != null && _selectedLauncherItems.Count <= 1)
        {
            _isOpeningSettings = true;
            var editorWindow = new LauncherItemEditorWindow(itemToEdit) { Owner = this };
            if(editorWindow.ShowDialog() == true)
            {
                int index = LauncherItemsOnCanvas.IndexOf(itemToEdit);
                if(index != -1)
                {
                    LauncherItemsOnCanvas[index] = editorWindow.Item;

                    itemToEdit.IsSelected = false;
                    _selectedLauncherItems.Remove(itemToEdit);
                    editorWindow.Item.IsSelected = true;
                    _selectedLauncherItems.Add(editorWindow.Item);

                    SaveCurrentLayoutAsDefault();
                    Debug.WriteLine($"EditSettings updated: {editorWindow.Item.DisplayName}");
                    LauncherItemsHostControl.Items.Refresh();
                }
                else Debug.WriteLine($"EditSettings: Cannot find original item '{itemToEdit.DisplayName}' to update.");
            }
            _isOpeningSettings = false;
            this.Activate();
            this.Focus();
        }
        else if(_selectedLauncherItems.Count > 1)
        {
            MessageBox.Show("This action is available for a single selected item only.", "Action Not Available", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else Debug.WriteLine("EditSettingsClick: Null item or conditions not met.");
    }

    void IconContextMenu_FileProperties_Click(object sender, RoutedEventArgs e)
    {
        Debug.WriteLine("CtxMenuFileProps");
        var item = GetLauncherItemFromContextMenu(sender);
        if(item != null && !string.IsNullOrWhiteSpace(item.ExecutablePath) && _selectedLauncherItems.Count <= 1)
        {
            string filePath = Environment.ExpandEnvironmentVariables(item.ExecutablePath);
            if(File.Exists(filePath) || Directory.Exists(filePath))
            {
                try
                {
                    SHELLEXECUTEINFO sei = new SHELLEXECUTEINFO();
                    sei.cbSize = Marshal.SizeOf(typeof(SHELLEXECUTEINFO));
                    sei.fMask = SEE_MASK_INVOKEIDLIST;
                    sei.hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                    sei.lpVerb = "properties";
                    sei.lpFile = filePath;
                    sei.nShow = SW_SHOWNORMAL;
                    if(!ShellExecuteEx(ref sei))
                    {
                        int errorCode = Marshal.GetLastWin32Error();
                        MessageBox.Show($"Could not show file properties. Error code: {errorCode}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        Debug.WriteLine($"ShellExecuteEx failed with error {errorCode} for path {filePath}");
                    }
                    else Debug.WriteLine($"Showing properties for {filePath}");
                }
                catch(Exception ex)
                {
                    MessageBox.Show($"Error showing file properties: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    Debug.WriteLine($"Exception showing file properties: {ex}");
                }
            }
            else MessageBox.Show($"File or directory not found: {filePath}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        else if(_selectedLauncherItems.Count > 1)
        {
            MessageBox.Show("This action is available for a single selected item only.", "Action Not Available", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else Debug.WriteLine("FilePropsClick: Null item/path or conditions not met.");
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
            ? $"Are you sure you want to remove '{_selectedLauncherItems.First().DisplayName}'?"
            : $"Are you sure you want to remove these {_selectedLauncherItems.Count} selected items?";

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
            MessageBox.Show($"Layout '{layoutName}' saved with current window and icon settings.", "Layout Saved", MessageBoxButton.OK, MessageBoxImage.Information);
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

                    if(layoutToLoad.WindowWidth >= this.MinWidth && layoutToLoad.WindowHeight >= this.MinHeight)
                    {
                        this.Width = layoutToLoad.WindowWidth;
                        this.Height = layoutToLoad.WindowHeight;
                    }
                    if(layoutToLoad.IconSize >= 8) Settings.Default.IconSize = layoutToLoad.IconSize;
                    if(layoutToLoad.IconSpacing >= 0) Settings.Default.IconSpacing = layoutToLoad.IconSpacing;

                    Settings.Default.Save();
                    ApplyIconSize();
                    Debug.WriteLine($"Applied layout settings - IconSize: {Settings.Default.IconSize}, Spacing: {Settings.Default.IconSpacing}");
                }
            }
            catch(Exception ex)
            {
                MessageBox.Show($"Error deserializing layout '{layoutToLoad.Name}': {ex.Message}\nLoading default items instead.", "Layout Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
                    if(namedLayout.IconSize <= 0) namedLayout.IconSize = 20;
                    if(namedLayout.IconSpacing < 0) namedLayout.IconSpacing = 10;
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
            var saveResult = MessageBox.Show("You made changes to layouts. Save them?", "Save Changes", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if(saveResult == MessageBoxResult.Yes) PersistAllNamedLayouts(manageWindow.GetUpdatedLayouts());
        }
        _isShowingInputDialog = false;
        this.Activate();
        this.Focus();
    }

    void Organize_AlignToGrid_Click(object sender, RoutedEventArgs e)
    {
        DeselectAllItems();
        Debug.WriteLine("Organize_AlignToGrid_Click");
        if(_iconCanvasInstance == null || !LauncherItemsOnCanvas.Any()) return;

        double cellSize = GridCellSize;
        if(cellSize <= 0) return;

        double currentX = StackPadding;
        double currentY = StackPadding;
        double maxRowWidth = _iconCanvasInstance.ActualWidth > 0 ? _iconCanvasInstance.ActualWidth - StackPadding : this.Width - 20 - StackPadding;

        foreach(var item in LauncherItemsOnCanvas.OrderBy(i => i.Y).ThenBy(i => i.X))
        {
            item.X = currentX;
            item.Y = currentY;
            currentX += cellSize;
            if(currentX + ItemVisualWidth > maxRowWidth && maxRowWidth > ItemVisualWidth)
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

        double cellHeight = GridCellSize;
        if(cellHeight <= 0) return;
        double currentY = StackPadding;
        double xPos = StackPadding;

        foreach(var item in LauncherItemsOnCanvas.OrderBy(i => i.DisplayName))
        {
            item.X = xPos;
            item.Y = currentY;
            currentY += cellHeight;
        }
        _dragHistory.ClearHistory();
        SaveCurrentLayoutAsDefault();
    }

    void Organize_StackHorizontally_Click(object sender, RoutedEventArgs e)
    {
        DeselectAllItems();
        Debug.WriteLine("Organize_StackHorizontally_Click");
        if(!LauncherItemsOnCanvas.Any()) return;

        double cellWidth = GridCellSize;
        if(cellWidth <= 0) return;
        double currentX = StackPadding;
        double yPos = StackPadding;

        foreach(var item in LauncherItemsOnCanvas.OrderBy(i => i.DisplayName))
        {
            item.X = currentX;
            item.Y = yPos;
            currentX += cellWidth;
        }
        _dragHistory.ClearHistory();
        SaveCurrentLayoutAsDefault();
    }

    void Organize_SnapToNearestGrid_Click(object sender, RoutedEventArgs e)
    {
        DeselectAllItems();
        Debug.WriteLine("Organize_SnapToNearestGrid_Click");

        if(_iconCanvasInstance == null || !LauncherItemsOnCanvas.Any()) return;

        double fullGridStep = GridCellSize;
        if(fullGridStep <= 0)
        {
            Debug.WriteLine("GridCellSize is not positive, cannot snap.");
            return;
        }

        double snapStep = fullGridStep / 2.0;
        if(snapStep <= 0)
        {
            Debug.WriteLine("Snap step is not positive, cannot snap.");
            return;
        }

        double padding = StackPadding;
        double itemVisualW = ItemVisualWidth;
        double itemVisualH = ItemVisualHeight;

        double canvasWidth = _iconCanvasInstance.ActualWidth > 0 ? _iconCanvasInstance.ActualWidth : (this.ActualWidth - MenuBorder.BorderThickness.Left - MenuBorder.BorderThickness.Right);
        double canvasHeight = _iconCanvasInstance.ActualHeight > 0 ? _iconCanvasInstance.ActualHeight : (this.ActualHeight - MenuBorder.BorderThickness.Top - MenuBorder.BorderThickness.Bottom);

        if(canvasWidth <= 0 || canvasHeight <= 0)
        {
            Debug.WriteLine("Canvas dimensions are not positive, cannot reliably snap.");
            return;
        }


        foreach(var item in LauncherItemsOnCanvas)
        {
            double currentX = item.X;
            double currentY = item.Y;

            double snappedX = padding + Math.Round((currentX - padding) / snapStep) * snapStep;
            double snappedY = padding + Math.Round((currentY - padding) / snapStep) * snapStep;

            snappedX = Math.Max(padding, snappedX);
            snappedY = Math.Max(padding, snappedY);

            snappedX = Math.Min(snappedX, canvasWidth - itemVisualW - padding);
            snappedY = Math.Min(snappedY, canvasHeight - itemVisualH - padding);

            if(canvasWidth - itemVisualW - padding < padding)
                snappedX = padding;
            if(canvasHeight - itemVisualH - padding < padding)
                snappedY = padding;


            item.X = snappedX;
            item.Y = snappedY;
        }

        _dragHistory.ClearHistory();
        SaveCurrentLayoutAsDefault();
    }
}