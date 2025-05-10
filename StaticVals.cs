// File: StaticVals.cs
using System.Diagnostics;

namespace RightClickAppLauncher
{
    public static class StaticVals
    {
        public const string AppName = "Right Click App Launcher"; // UPDATED
        public static readonly string AppPath = Process.GetCurrentProcess().MainModule.FileName;
        public const string RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    }
}

