using Newtonsoft.Json;
using RightClickAppLauncher.Models;
using RightClickAppLauncher.Properties;
using RightClickAppLauncher.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Windows.Data;

namespace RightClickAppLauncher.Converters
{
    public class IconPathToImageSourceConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if(value is string iconPath)
            {
                bool useSmallIcon = parameter as string == "small";
                return IconExtractor.GetIcon(iconPath, useSmallIcon);
            }
            return IconExtractor.GetIcon(null);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}