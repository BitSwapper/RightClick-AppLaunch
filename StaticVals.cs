using System.Diagnostics;
using System.IO;

namespace RightClickAppLauncher;

public static class StaticVals
{
    public const string AppName = "Right Click App Launcher";
    public static readonly string AppPath = Process.GetCurrentProcess().MainModule.FileName;
    public const string RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";


    public static string AppDataPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName);

    public static void EnsureAppDataPathExists()
    {
        if(!Directory.Exists(AppDataPath))
            Directory.CreateDirectory(AppDataPath);
    }
}
