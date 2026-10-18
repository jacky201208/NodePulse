using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace NodePulse.Converters
{
    /// <summary>
    /// Avalonia无Visibility枚举，绑定IsVisible(bool)
    /// true=显示 false=隐藏
    /// </summary>
    public class BoolToVisibilityConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool b)
                return b;
            return false;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// 布尔取反，用于IsVisible绑定
    /// </summary>
    public class InverseBoolVisibilityConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool b)
                return !b;
            return true;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class BoolToBlackBorderConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool b && b)
                return Brushes.Black;
            return Brushes.Transparent;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
