using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace TelegraphGallery.Converters
{
    public class DuplicateGroupToBrushConverter : IValueConverter
    {
        private static readonly Brush[] GroupBrushes =
        [
            new SolidColorBrush(Color.FromArgb(40, 255, 100, 100)),
            new SolidColorBrush(Color.FromArgb(40, 100, 100, 255)),
            new SolidColorBrush(Color.FromArgb(40, 100, 255, 100)),
            new SolidColorBrush(Color.FromArgb(40, 255, 255, 100)),
            new SolidColorBrush(Color.FromArgb(40, 255, 100, 255)),
            new SolidColorBrush(Color.FromArgb(40, 100, 255, 255))
        ];

        static DuplicateGroupToBrushConverter()
        {
            foreach (var brush in GroupBrushes)
                brush.Freeze();
        }

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is int groupId)
            {
                return GroupBrushes[groupId % GroupBrushes.Length];
            }

            return Brushes.Transparent;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
