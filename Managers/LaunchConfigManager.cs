using System.Diagnostics;
using Newtonsoft.Json;
using RightClickAppLauncher.Models;
using RightClickAppLauncher.Properties;

namespace RightClickAppLauncher.Managers;

public class LauncherConfigManager
{
    public List<LauncherItem> LoadLauncherItems()
    {
        try
        {
            string json = Settings.Default.LauncherItemsConfig;
            if(!string.IsNullOrWhiteSpace(json))
            {
                var items = JsonConvert.DeserializeObject<List<LauncherItem>>(json);
                return items ?? new List<LauncherItem>();
            }
        }
        catch(Exception ex)
        {
            Debug.WriteLine($"Error loading launcher items: {ex.Message}");
        }
        return new List<LauncherItem>();
    }

    public void SaveLauncherItems(List<LauncherItem> items)
    {
        try
        {
            string json = JsonConvert.SerializeObject(items, Formatting.Indented);
            Settings.Default.LauncherItemsConfig = json;
            Settings.Default.Save();
        }
        catch(Exception ex)
        {
            Debug.WriteLine($"Error saving launcher items: {ex.Message}");
        }
    }
}
