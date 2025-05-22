using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using RightClickAppLauncher.Models;
using RightClickAppLauncher.Utils; // For IconExtractor
using Application = System.Windows.Application; // To resolve ambiguity with System.Windows.Forms.Application if used

namespace RightClickAppLauncher.Services
{
    public enum IconSizeCategory { Small, Large }

    public class IconCacheService
    {
        private static readonly Lazy<IconCacheService> _lazyInstance =
            new Lazy<IconCacheService>(() => new IconCacheService());
        public static IconCacheService Instance => _lazyInstance.Value;

        private readonly Dictionary<string, ImageSource> _iconCache = new Dictionary<string, ImageSource>();
        private ImageSource _defaultSmallIcon { get; set; }
        private ImageSource _defaultLargeIcon { get; set; }

        private IconCacheService() // Private constructor for singleton
        {
            // Initialize default icons. These are frozen by IconExtractor.
            // If IconExtractor.GetIcon might not be thread-safe for some reason,
            // ensure this constructor or these calls are made from the UI thread,
            // but frozen ImageSources are generally fine.
            if(Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    _defaultSmallIcon = IconExtractor.GetIcon(null, true); // Default system icon (small)
                    _defaultLargeIcon = IconExtractor.GetIcon(null, false); // Default system icon (large)
                });
            }
            else
            {
                _defaultSmallIcon = IconExtractor.GetIcon(null, true);
                _defaultLargeIcon = IconExtractor.GetIcon(null, false);
            }
        }

        private string GetCacheKey(string path, IconSizeCategory size) => $"{path?.ToLowerInvariant()}_{size}";

        public ImageSource GetOrAddIcon(LauncherItem item, IconSizeCategory sizeCategory)
        {
            if(item == null)
                return sizeCategory == IconSizeCategory.Small ? _defaultSmallIcon : _defaultLargeIcon;

            // Determine the path to use for the icon: Custom IconPath first, then ExecutablePath.
            string pathForIcon = item.IconPath;
            if(string.IsNullOrWhiteSpace(pathForIcon))
            {
                pathForIcon = item.ExecutablePath;
            }

            // If still no path, use a generic default icon.
            if(string.IsNullOrWhiteSpace(pathForIcon))
            {
                Debug.WriteLine($"No valid path for icon for item '{item.DisplayName}'. Using system default.");
                return sizeCategory == IconSizeCategory.Small ? _defaultSmallIcon : _defaultLargeIcon;
            }

            string cacheKey = GetCacheKey(pathForIcon, sizeCategory);

            lock(_iconCache) // Ensure thread safety for cache access
            {
                if(_iconCache.TryGetValue(cacheKey, out ImageSource cachedIcon))
                {
                    return cachedIcon;
                }
            }

            // If not in cache, extract it. IconExtractor is synchronous.
            // This part could be run on a Task.Run if it becomes a bottleneck during normal use,
            // but for pre-loading it's already on a background thread.
            // For on-demand, it's usually fast enough.
            bool useSmall = sizeCategory == IconSizeCategory.Small;
            ImageSource newIcon = IconExtractor.GetIcon(pathForIcon, useSmall);

            if(newIcon == null && !string.IsNullOrWhiteSpace(item.ExecutablePath) && item.ExecutablePath != pathForIcon)
            {
                // If custom IconPath failed, and it wasn't the ExecutablePath already, try ExecutablePath as a final fallback.
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
                    lock(_iconCache) // Ensure thread safety for cache access
                    {
                        _iconCache[fallbackCacheKey] = newIcon; // Cache under the executable path key
                    }
                }
            }

            if(newIcon != null)
            {
                lock(_iconCache) // Ensure thread safety for cache access
                {
                    _iconCache[cacheKey] = newIcon; // Cache under the original primary path key
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
                    // Preload both sizes as they might be used in different contexts
                    GetOrAddIcon(item, IconSizeCategory.Small);
                    GetOrAddIcon(item, IconSizeCategory.Large);
                    count++;
                }
                Debug.WriteLine($"IconCacheService: Preloaded icons for {count} items.");
            }).ConfigureAwait(false); // ConfigureAwait(false) if not returning to UI context directly
        }
    }
}