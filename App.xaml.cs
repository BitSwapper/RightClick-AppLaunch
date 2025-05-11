// File: App.xaml.cs
using System.Diagnostics;
using System.Windows;
using RightClickAppLauncher.Managers;
using RightClickAppLauncher.Native; // For OS_StartupManager
using RightClickAppLauncher.Properties;

namespace RightClickAppLauncher;

public partial class App : System.Windows.Application
{
    NotifyIcon _notifyIcon;
    TaskbarMonitor _taskbarMonitor;
    LauncherConfigManager _configManager;
    bool _isExiting = false;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Ensure only one instance is running
        string appName = System.Reflection.Assembly.GetExecutingAssembly().GetName().Name;
        bool createdNew;
        System.Threading.Mutex mutex = new System.Threading.Mutex(true, appName, out createdNew);

        if(!createdNew)
        {
            System.Windows.MessageBox.Show($"{StaticVals.AppName} is already running.", "Application Already Running", MessageBoxButton.OK, MessageBoxImage.Information);
            _isExiting = true;
            System.Windows.Application.Current.Shutdown();
            return;
        }


        _configManager = new LauncherConfigManager();
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

        // Load icon from Resources/Gear.ico
        try
        {
            var iconUri = new Uri("pack://application:,,,/Resources/Gear.ico");
            var iconStream = System.Windows.Application.GetResourceStream(iconUri);

            if(iconStream != null)
            {
                _notifyIcon.Icon = new System.Drawing.Icon(iconStream.Stream);
                Debug.WriteLine("System tray icon loaded successfully");
            }
            else
            {
                // Fallback to system icon
                _notifyIcon.Icon = SystemIcons.Application;
                Debug.WriteLine("Using SystemIcons.Application as tray icon.");
            }
        }
        catch(Exception ex)
        {
            _notifyIcon.Icon = SystemIcons.Application; // Fallback icon
            Debug.WriteLine($"Error loading tray icon: {ex.Message}");
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
            // Non-fatal, app can continue
        }
    }


    void OnSettingsClicked(object sender, EventArgs e)
    {
        var settingsWindow = new SettingsWindow { Owner = GetActiveWindow() };
        bool? result = settingsWindow.ShowDialog();
        if(result == true) // If settings were saved
        {
            _taskbarMonitor.ReloadHotkeySettings(); // Reload hotkey settings
        }
    }

    // Helper to find an active window to be owner of dialogs
    Window GetActiveWindow()
    {
        foreach(Window window in System.Windows.Application.Current.Windows)
        {
            if(window.IsActive) return window;
        }
        // If no window is active (e.g. only tray icon), return null or a hidden main window if you have one.
        // For simple tray app, null is fine, dialog will not be owned.
        return null;
    }


    void OnExitClicked(object sender, EventArgs e)
    {
        _isExiting = true;
        System.Windows.Application.Current.Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if(!_isExiting) // Prevent cleanup if already exiting due to mutex
        {
            _taskbarMonitor?.StopMonitoring();
            _taskbarMonitor?.Dispose();
            _notifyIcon?.Dispose();
        }

        if(_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }

        base.OnExit(e);
        Debug.WriteLine($"{StaticVals.AppName} exited.");
    }
}