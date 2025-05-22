using System.Diagnostics;
using System.Windows.Media;
using RightClickAppLauncher.Models;
using RightClickAppLauncher.Utils;
using Application = System.Windows.Application;

namespace RightClickAppLauncher.Services;

public enum IconSizeCategory { Small, Large }

public class IconCacheService
{
    static readonly Lazy<IconCacheService> _lazyInstance =
        new Lazy<IconCacheService>(() => new IconCacheService());
    public static IconCacheService Instance => _lazyInstance.Value;

    readonly Dictionary<string, ImageSource> _iconCache = new Dictionary<string, ImageSource>();
    ImageSource _defaultSmallIcon { get; set; }
    ImageSource _defaultLargeIcon { get; set; }

    IconCacheService()
    {
        if(Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                _defaultSmallIcon = IconExtractor.GetIcon(null, true);
                _defaultLargeIcon = IconExtractor.GetIcon(null, false);
            });
        }
        else
        {
            _defaultSmallIcon = IconExtractor.GetIcon(null, true);
            _defaultLargeIcon = IconExtractor.GetIcon(null, false);
        }
    }

    string GetCacheKey(string path, IconSizeCategory size) => $"{path?.ToLowerInvariant()}_{size}";

    public ImageSource GetOrAddIcon(LauncherItem item, IconSizeCategory sizeCategory)
    {
        if(item == null)
            return sizeCategory == IconSizeCategory.Small ? _defaultSmallIcon : _defaultLargeIcon;

        string pathForIcon = item.IconPath;
        if(string.IsNullOrWhiteSpace(pathForIcon))
        {
            pathForIcon = item.ExecutablePath;
        }

        if(string.IsNullOrWhiteSpace(pathForIcon))
        {
            Debug.WriteLine($"No valid path for icon for item '{item.DisplayName}'. Using system default.");
            return sizeCategory == IconSizeCategory.Small ? _defaultSmallIcon : _defaultLargeIcon;
        }

        string cacheKey = GetCacheKey(pathForIcon, sizeCategory);

        lock(_iconCache)
        {
            if(_iconCache.TryGetValue(cacheKey, out ImageSource cachedIcon))
            {
                return cachedIcon;
            }
        }

        bool useSmall = sizeCategory == IconSizeCategory.Small;
        ImageSource newIcon = IconExtractor.GetIcon(pathForIcon, useSmall);

        if(newIcon == null && !string.IsNullOrWhiteSpace(item.ExecutablePath) && item.ExecutablePath != pathForIcon)
        {
            Debug.WriteLine($"Custom icon path '{item.IconPath}' for '{item.DisplayName}' failed. Trying executable path '{item.ExecutablePath}'.");
            string fallbackCacheKey = GetCacheKey(item.ExecutablePath, sizeCategory);
            lock(_iconCache)
            {
                if(_iconCache.TryGetValue(fallbackCacheKey, out ImageSource cachedFallbackIcon))
                {
                    return cachedFallbackIcon;
                }
            }
            newIcon = IconExtractor.GetIcon(item.ExecutablePath, useSmall);
            if(newIcon != null)
            {
                lock(_iconCache)
                {
                    _iconCache[fallbackCacheKey] = newIcon;
                }
            }
        }

        if(newIcon != null)
        {
            lock(_iconCache)
            {
                _iconCache[cacheKey] = newIcon;
            }
            return newIcon;
        }

        Debug.WriteLine($"Failed to extract icon from '{pathForIcon}' (and fallback if applicable) for item '{item.DisplayName}'. Using system default.");
        return sizeCategory == IconSizeCategory.Small ? _defaultSmallIcon : _defaultLargeIcon;
    }

    public async Task PreloadIconsAsync(IEnumerable<LauncherItem> items)
    {
        if(items == null || !items.Any()) return;

        await Task.Run(() =>
        {
            int count = 0;
            foreach(var item in items)
            {
                GetOrAddIcon(item, IconSizeCategory.Small);
                GetOrAddIcon(item, IconSizeCategory.Large);
                count++;
            }
            Debug.WriteLine($"IconCacheService: Preloaded icons for {count} items.");
        }).ConfigureAwait(false);
    }
}