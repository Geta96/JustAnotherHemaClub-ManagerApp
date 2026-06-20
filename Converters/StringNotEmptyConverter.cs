using System.Globalization;

namespace JustAnotherHemaClub.Converters;

/// <summary>
/// Returns <c>true</c> when the bound string is non-null and non-whitespace,
/// <c>false</c> otherwise. Used to toggle <c>IsVisible</c> on error banners and
/// caption labels that should hide while their backing string is empty.
/// </summary>
public sealed class StringNotEmptyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => !string.IsNullOrWhiteSpace(value as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}