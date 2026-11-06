using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using ExpoReturn.Domain;

namespace ExpoReturn.Desktop;

public sealed class StatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is WorkflowStatus s
        ? Brush(s switch
        {
            WorkflowStatus.Draft => "#4B5563",
            WorkflowStatus.Requested => "#64748B",
            WorkflowStatus.Imported => "#2E75B6",
            WorkflowStatus.InRework => "#4F46E5",
            WorkflowStatus.PendingHse => "#B45309",
            WorkflowStatus.PendingExport => "#C2410C",
            WorkflowStatus.DutyRisk => "#DC2626",
            WorkflowStatus.Exported => "#15803D",
            WorkflowStatus.Scrapped => "#0F766E",
            WorkflowStatus.ClosedDutyPaid => "#7C3AED",
            _ => "#6B7280",
        })
        : Brushes.Gray;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;

    internal static SolidColorBrush Brush(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }
}

public sealed class SeverityToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        Severity.Error => StatusToBrushConverter.Brush("#B91C1C"),
        Severity.Warning => StatusToBrushConverter.Brush("#B45309"),
        _ => StatusToBrushConverter.Brush("#2E75B6"),
    };
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>true -> Visible. ConverterParameter="Invert" invierte.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var b = value is true || (value is int i && i > 0) || (value is string s && !string.IsNullOrWhiteSpace(s));
        if (parameter as string == "Invert") b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
