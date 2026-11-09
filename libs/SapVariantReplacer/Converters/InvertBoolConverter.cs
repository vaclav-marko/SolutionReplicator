using System.Globalization;
using System.Windows.Data;

namespace SapVariantReplacer;

/// <summary>
/// Converts bool to its inverse. Used to disable controls when IsBusy is true.
/// </summary>
/// <remarks>
/// Relocated out of App.xaml.cs so this converter survives when the project is
/// consumed as a class library (App.xaml / App.xaml.cs are excluded from the
/// library build by SapVariantReplacer.csproj).
/// </remarks>
public sealed class InvertBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : value;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : value;
}
