// CountToBoolConverter.cs
using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace SwiftDrop.Desktop.ViewModels;

public class CountToBoolConverter : IValueConverter
{
    public static readonly CountToBoolConverter Instance = new();
    public object Convert(object? value, Type targetType,
        object? parameter, CultureInfo culture)
        => value is int count && count > 0;
    public object ConvertBack(object? value, Type targetType,
        object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}