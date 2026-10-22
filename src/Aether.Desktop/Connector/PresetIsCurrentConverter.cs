using System;
using System.Collections;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Aether.Desktop.Connector;

/// <summary>
/// 多值 Converter：把 chip 自身（ConnectionPreset）与 VM.SelectedPreset 比较 → bool → is-current class。
/// XAML 用 MultiBinding 传入两个值。
/// </summary>
public sealed class PresetIsCurrentConverter : IMultiValueConverter
{
    public static readonly PresetIsCurrentConverter Instance = new();

    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values != null && values.Count >= 2 && values[0] is ConnectionPreset a && values[1] is ConnectionPreset b)
            return string.Equals(a.Name, b.Name, StringComparison.Ordinal);
        return false;
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
