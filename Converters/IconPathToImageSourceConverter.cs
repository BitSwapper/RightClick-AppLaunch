using System.Globalization;
using System.Windows.Data;
using RightClickAppLauncher.Models;
using RightClickAppLauncher.Services;

namespace RightClickAppLauncher.Converters;

public class IconPathToImageSourceConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if(!(value is LauncherItem item))
            return null;

        string sizeParam = parameter as string;
        IconSizeCategory iconSizeCat = IconSizeCategory.Large;

        if(!string.IsNullOrEmpty(sizeParam))
            if(sizeParam.Equals("small", StringComparison.OrdinalIgnoreCase))
                iconSizeCat = IconSizeCategory.Small;

        return IconCacheService.Instance.GetOrAddIcon(item, iconSizeCat);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}