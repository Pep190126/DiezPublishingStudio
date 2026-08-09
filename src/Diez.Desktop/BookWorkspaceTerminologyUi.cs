using Avalonia.Controls;

namespace DiezPublishingStudio;

internal static class BookWorkspaceTerminologyUi
{
    public static void Attach(MainWindow window)
    {
        if (window.Content is not Control root) return;
        Rename(root);
    }

    private static void Rename(Control control)
    {
        if (control is TabItem tab && string.Equals(tab.Header?.ToString(), "Puzzle", StringComparison.Ordinal))
            tab.Header = "Tipo libro";

        if (control is Panel panel)
        {
            foreach (var child in panel.Children.OfType<Control>()) Rename(child);
            return;
        }

        if (control is Border border && border.Child is Control borderChild)
        {
            Rename(borderChild);
            return;
        }

        if (control is ContentControl contentControl && contentControl.Content is Control contentChild)
            Rename(contentChild);

        if (control is ItemsControl itemsControl && itemsControl.ItemsSource is IEnumerable<TabItem> tabs)
            foreach (var tab in tabs) Rename(tab);
    }
}
