// InverseBoolConverter.cs
using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace SwiftDrop.Desktop.ViewModels;

public class InverseBoolConverter : IValueConverter
{
    public static readonly InverseBoolConverter Instance = new();
    public object Convert(object? value, Type targetType,
        object? parameter, CultureInfo culture)
        => value is bool b && !b;
    public object ConvertBack(object? value, Type targetType,
        object? parameter, CultureInfo culture)
        => value is bool b && !b;
}