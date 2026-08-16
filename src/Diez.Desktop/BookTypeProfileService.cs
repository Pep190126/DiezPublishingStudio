using System.Reflection;
using Avalonia.Controls;
using Avalonia.Threading;

namespace DiezPublishingStudio;

/// <summary>
/// Avalonia-only bridge for the framework-wide BookTypeProfileService.
/// The book-type contract itself lives in Diez.Core.
/// </summary>
internal static class BookTypeProfileUi
{
    private static readonly HashSet<RadioButton> AttachedChoices = [];
    private static string? _pendingChoice;
    private static bool _saving;

    public static void Attach(MainWindow window)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        timer.Tick += async (_, _) =>
        {
            EnsureAdditionalChoices(window);
            foreach (var radio in Descendants(window).OfType<RadioButton>()
                         .Where(r => string.Equals(r.GroupName, "project-type", StringComparison.Ordinal)))
            {
                if (!AttachedChoices.Add(radio)) continue;
                radio.IsCheckedChanged += (_, _) =>
                {
                    if (radio.IsChecked != true) return;
                    _pendingChoice = radio.Content?.ToString();
                    SetGuideProjectType(window, _pendingChoice);
                };
            }

            if (_saving || string.IsNullOrWhiteSpace(_pendingChoice) || !TrySession(window, out var project, out var path)) return;
            var normalized = BookTypeProfileService.Normalize(_pendingChoice);
            if (string.IsNullOrWhiteSpace(normalized)) return;
            if (string.Equals(BookTypeProfileService.Get(project), normalized, StringComparison.OrdinalIgnoreCase))
            {
                _pendingChoice = null;
                return;
            }

            _saving = true;
            try
            {
                BookTypeProfileService.Set(project, normalized);
                await ProjectFileStore.SaveAsync(path, project);
                _pendingChoice = null;
            }
            finally { _saving = false; }
        };
        window.Closed += (_, _) => timer.Stop();
        timer.Start();
    }

    private static void EnsureAdditionalChoices(MainWindow window)
    {
        foreach (var panel in Descendants(window).OfType<StackPanel>())
        {
            var choices = panel.Children.OfType<RadioButton>()
                .Where(r => string.Equals(r.GroupName, "project-type", StringComparison.Ordinal))
                .ToList();
            if (choices.Count == 0) continue;
            AddChoice(panel, choices, BookTypeProfileService.ImageCollection);
            choices = panel.Children.OfType<RadioButton>().Where(r => string.Equals(r.GroupName, "project-type", StringComparison.Ordinal)).ToList();
            AddChoice(panel, choices, BookTypeProfileService.EssayManual);
            choices = panel.Children.OfType<RadioButton>().Where(r => string.Equals(r.GroupName, "project-type", StringComparison.Ordinal)).ToList();
            AddChoice(panel, choices, BookTypeProfileService.Crossword);
        }
    }

    private static void AddChoice(StackPanel panel, IReadOnlyList<RadioButton> choices, string value)
    {
        if (choices.Any(r => string.Equals(r.Content?.ToString(), value, StringComparison.Ordinal))) return;
        panel.Children.Add(new RadioButton
        {
            Content = value,
            GroupName = "project-type",
            IsChecked = false
        });
    }

    private static void SetGuideProjectType(MainWindow window, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var guide = Descendants(window).OfType<PublisherGuideView>().FirstOrDefault();
        if (guide is null) return;
        typeof(PublisherGuideView).GetField("_projectType", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(guide, value);
    }

    private static bool TrySession(MainWindow window, out PreviewProject project, out string path)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        project = typeof(MainWindow).GetField("_project", flags)?.GetValue(window) as PreviewProject ?? null!;
        path = typeof(MainWindow).GetField("_currentProjectPath", flags)?.GetValue(window) as string ?? string.Empty;
        return project is not null && !string.IsNullOrWhiteSpace(path);
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
