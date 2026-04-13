using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace SwiftDrop.Desktop.ViewModels;

public class BoolToBrushConverter : IValueConverter
{
    public static readonly BoolToBrushConverter Instance = new();

    public object Convert(object? value, Type targetType,
        object? parameter, CultureInfo culture)
        => value is true
            ? new SolidColorBrush(Color.Parse("#1e1e35"))
            : new SolidColorBrush(Colors.Transparent);

    public object ConvertBack(object? value, Type targetType,
        object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}