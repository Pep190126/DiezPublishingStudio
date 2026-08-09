using System.Reflection;
using Avalonia.Controls;
using Avalonia.Threading;

namespace DiezPublishingStudio;

internal static class ImageCollectionWorkspaceTabsUi
{
    public static void Attach(MainWindow window)
    {
        TabControl? host = null;
        List<TabItem>? bookTabs = null;
        List<object?>? originalContents = null;
        ImageCollectionTabWorkspace? workspace = null;
        var imageMode = false;
        var refreshing = false;

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += async (_, _) =>
        {
            if (host is null || bookTabs is null)
            {
                host = FindBookTabs(window, out bookTabs);
                if (host is null || bookTabs is null) return;
                originalContents = bookTabs.Select(t => t.Content).ToList();
            }

            var project = TryGetProject(window);
            var shouldUseImages = project is not null && IsImageCollection(project);
            if (shouldUseImages && !imageMode)
            {
                workspace ??= new ImageCollectionTabWorkspace(window);
                var contents = workspace.Contents;
                for (var i = 0; i < bookTabs.Count && i < contents.Count; i++)
                {
                    bookTabs[i].Header = i switch
                    {
                        0 => "Database",
                        1 => "Tipo libro",
                        2 => "Controlli",
                        3 => "AI",
                        _ => "Esporta"
                    };
                    bookTabs[i].Content = contents[i];
                    bookTabs[i].IsVisible = true;
                }
                imageMode = true;
            }
            else if (!shouldUseImages && imageMode)
            {
                if (originalContents is not null)
                    for (var i = 0; i < bookTabs.Count && i < originalContents.Count; i++)
                        bookTabs[i].Content = originalContents[i];
                bookTabs[1].Header = "Tipo libro";
                var showWordSearch = project is not null && BookTypeRecognition.IsWordSearch(project);
                foreach (var tab in bookTabs) tab.IsVisible = showWordSearch;
                imageMode = false;
            }

            if (!shouldUseImages || workspace is null || refreshing) return;
            refreshing = true;
            try { await workspace.RefreshAsync(); }
            finally { refreshing = false; }
        };
        window.Closed += (_, _) => timer.Stop();
        timer.Start();
    }

    private static bool IsImageCollection(PreviewProject project)
    {
        if (BookTypeProfileService.IsImageCollection(project)) return true;
        var imageJobs = project.AiProductionJobs.Count(j => string.Equals(j.OutputType, AiProductionService.TypeImage, StringComparison.OrdinalIgnoreCase));
        var nonImageJobs = project.AiProductionJobs.Count - imageJobs;
        return imageJobs >= 2 && nonImageJobs == 0 && project.ContentNodes.Count == 0;
    }

    private static TabControl? FindBookTabs(Control root, out List<TabItem>? bookTabs)
    {
        foreach (var tabControl in Descendants(root).OfType<TabControl>())
        {
            if (tabControl.ItemsSource is not IEnumerable<TabItem> source) continue;
            var items = source.ToList();
            if (items.Count < 6 || !string.Equals(items[0].Header?.ToString(), "Progetto", StringComparison.Ordinal)) continue;
            bookTabs = items.Skip(1).Take(5).ToList();
            return tabControl;
        }
        bookTabs = null;
        return null;
    }

    private static PreviewProject? TryGetProject(MainWindow window) =>
        typeof(MainWindow).GetField("_project", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(window) as PreviewProject;

    private static IEnumerable<Control> Descendants(Control root)
    {
        yield return root;
        if (root is Panel panel)
            foreach (var child in panel.Children.SelectMany(Descendants)) yield return child;
        if (root is Border border && border.Child is Control borderChild)
            foreach (var child in Descendants(borderChild)) yield return child;
        if (root is ScrollViewer scroll && scroll.Content is Control scrollChild)
            foreach (var child in Descendants(scrollChild)) yield return child;
        if (root is ContentControl content && content.Content is Control contentChild)
            foreach (var child in Descendants(contentChild)) yield return child;
    }
}
