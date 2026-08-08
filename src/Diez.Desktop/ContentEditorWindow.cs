using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace DiezPublishingStudio;

internal sealed class ContentEditorWindow : Window
{
    private readonly TextBox _editor;

    public ContentEditorWindow(ContentNode node, int revisionCount)
    {
        Title = $"Modifica Master — {node.Title}";
        Width = 900;
        Height = 720;
        MinWidth = 700;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var heading = new TextBlock
        {
            Text = $"{node.Kind}: {node.Title}",
            FontSize = 22,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        };
        var source = new TextBlock
        {
            Text = $"Sorgente: {node.SourceLocator} · revisioni manuali registrate: {revisionCount}",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        };
        var notice = new TextBlock
        {
            Text = "Stai modificando il Master editoriale di Diez. Il file originale importato e incorporato nel .diez non viene sovrascritto.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        };

        _editor = new TextBox
        {
            Text = node.Body,
            AcceptsReturn = true,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Height = 500,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var cancel = new Button { Content = "Annulla", Width = 140 };
        cancel.Click += (_, _) => Close((string?)null);
        var save = new Button { Content = "Salva nel Master", Width = 180 };
        save.Click += (_, _) => Close(_editor.Text ?? string.Empty);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { cancel, save }
        };

        Content = new Border
        {
            Padding = new Thickness(20),
            Child = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto"),
                RowSpacing = 10,
                Children =
                {
                    heading,
                    source.WithGridRow(1),
                    notice.WithGridRow(2),
                    _editor.WithGridRow(3),
                    buttons.WithGridRow(4)
                }
            }
        };
    }
}

internal static class GridPlacementExtensions
{
    public static T WithGridRow<T>(this T control, int row) where T : Control
    {
        Grid.SetRow(control, row);
        return control;
    }
}
