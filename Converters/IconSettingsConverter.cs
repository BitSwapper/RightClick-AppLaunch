using System.Globalization;
using System.Windows.Data;
using RightClickAppLauncher.Models;

namespace RightClickAppLauncher.Converters;

public class IconSettingsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if(value is NamedLayout layout)
        {
            return $"{layout.IconSize:0}px / {layout.IconSpacing:0}px";
        }
        return "N/A";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}