using System.Globalization;
using System.Windows.Data;
namespace RightClickAppLauncher.Converters;

public class IconSizeToFontSizeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if(value is double iconSize)
            return Math.Max(8, iconSize / 3);
        return 12;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}


