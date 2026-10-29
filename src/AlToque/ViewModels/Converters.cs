using AlToque.Chasqui;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace AlToque.ViewModels;

public static class Converters
{
    public static readonly IValueConverter BoolToSemiBold =
        new FuncValueConverter<bool, FontWeight>(b => b ? FontWeight.SemiBold : FontWeight.Normal);

    public static readonly IValueConverter SuccessOrError =
        new FuncValueConverter<bool, ChasquiState>(ok => ok ? ChasquiState.Success : ChasquiState.Error);

    public static readonly IValueConverter ErrorForeground =
        new FuncValueConverter<bool, IBrush?>(error => error ? Brushes2.FromHex("#E5484D") : null);
}
