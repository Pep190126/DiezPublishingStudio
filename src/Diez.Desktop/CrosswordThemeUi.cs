using System.Reflection;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace DiezPublishingStudio;

internal static class CrosswordThemeUi
{
    private static readonly HashSet<TabItem> Attached = [];

    public static void Attach(MainWindow window)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += (_, _) =>
        {
            var project = TryGetProject(window);
            if (project is null || !BookTypeProfileService.IsCrossword(project)) return;
            foreach (var tab in Descendants(window).OfType<TabItem>()
                         .Where(t => string.Equals(t.Header?.ToString(), "Liste speciali", StringComparison.Ordinal)))
            {
                if (!Attached.Add(tab)) continue;
                tab.Content = Build(window, project);
            }
        };
        window.Closed += (_, _) => timer.Stop();
        timer.Start();
    }

    private static Control Build(MainWindow window, PreviewProject project)
    {
        var list = new ListBox { MinHeight = 280 };
        var status = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var search = new TextBox { Width = 220, Watermark = "Cerca parola" };
        List<GraphEntity> visible = [];

        void Refresh(string? message = null)
        {
            var filter = (search.Text ?? string.Empty).Trim();
            visible = CrosswordService.Words(project)
                .Where(w => filter.Length == 0 || w.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .ToList();
            list.ItemsSource = visible.Select(w => CrosswordThemeService.DecoratedLabel(project, w)).ToList();
            var required = CrosswordThemeService.ByRole(project, CrosswordThemeService.Required).Count;
            var preferred = CrosswordThemeService.ByRole(project, CrosswordThemeService.Preferred).Count;
            var fallback = CrosswordThemeService.ByRole(project, CrosswordThemeService.Fallback).Count;
            status.Text = message ?? $"Obbligatorie: {required:N0} · Preferite: {preferred:N0} · Soccorso: {fallback:N0}. Le altre parole restano normali.";
        }

        async Task SetSelectedRole(string role)
        {
            if (list.SelectedIndex < 0 || list.SelectedIndex >= visible.Count)
            {
                status.Text = "Seleziona prima una parola.";
                return;
            }
            var word = visible[list.SelectedIndex];
            CrosswordThemeService.SetRole(project, word.EntityId, role);
            await SaveCurrentAsync(window, project);
            Refresh($"{word.Name}: {role}.");
        }

        async Task ImportThemeList(string role)
        {
            var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = role == CrosswordThemeService.Required ? "Importa parole obbligatorie del tema" : "Importa parole preferite del tema",
                AllowMultiple = true,
                FileTypeFilter = [new FilePickerFileType("Lista parole TXT") { Patterns = ["*.txt"] }]
            });
            if (files.Count == 0) return;
            var touched = 0;
            foreach (var file in files)
            {
                var lines = await File.ReadAllLinesAsync(file.Path.LocalPath, Encoding.UTF8);
                foreach (var line in lines)
                {
                    var normalized = CrosswordService.NormalizeGridWord(line);
                    if (normalized.Length < 2) continue;
                    var word = CrosswordService.EnsureWord(project, normalized, Path.GetFileName(file.Path.LocalPath));
                    CrosswordThemeService.SetRole(project, word.EntityId, role);
                    touched++;
                }
            }
            await SaveCurrentAsync(window, project);
            Refresh($"{touched:N0} parole impostate come {role.ToLowerInvariant()}.");
        }

        var find = new Button { Content = "Cerca", Width = 80 };
        find.Click += (_, _) => Refresh();
        var requiredButton = new Button { Content = "Obbligatoria", MinWidth = 115 };
        requiredButton.Click += async (_, _) => await SetSelectedRole(CrosswordThemeService.Required);
        var preferredButton = new Button { Content = "Preferita", MinWidth = 100 };
        preferredButton.Click += async (_, _) => await SetSelectedRole(CrosswordThemeService.Preferred);
        var normalButton = new Button { Content = "Normale", MinWidth = 90 };
        normalButton.Click += async (_, _) => await SetSelectedRole(CrosswordThemeService.Normal);
        var fallbackButton = new Button { Content = "Soccorso", MinWidth = 90 };
        fallbackButton.Click += async (_, _) => await SetSelectedRole(CrosswordThemeService.Fallback);
        var importRequired = new Button { Content = "Importa obbligatorie…", MinWidth = 155 };
        importRequired.Click += async (_, _) => await ImportThemeList(CrosswordThemeService.Required);
        var importPreferred = new Button { Content = "Importa preferite…", MinWidth = 145 };
        importPreferred.Click += async (_, _) => await ImportThemeList(CrosswordThemeService.Preferred);

        Refresh();
        return new Grid
        {
            Margin = new Thickness(10),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*"),
            RowSpacing = 8,
            Children =
            {
                new TextBlock
                {
                    Text = "Parole del tema e priorità",
                    FontSize = 19,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 7,
                    Children = { search, find, requiredButton, preferredButton, normalButton, fallbackButton }
                }.WithGridRow(1),
                new StackPanel
                {
                    Spacing = 5,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "Obbligatoria = deve entrare nella griglia. Preferita = Diez prova a usarla prima. Soccorso = resta disponibile quando serve per chiudere gli incroci.",
                            TextWrapping = Avalonia.Media.TextWrapping.Wrap
                        },
                        new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, Children = { importRequired, importPreferred } },
                        status
                    }
                }.WithGridRow(2),
                list.WithGridRow(3)
            }
        };
    }

    private static PreviewProject? TryGetProject(MainWindow window) =>
        typeof(MainWindow).GetField("_project", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(window) as PreviewProject;

    private static string? CurrentPath(MainWindow window) =>
        typeof(MainWindow).GetField("_currentProjectPath", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(window) as string;

    private static async Task SaveCurrentAsync(MainWindow window, PreviewProject project)
    {
        var path = CurrentPath(window);
        if (string.IsNullOrWhiteSpace(path)) return;
        await ProjectFileStore.SaveAsync(path, project);
    }

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
