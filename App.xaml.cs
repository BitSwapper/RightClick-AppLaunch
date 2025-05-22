// File: App.xaml.cs
using System;
using System.Diagnostics;
using System.Drawing; // For SystemIcons
using System.Linq;
using System.Threading; // For Mutex
using System.Windows;
using System.Windows.Forms; // For NotifyIcon, ContextMenuStrip, etc.
using RightClickAppLauncher.Managers;
using RightClickAppLauncher.Native;
using RightClickAppLauncher.Properties;
using RightClickAppLauncher.Services; // Added for IconCacheService

namespace RightClickAppLauncher
{
    public partial class App : System.Windows.Application
    {
        private NotifyIcon _notifyIcon;
        private TaskbarMonitor _taskbarMonitor;
        private LauncherConfigManager _configManager;
        private bool _isExiting = false;
        private Mutex _mutex;

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Ensure only one instance is running
            string appName = System.Reflection.Assembly.GetExecutingAssembly().GetName().Name;
            bool createdNew;
            _mutex = new Mutex(true, appName, out createdNew);

            if(!createdNew)
            {
                System.Windows.MessageBox.Show($"{StaticVals.AppName} is already running.", "Application Already Running", MessageBoxButton.OK, MessageBoxImage.Information);
                _isExiting = true; // Set flag before shutting down
                Shutdown(); // Use Shutdown() instead of Current.Shutdown() here for clarity
                return;
            }

            _configManager = new LauncherConfigManager();

            // Preload icons asynchronously
            var itemsToPreload = _configManager.LoadLauncherItems();
            if(itemsToPreload.Any())
            {
                Debug.WriteLine($"App.OnStartup: Starting to preload icons for {itemsToPreload.Count} items.");
                // No await here, let it run in the background. Application startup continues.
                // IconCacheService.Instance is initialized lazily on first access.
                _ = IconCacheService.Instance.PreloadIconsAsync(itemsToPreload)
                    .ContinueWith(t => {
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
                _notifyIcon.Icon = SystemIcons.Application; // Fallback icon
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
            // Bring to front if already open, or create new.
            // This simple version just creates a new one.
            // Consider a more robust way to manage the SettingsWindow instance if needed.
            var settingsWindow = new SettingsWindow { Owner = GetActiveWindow() };
            bool? result = settingsWindow.ShowDialog();
            if(result == true)
            {
                _taskbarMonitor.ReloadHotkeySettings();
                // After settings are saved, items might have changed. Re-cache if necessary.
                // For simplicity, a full re-cache on settings save can be done,
                // or a more granular update if performance is critical.
                // Kicking off a new preload task would be one way.
                var itemsToPreload = _configManager.LoadLauncherItems();
                _ = IconCacheService.Instance.PreloadIconsAsync(itemsToPreload)
                   .ContinueWith(t => {
                       if(t.IsFaulted) Debug.WriteLine($"Icon re-preloading failed: {t.Exception?.GetBaseException().Message}");
                       else Debug.WriteLine("Icon re-preloading task completed after settings change.");
                   });
            }
        }

        Window GetActiveWindow()
        {
            // Gets the currently active window of this application.
            return System.Windows.Application.Current.Windows.OfType<Window>().SingleOrDefault(x => x.IsActive);
            // Fallback: return Application.Current.MainWindow if you have one and it's suitable.
        }

        void OnExitClicked(object sender, EventArgs e)
        {
            _isExiting = true; // Set flag before shutting down
            Shutdown(); // Use Shutdown() to trigger OnExit
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if(!_isExiting && _mutex == null) // Check if already exiting due to mutex (mutex will be null if startup failed early)
            {
                // This path might not be hit if _isExiting is always true before shutdown
                // but good for defensive programming.
                _taskbarMonitor?.StopMonitoring();
                _taskbarMonitor?.Dispose();
            }

            // Always try to dispose resources if they were created
            _taskbarMonitor?.Dispose(); // StopMonitoring is called inside Dispose

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
}