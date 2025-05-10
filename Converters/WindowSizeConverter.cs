using RightClickAppLauncher.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Data;

namespace RightClickAppLauncher.Converters
{
    public class WindowSizeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if(value is NamedLayout layout)
            {
                return $"{layout.WindowWidth:0}x{layout.WindowHeight:0}";
            }
            return "N/A";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}