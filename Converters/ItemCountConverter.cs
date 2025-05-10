using Newtonsoft.Json;
using RightClickAppLauncher.Models;
using System.Globalization;
using System.Windows.Data;

namespace RightClickAppLauncher.Converters
{
    public class ItemCountConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if(value is NamedLayout layout && !string.IsNullOrWhiteSpace(layout.LayoutJson))
            {
                try
                {
                    var items = JsonConvert.DeserializeObject<List<LauncherItem>>(layout.LayoutJson);
                    return items?.Count ?? 0;
                }
                catch
                {
                    return 0;
                }
            }
            return 0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}