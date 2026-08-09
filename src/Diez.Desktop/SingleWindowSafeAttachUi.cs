using System.Reflection;
using Avalonia.Controls;
using Avalonia.Layout;

namespace DiezPublishingStudio;

/// <summary>
/// Safe final attachment for the single-window host. It deliberately avoids a
/// recursive visual-tree scan: the project command row is found through the
/// stable FriendlyLayout structure (Border -> desktop Grid -> header Grid).
/// </summary>
internal static class SingleWindowSafeAttachUi
{
    public static void Attach(MainWindow window)
    {
        if (window.Content is not Control original) return;

        var row = FindProjectButtonRow(original);
        window.Content = null;
        var host = new SingleWindowBookFlowHost(window, original);
        window.Content = host;

        if (row is null) return;
        foreach (var old in row.Children.OfType<Button>())
        {
            var text = old.Content?.ToString() ?? string.Empty;
            if (text.Contains("Produzione AI", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("Contenuti con AI", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("Prompt Pack AI", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("Serie immagini AI", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("Correzione AI", StringComparison.OrdinalIgnoreCase))
                old.IsVisible = false;
        }

        if (row.Children.OfType<Button>().Any(b => string.Equals(b.Content?.ToString(), "Percorso libro", StringComparison.Ordinal))) return;
        var start = new Button
        {
            Content = "Percorso libro",
            Width = 150,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        ToolTip.SetTip(start, "Percorso progressivo nello stesso MainWindow, con Indietro e Anteprima.");
        start.Click += (_, _) =>
        {
            var method = typeof(SingleWindowBookFlowHost).GetMethod("OpenCurrentBook", BindingFlags.Instance | BindingFlags.NonPublic);
            method?.Invoke(host, null);
        };
        row.Children.Add(start);
    }

    private static StackPanel? FindProjectButtonRow(Control original)
    {
        if (original is not Border border || border.Child is not Grid desktop) return null;
        var header = desktop.Children.OfType<Grid>().FirstOrDefault(c => Grid.GetRow(c) == 0);
        if (header is null) return null;
        return header.Children.OfType<StackPanel>().FirstOrDefault(panel =>
            panel.Orientation == Orientation.Horizontal &&
            panel.Children.OfType<Button>().Any(b => string.Equals(b.Content?.ToString(), "Nuovo progetto", StringComparison.OrdinalIgnoreCase)));
    }
}
