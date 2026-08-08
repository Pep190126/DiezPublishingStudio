using Avalonia.Controls;

namespace DiezPublishingStudio;

internal static class GridColumnExtensions
{
    public static T WithGridColumn<T>(this T control, int column) where T : Control
    {
        Grid.SetColumn(control, column);
        return control;
    }
}
