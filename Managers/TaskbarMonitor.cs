using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using RightClickAppLauncher.Models;
using RightClickAppLauncher.Native;
using RightClickAppLauncher.Properties;
using RightClickAppLauncher.UI;
using Application = System.Windows.Application;

namespace RightClickAppLauncher.Managers;

public class TaskbarMonitor : IDisposable
{
    readonly WindowsHooks _windowsHooks;
    readonly LauncherConfigManager _configManager;
    LauncherMenuWindow _currentLauncherMenu;

    bool _reqCtrl, _reqAlt, _reqShift, _reqWin;
    bool _isDisposed = false;
    long _isProcessingClick = 0;

    public TaskbarMonitor(LauncherConfigManager configManager)
    {
        _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
        _windowsHooks = new WindowsHooks();
        _windowsHooks.RightMouseClick += OnRightMouseClick;
        LoadHotkeySettings();
    }

    public void StartMonitoring()
    {
        if(_isDisposed) throw new ObjectDisposedException(nameof(TaskbarMonitor));
        _windowsHooks.InstallMouseHook();
        Debug.WriteLine("TaskbarMonitor started monitoring.");
    }

    public void StopMonitoring()
    {
        if(_isDisposed) return;
        _windowsHooks.UninstallMouseHook();
        CloseCurrentLauncherMenu();
        Debug.WriteLine("TaskbarMonitor stopped monitoring.");
    }

    public void ReloadHotkeySettings() => LoadHotkeySettings();

    void LoadHotkeySettings()
    {
        _reqCtrl = Settings.Default.Hotkey_Ctrl;
        _reqAlt = Settings.Default.Hotkey_Alt;
        _reqShift = Settings.Default.Hotkey_Shift;
        _reqWin = Settings.Default.Hotkey_Win;
    }

    void OnRightMouseClick(object sender, MouseHookEventArgs e)
    {
        if(_isDisposed) return;
        if(!CheckHotkeyModifiers()) return;
        if(Interlocked.CompareExchange(ref _isProcessingClick, 1, 0) != 0) return;

        try
        {
            System.Windows.Point clickPoint = new System.Windows.Point(e.X, e.Y);
            Application.Current.Dispatcher.InvokeAsync(() => ShowLauncherMenu(clickPoint));
        }
        finally
        {
            Interlocked.Exchange(ref _isProcessingClick, 0);
        }
    }

    bool CheckHotkeyModifiers()
    {
        bool ctrlPressed = (Keyboard.GetKeyStates(Key.LeftCtrl) & KeyStates.Down) > 0 || (Keyboard.GetKeyStates(Key.RightCtrl) & KeyStates.Down) > 0;
        bool altPressed = (Keyboard.GetKeyStates(Key.LeftAlt) & KeyStates.Down) > 0 || (Keyboard.GetKeyStates(Key.RightAlt) & KeyStates.Down) > 0;
        bool shiftPressed = (Keyboard.GetKeyStates(Key.LeftShift) & KeyStates.Down) > 0 || (Keyboard.GetKeyStates(Key.RightShift) & KeyStates.Down) > 0;
        bool winPressed = (Keyboard.GetKeyStates(Key.LWin) & KeyStates.Down) > 0 || (Keyboard.GetKeyStates(Key.RWin) & KeyStates.Down) > 0;

        bool hotkeyMatch = (ctrlPressed == _reqCtrl) &&
                           (altPressed == _reqAlt) &&
                           (shiftPressed == _reqShift) &&
                           (winPressed == _reqWin);

        bool anyModifierRequired = _reqCtrl || _reqAlt || _reqShift || _reqWin;
        return hotkeyMatch && anyModifierRequired;
    }


    void ShowLauncherMenu(System.Windows.Point position)
    {
        CloseCurrentLauncherMenu();

        var itemsList = _configManager.LoadLauncherItems();
        var observableItems = new ObservableCollection<LauncherItem>(itemsList);

        if(!observableItems.Any() || observableItems.All(it => it.ExecutablePath == "NO_ACTION"))
        {
            Debug.WriteLine("No launcher items configured or only placeholder exists.");
        }

        _currentLauncherMenu = new LauncherMenuWindow(observableItems, position, _configManager);
        _currentLauncherMenu.Closed += (s, ev) =>
        {
            _currentLauncherMenu = null;
        };
        _currentLauncherMenu.Show();
    }

    void CloseCurrentLauncherMenu()
    {
        if(_currentLauncherMenu != null)
        {
            try
            {
                _currentLauncherMenu.Close();
            }
            catch(Exception ex)
            {
                Debug.WriteLine($"Error closing launcher menu: {ex.Message}");
            }
            _currentLauncherMenu = null;
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if(!_isDisposed)
        {
            if(disposing)
            {
                StopMonitoring();
                if(_windowsHooks != null)
                {
                    _windowsHooks.RightMouseClick -= OnRightMouseClick;
                }
            }
            _isDisposed = true;
        }
    }
}