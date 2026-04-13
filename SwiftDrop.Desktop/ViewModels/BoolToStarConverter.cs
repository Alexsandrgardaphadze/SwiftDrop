// BoolToStarConverter.cs
using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace SwiftDrop.Desktop.ViewModels;

public class BoolToStarConverter : IValueConverter
{
    public static readonly BoolToStarConverter Instance = new();
    public object Convert(object? value, Type targetType,
        object? parameter, CultureInfo culture)
        => value is true ? "⭐" : "☆";
    public object ConvertBack(object? value, Type targetType,
        object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}