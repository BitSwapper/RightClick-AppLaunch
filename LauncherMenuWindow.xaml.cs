using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
// File: UI/LauncherMenuWindow.xaml.cs
using System.Runtime.InteropServices; // For ShellExecuteEx
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using RightClickAppLauncher.Managers;
using RightClickAppLauncher.Models;
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

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct SHELLEXECUTEINFO
        { /* ... same as before ... */
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

        public LauncherMenuWindow(ObservableCollection<LauncherItem> items, Point position, LauncherConfigManager configManager)
        {
            InitializeComponent();
            DataContext = this;
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

        private void UpdateNoItemsMessage() => ShowNoItemsMessage = !LauncherItemsOnCanvas.Any() || LauncherItemsOnCanvas.All(it => it.ExecutablePath == "NO_ACTION");
        private void ApplyItemPosition(LauncherItem item, double x, double y) { if(item != null) { item.X = x; item.Y = y; } }
        private LauncherItem FindItemById(Guid id) => LauncherItemsOnCanvas.FirstOrDefault(item => item.Id == id);

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _iconCanvasInstance = FindVisualChild<Canvas>(LauncherItemsHostControl);
            if(_iconCanvasInstance == null) Debug.WriteLine("WARNING: IconCanvas instance not found!");
            this.Focus(); this.Activate();
            if(ShowNoItemsMessage) MenuBorder.Focus();
        }

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
            if(this.Left < 0) this.Left = 0; if(this.Top < 0) this.Top = 0;
        }

        private void Window_Deactivated(object sender, EventArgs e)
        {
            if(!_isCurrentlyDragging && !_isOpeningSettings) { try { this.Close(); } catch(Exception ex) { Debug.WriteLine($"Err closing on deactivate: {ex.Message}"); } }
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            // Save current visual state IF NOT ALREADY SAVED by OpenSettingsWindow
            // This primarily catches the case where the window is closed directly (e.g., Escape key)
            // without going through the settings workflow.
            // If OpenSettingsWindow was called, it already saved. Re-saving here is fine, it'll just be the same data.
            SaveAllLauncherItemPositions();

            Properties.Settings.Default.LauncherMenuX = this.Left; Properties.Settings.Default.LauncherMenuY = this.Top;
            Properties.Settings.Default.LauncherMenuWidth = this.ActualWidth; Properties.Settings.Default.LauncherMenuHeight = this.ActualHeight;
            Properties.Settings.Default.Save();
        }
        private void SaveAllLauncherItemPositions()
        {
            if(LauncherItemsOnCanvas != null && _configManager != null)
            {
                Debug.WriteLine("SaveAllLauncherItemPositions called.");
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
            if(item == null || string.IsNullOrWhiteSpace(item.ExecutablePath) || item.ExecutablePath == "NO_ACTION") { if(item?.ExecutablePath != "NO_ACTION") MessageBox.Show("Path not configured.", "Error"); return; }
            try { var psi = new ProcessStartInfo { FileName = Environment.ExpandEnvironmentVariables(item.ExecutablePath), Arguments = Environment.ExpandEnvironmentVariables(item.Arguments ?? ""), UseShellExecute = true }; if(!string.IsNullOrWhiteSpace(item.WorkingDirectory)) { string wd = Environment.ExpandEnvironmentVariables(item.WorkingDirectory); if(Directory.Exists(wd)) psi.WorkingDirectory = wd; else { string ed = Path.GetDirectoryName(psi.FileName); if(Directory.Exists(ed)) psi.WorkingDirectory = ed; } } else { string ed = Path.GetDirectoryName(psi.FileName); if(Directory.Exists(ed)) psi.WorkingDirectory = ed; } Process.Start(psi); Debug.WriteLine($"Started: {item.DisplayName}"); }
            catch(Exception ex) { MessageBox.Show($"Launch failed for '{item.DisplayName}': {ex.Message}", "Error"); Debug.WriteLine($"Launch Err: {ex}"); }
        }

        private void Icon_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            Debug.WriteLine("Icon_PreviewMouseLeftButtonDown");
            if(sender is FrameworkElement fe && fe.DataContext is LauncherItem launcherItem)
            {
                _draggedItemVisual = fe;
                _draggedLauncherItemModel = launcherItem;
                if(_iconCanvasInstance == null) { _iconCanvasInstance = FindVisualChild<Canvas>(LauncherItemsHostControl); if(_iconCanvasInstance == null) { Debug.WriteLine("CRITICAL: IconCanvas not found!"); return; } }
                _mouseDragStartPoint_CanvasRelative = e.GetPosition(_iconCanvasInstance);
                _originalItemPositionBeforeDrag = new Point(_draggedLauncherItemModel.X, _draggedLauncherItemModel.Y);
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
                    Point currentPositionOnCanvas = e.GetPosition(_iconCanvasInstance);
                    if(Math.Abs(currentPositionOnCanvas.X - _mouseDragStartPoint_CanvasRelative.X) > SystemParameters.MinimumHorizontalDragDistance ||
                        Math.Abs(currentPositionOnCanvas.Y - _mouseDragStartPoint_CanvasRelative.Y) > SystemParameters.MinimumVerticalDragDistance)
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
                    Point currentMousePositionOnCanvas = e.GetPosition(_iconCanvasInstance);
                    double offsetX = currentMousePositionOnCanvas.X - _mouseDragStartPoint_CanvasRelative.X;
                    double offsetY = currentMousePositionOnCanvas.Y - _mouseDragStartPoint_CanvasRelative.Y;
                    double newX = _originalItemPositionBeforeDrag.X + offsetX;
                    double newY = _originalItemPositionBeforeDrag.Y + offsetY;
                    double itemWidth = _draggedItemVisual.ActualWidth;
                    double itemHeight = _draggedItemVisual.ActualHeight;
                    if(double.IsNaN(itemWidth) || itemWidth <= 0) itemWidth = 30;
                    if(double.IsNaN(itemHeight) || itemHeight <= 0) itemHeight = 30;
                    newX = Math.Max(0, Math.Min(newX, _iconCanvasInstance.ActualWidth - itemWidth));
                    newY = Math.Max(0, Math.Min(newY, _iconCanvasInstance.ActualHeight - itemHeight));
                    _draggedLauncherItemModel.X = newX;
                    _draggedLauncherItemModel.Y = newY;
                }
            }
        }

        private void Icon_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            Debug.WriteLine($"IconUp. Dragging: {_isCurrentlyDragging}, MouseDownOnIcon: {_leftMouseDownOnIcon}");
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
                Debug.WriteLine($"Single click launch: {itemModelForClick.DisplayName}");
                LaunchItem(itemModelForClick);
                this.Close();
            }
        }

        private void OptionsButton_Click(object sender, RoutedEventArgs e) => OpenSettingsWindow();

        private void OpenSettingsWindow()
        {
            _isOpeningSettings = true;

            // ***** SAVE CURRENT POSITIONS BEFORE OPENING SETTINGS *****
            SaveAllLauncherItemPositions();
            Debug.WriteLine("Saved icon positions before opening settings.");

            var settingsWindow = new SettingsWindow { Owner = this };
            settingsWindow.Closed += SettingsWindow_Closed;
            this.Hide();
            settingsWindow.ShowDialog();
        }

        private void SettingsWindow_Closed(object sender, EventArgs e)
        {
            _isOpeningSettings = false;
            ReloadItemsFromConfig(); // This will load from the file (either original or settings-saved)
            if(sender is SettingsWindow sw) sw.Closed -= SettingsWindow_Closed;
            this.Show(); this.Activate(); this.Focus();
        }

        private void ReloadItemsFromConfig()
        {
            Debug.WriteLine("ReloadItemsFromConfig called.");
            var updatedItems = new ObservableCollection<LauncherItem>(_configManager.LoadLauncherItems());
            LauncherItemsOnCanvas = updatedItems;
            UpdateNoItemsMessage();
            _dragHistory.ClearHistory();
        }

        private void MenuBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if(e.Handled) return; // If icon click handled it, don't drag window

            Debug.WriteLine("MenuBorder_MouseLeftButtonDown");
            if(e.ButtonState == MouseButtonState.Pressed)
            {
                try { this.DragMove(); } catch(InvalidOperationException) { /* Can happen */ }
            }
        }
        private void ResizeDragDelta(object sender, DragDeltaEventArgs e) { double nW = Width + e.HorizontalChange, nH = Height + e.VerticalChange; if(nW >= MinWidth) Width = nW; if(nH >= MinHeight) Height = nH; }

        private void IconBorder_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            Debug.WriteLine("IconBorder_ContextMenuOpening");
            if(sender is FrameworkElement fe && fe.DataContext is LauncherItem item)
            {
                if(fe.ContextMenu != null) { fe.ContextMenu.DataContext = item; Debug.WriteLine($"CtxMenu DC set: {item.DisplayName}"); }
                else { Debug.WriteLine("Ctx on IconBorder is null!"); e.Handled = true; }
            }
            else { Debug.WriteLine("Sender not FE or DC not LI in CtxMenuOpening."); e.Handled = true; }
        }

        private void IconBorder_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            Debug.WriteLine("IconBorder_PreviewMouseRightButtonUp");
            if(e.LeftButton == MouseButtonState.Pressed && _leftMouseDownOnIcon) { Debug.WriteLine("Ctx skipped: LBtn down."); return; }
            if(_isCurrentlyDragging) { Debug.WriteLine("Ctx skipped: dragging."); return; }
        }

        private LauncherItem GetLauncherItemFromContextMenu(object sender)
        {
            Debug.WriteLine($"GetLIFromCtxMenu by: {sender?.GetType().FullName}");
            if(sender is MenuItem mi)
            {
                if(mi.DataContext is LauncherItem itemDC) { Debug.WriteLine($"Found LI '{itemDC.DisplayName}' from MI.DC."); return itemDC; }
                Debug.WriteLine($"MI.DC not LI: {mi.DataContext?.GetType().FullName}. Trying Parent CtxMenu.");
                if(mi.Parent is ContextMenu pcm && pcm.DataContext is LauncherItem itemPCM) { Debug.WriteLine($"Found LI '{itemPCM.DisplayName}' from PCM.DC."); return itemPCM; }
                Debug.WriteLine($"PCM.DC also not LI: {(mi.Parent as ContextMenu)?.DataContext?.GetType().FullName}");
            }
            Debug.WriteLine("Could not get LI from CtxMenu sender."); return null;
        }

        private void IconContextMenu_Launch_Click(object sender, RoutedEventArgs e) { Debug.WriteLine("CtxMenuLaunch"); var i = GetLauncherItemFromContextMenu(sender); if(i != null) { LaunchItem(i); Close(); } else Debug.WriteLine("LaunchClick: Null item"); }
        private void IconContextMenu_OpenFileLocation_Click(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("CtxMenuOpenLocation"); var i = GetLauncherItemFromContextMenu(sender);
            if(i != null && !string.IsNullOrWhiteSpace(i.ExecutablePath)) { try { string p = Environment.ExpandEnvironmentVariables(i.ExecutablePath); if(File.Exists(p)) Process.Start("explorer.exe", $"/select,\"{p}\""); else if(Directory.Exists(p)) Process.Start("explorer.exe", $"\"{p}\""); else { string d = Path.GetDirectoryName(p); if(Directory.Exists(d)) Process.Start("explorer.exe", $"\"{d}\""); else MessageBox.Show("Cannot find location.", "Error"); } } catch(Exception ex) { MessageBox.Show($"Err: {ex.Message}", "Error"); } } else Debug.WriteLine("OpenLocationClick: Null item/path");
        }
        private void IconContextMenu_EditSettings_Click(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("CtxMenuEditSettings"); var i = GetLauncherItemFromContextMenu(sender);
            if(i != null) { _isOpeningSettings = true; var ed = new LauncherItemEditorWindow(i) { Owner = this }; if(ed.ShowDialog() == true) { var oI = LauncherItemsOnCanvas.FirstOrDefault(x => x.Id == i.Id); int idx = oI != null ? LauncherItemsOnCanvas.IndexOf(oI) : -1; if(idx != -1) { LauncherItemsOnCanvas[idx] = ed.Item; SaveAllLauncherItemPositions(); Debug.WriteLine($"EditSettings updated: {ed.Item.DisplayName}"); } else Debug.WriteLine($"EditSettings: Cannot find original {i.DisplayName}"); } _isOpeningSettings = false; Focus(); } else Debug.WriteLine("EditSettingsClick: Null item");
        }
        private void IconContextMenu_FileProperties_Click(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("CtxMenuFileProps"); var i = GetLauncherItemFromContextMenu(sender);
            if(i != null && !string.IsNullOrWhiteSpace(i.ExecutablePath)) { string fp = Environment.ExpandEnvironmentVariables(i.ExecutablePath); if(File.Exists(fp) || Directory.Exists(fp)) { try { SHELLEXECUTEINFO sei = new SHELLEXECUTEINFO { cbSize = Marshal.SizeOf(typeof(SHELLEXECUTEINFO)), fMask = SEE_MASK_INVOKEIDLIST, hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle, lpVerb = "properties", lpFile = fp, nShow = SW_SHOWNORMAL }; if(!ShellExecuteEx(ref sei)) { int err = Marshal.GetLastWin32Error(); MessageBox.Show($"Cannot show file props. Err: {err}", "Error"); Debug.WriteLine($"ShellEx Err: {err} for {fp}"); } else Debug.WriteLine($"Showing props for {fp}"); } catch(Exception ex) { MessageBox.Show($"Err showing file props: {ex.Message}", "Error"); Debug.WriteLine($"Ex showing props: {ex}"); } } else MessageBox.Show($"Not found: {fp}", "Error"); } else Debug.WriteLine("FilePropsClick: Null item/path");
        }
        private void IconContextMenu_Remove_Click(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("CtxMenuRemove"); var i = GetLauncherItemFromContextMenu(sender);
            if(i != null) { if(MessageBox.Show($"Remove '{i.DisplayName}'?", "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes) { LauncherItemsOnCanvas.Remove(i); UpdateNoItemsMessage(); _dragHistory.ClearHistory(); SaveAllLauncherItemPositions(); Debug.WriteLine($"Removed: {i.DisplayName}"); } } else Debug.WriteLine("RemoveClick: Null item");
        }
        private void BackgroundContextMenu_AddItem_Click(object sender, RoutedEventArgs e) => OpenSettingsWindow();
    }
}