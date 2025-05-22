using System.Diagnostics;
using System.Windows;
using RightClickAppLauncher.Managers;
using RightClickAppLauncher.Native;
using RightClickAppLauncher.Properties;
using RightClickAppLauncher.Services;

namespace RightClickAppLauncher;

public partial class App : System.Windows.Application
{
    NotifyIcon _notifyIcon;
    TaskbarMonitor _taskbarMonitor;
    LauncherConfigManager _configManager;
    bool _isExiting = false;
    Mutex _mutex;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        string appName = System.Reflection.Assembly.GetExecutingAssembly().GetName().Name;
        bool createdNew;
        _mutex = new Mutex(true, appName, out createdNew);

        if(!createdNew)
        {
            System.Windows.MessageBox.Show($"{StaticVals.AppName} is already running.", "Application Already Running", MessageBoxButton.OK, MessageBoxImage.Information);
            _isExiting = true;
            Shutdown();
            return;
        }

        _configManager = new LauncherConfigManager();

        var itemsToPreload = _configManager.LoadLauncherItems();
        if(itemsToPreload.Any())
        {
            Debug.WriteLine($"App.OnStartup: Starting to preload icons for {itemsToPreload.Count} items.");
            _ = IconCacheService.Instance.PreloadIconsAsync(itemsToPreload)
                .ContinueWith(t =>
                {
                    if(t.IsFaulted) Debug.WriteLine($"Icon preloading failed: {t.Exception?.GetBaseException().Message}");
                    else Debug.WriteLine("Icon preloading task completed.");
                });
        }
        else
        {
            Debug.WriteLine("App.OnStartup: No items to preload icons for.");
        }

        _taskbarMonitor = new TaskbarMonitor(_configManager);

        SetupTrayIcon();
        ApplyStartupSetting();

        _taskbarMonitor.StartMonitoring();
        Debug.WriteLine($"{StaticVals.AppName} started.");
    }

    void SetupTrayIcon()
    {
        _notifyIcon = new NotifyIcon();
        _notifyIcon.Text = StaticVals.AppName;

        try
        {
            var iconUri = new Uri("pack://application:,,,/Resources/Gear.ico");
            var iconStreamInfo = System.Windows.Application.GetResourceStream(iconUri);

            if(iconStreamInfo != null)
            {
                using(var iconStream = iconStreamInfo.Stream)
                {
                    _notifyIcon.Icon = new System.Drawing.Icon(iconStream);
                }
                Debug.WriteLine("System tray icon loaded successfully from Gear.ico");
            }
            else
            {
                _notifyIcon.Icon = SystemIcons.Application;
                Debug.WriteLine("Failed to load Gear.ico, using SystemIcons.Application as tray icon.");
            }
        }
        catch(Exception ex)
        {
            _notifyIcon.Icon = SystemIcons.Application;
            Debug.WriteLine($"Error loading tray icon: {ex.Message}. Using SystemIcons.Application.");
        }

        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add("Settings...", null, OnSettingsClicked);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add("Exit", null, OnExitClicked);
        _notifyIcon.ContextMenuStrip = contextMenu;

        _notifyIcon.Visible = true;
    }

    void ApplyStartupSetting()
    {
        try
        {
            if(Settings.Default.LaunchOnStartup)
            {
                if(!OS_StartupManager.IsInStartup())
                {
                    OS_StartupManager.AddToStartup();
                }
            }
            else
            {
                if(OS_StartupManager.IsInStartup())
                {
                    OS_StartupManager.RemoveFromStartup();
                }
            }
        }
        catch(Exception ex)
        {
            Debug.WriteLine($"Error managing startup registry: {ex.Message}");
        }
    }

    void OnSettingsClicked(object sender, EventArgs e)
    {
        var settingsWindow = new SettingsWindow { Owner = GetActiveWindow() };
        bool? result = settingsWindow.ShowDialog();
        if(result == true)
        {
            _taskbarMonitor.ReloadHotkeySettings();
            var itemsToPreload = _configManager.LoadLauncherItems();
            _ = IconCacheService.Instance.PreloadIconsAsync(itemsToPreload)
               .ContinueWith(t =>
               {
                   if(t.IsFaulted) Debug.WriteLine($"Icon re-preloading failed: {t.Exception?.GetBaseException().Message}");
                   else Debug.WriteLine("Icon re-preloading task completed after settings change.");
               });
        }
    }

    Window GetActiveWindow() => System.Windows.Application.Current.Windows.OfType<Window>().SingleOrDefault(x => x.IsActive);

    void OnExitClicked(object sender, EventArgs e)
    {
        _isExiting = true;
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if(!_isExiting && _mutex == null)
        {
            _taskbarMonitor?.StopMonitoring();
            _taskbarMonitor?.Dispose();
        }

        _taskbarMonitor?.Dispose();

        if(_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }

        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        _mutex = null;

        base.OnExit(e);
        Debug.WriteLine($"{StaticVals.AppName} exited.");
    }
}