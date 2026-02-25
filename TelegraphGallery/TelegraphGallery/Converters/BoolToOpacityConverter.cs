using System;
using System.Globalization;
using System.Windows.Data;

namespace TelegraphGallery.Converters
{
    public class BoolToOpacityConverter : IValueConverter
    {
        private const double ExcludedOpacity = 0.4;
        private const double FullOpacity = 1.0;

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is true)
            {
                return ExcludedOpacity;
            }

            return FullOpacity;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
