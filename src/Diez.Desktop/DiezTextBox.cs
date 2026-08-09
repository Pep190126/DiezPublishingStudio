global using TextBox = DiezPublishingStudio.DiezTextBox;

namespace DiezPublishingStudio;

internal class DiezTextBox : Avalonia.Controls.TextBox
{
    public Avalonia.Controls.Primitives.ScrollBarVisibility VerticalScrollBarVisibility
    {
        get => GetValue(Avalonia.Controls.ScrollViewer.VerticalScrollBarVisibilityProperty);
        set => SetValue(Avalonia.Controls.ScrollViewer.VerticalScrollBarVisibilityProperty, value);
    }
}
