// File: Native/OS_StartupManager.cs
using System.Diagnostics;
using Microsoft.Win32;
using static RightClickAppLauncher.StaticVals; // Ensure this using matches your StaticVals namespace

namespace RightClickAppLauncher.Native;

public static class OS_StartupManager
{
    // ... (rest of the code is the same as provided in the question, just ensure namespace is correct)
    public static bool IsInStartup()
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryPath, false);
            if(key == null) return false;
            object value = key.GetValue(AppName);
            return value != null && value.ToString().Equals(AppPath, StringComparison.OrdinalIgnoreCase);
        }
        catch(Exception ex)
        {
            Debug.WriteLine($"Error checking startup registry: {ex.Message}");
            return false;
        }
    }

    public static void AddToStartup()
    {
        if(IsInStartup())
        {
            Debug.WriteLine($"Application already in startup registry: {AppName} -> {AppPath}");
            return;
        }

        try
        {
            using RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryPath, true) ??
                                  Registry.CurrentUser.CreateSubKey(RegistryPath, true); // Create if not exists

            if(key == null)
            {
                Debug.WriteLine("Error adding to startup: Could not open or create registry key.");
                throw new InvalidOperationException("Could not open or create startup registry key.");
            }
            key.SetValue(AppName, $"\"{AppPath}\""); // Enclose path in quotes
            Debug.WriteLine($"Added startup entry: {AppName} -> \"{AppPath}\"");
        }
        catch(UnauthorizedAccessException ex)
        {
            Debug.WriteLine($"Error adding to startup (Unauthorized): {ex.Message}");
            throw;
        }
        catch(Exception ex)
        {
            Debug.WriteLine($"Error adding to startup: {ex.Message}");
            throw;
        }
    }

    public static void RemoveFromStartup()
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryPath, true);
            if(key == null)
            {
                Debug.WriteLine("Error removing from startup: Could not open registry key (key does not exist).");
                return;
            }
            if(key.GetValue(AppName) != null)
            {
                key.DeleteValue(AppName, false);
                Debug.WriteLine($"Removed startup entry: {AppName}");
            }
            else
                Debug.WriteLine($"Startup entry not found, nothing to remove: {AppName}");
        }
        catch(UnauthorizedAccessException ex)
        {
            Debug.WriteLine($"Error removing from startup (Unauthorized): {ex.Message}");
            throw;
        }
        catch(Exception ex)
        {
            Debug.WriteLine($"Error removing from startup: {ex.Message}");
            throw;
        }
    }
}