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
        if (control is TabItem currentTab && string.Equals(currentTab.Header?.ToString(), "Puzzle", StringComparison.Ordinal))
            currentTab.Header = "Tipo libro";

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
            foreach (var item in tabs) Rename(item);
    }
}
