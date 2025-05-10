// File: App.xaml.cs
using RightClickAppLauncher.Managers;
using RightClickAppLauncher.Native; // For OS_StartupManager
using RightClickAppLauncher.Properties;
using System;
using System.Diagnostics;
using System.Drawing; // For System.Drawing.Icon
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Forms; // For NotifyIcon, ContextMenuStrip, ToolStripMenuItem

namespace RightClickAppLauncher
{
    public partial class App : System.Windows.Application
    {
        private NotifyIcon _notifyIcon;
        private TaskbarMonitor _taskbarMonitor;
        private LauncherConfigManager _configManager;
        private bool _isExiting = false;

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

        private void SetupTrayIcon()
        {
            _notifyIcon = new NotifyIcon();
            _notifyIcon.Text = StaticVals.AppName;

            // Load icon from embedded resource or file
            try
            {
                // Assuming you have an App.ico file in your project, set as "Embedded Resource"
                Stream iconStream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/App.ico"))?.Stream;
                if(iconStream != null)
                {
                    _notifyIcon.Icon = new System.Drawing.Icon(iconStream);
                    iconStream.Dispose();
                }
                else // Fallback if embedded icon not found
                {
                    Assembly currentAssembly = Assembly.GetExecutingAssembly();
                    string[] manifestResourceNames = currentAssembly.GetManifestResourceNames();
                    Debug.WriteLine("Available manifest resources:");
                    foreach(string name in manifestResourceNames) Debug.WriteLine(name);

                    // Try loading a default system icon if App.ico is missing
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

        private void ApplyStartupSetting()
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


        private void OnSettingsClicked(object sender, EventArgs e)
        {
            var settingsWindow = new SettingsWindow { Owner = GetActiveWindow() };
            bool? result = settingsWindow.ShowDialog();
            if(result == true) // If settings were saved
            {
                _taskbarMonitor.ReloadHotkeySettings(); // Reload hotkey settings
            }
        }

        // Helper to find an active window to be owner of dialogs
        private Window GetActiveWindow()
        {
            foreach(Window window in System.Windows.Application.Current.Windows)
            {
                if(window.IsActive) return window;
            }
            // If no window is active (e.g. only tray icon), return null or a hidden main window if you have one.
            // For simple tray app, null is fine, dialog will not be owned.
            return null;
        }


        private void OnExitClicked(object sender, EventArgs e)
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
            base.OnExit(e);
            Debug.WriteLine($"{StaticVals.AppName} exited.");
        }
    }
}