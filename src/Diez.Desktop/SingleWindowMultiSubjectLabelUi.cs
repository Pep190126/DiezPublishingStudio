using System.Reflection;
using Avalonia.Controls;
using Avalonia.Threading;

namespace DiezPublishingStudio;

/// <summary>
/// Keeps the one existing native subject TextBox, label and visible value synchronized with the optional
/// structured multi-subject mode. The current structured model is authoritative whenever the active SubjectId
/// changes, so switching subjects restores the description belonging to that exact stable identity.
/// Also suppresses the legacy generic character-consistency row while per-subject Consistent is active.
/// </summary>
internal static class SingleWindowMultiSubjectLabelUi
{
    private static readonly HashSet<MainWindow> Attached = [];
    private static readonly HashSet<Control> Wired = [];
    private static readonly HashSet<TextBox> Applying = [];

    public static void Attach(MainWindow window)
    {
        if (!Attached.Add(window)) return;
        var host = SingleWindowEntryPointUi.GetHost(window);
        var pageHost = host.GetType().GetField("_pageHost", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host) as ContentControl;
        if (pageHost is null) return;
        pageHost.PropertyChanged += (_, e) =>
        {
            if (e.Property != ContentControl.ContentProperty) return;
            Dispatcher.UIThread.Post(() => Refresh(window), DispatcherPriority.Loaded);
            Dispatcher.UIThread.Post(() => Refresh(window), DispatcherPriority.Background);
        };
        window.Closed += (_, _) => Attached.Remove(window);
        Refresh(window);
    }

    public static void Refresh(MainWindow window)
    {
        if (!TrySession(window, out var project)) return;
        var host = SingleWindowEntryPointUi.GetHost(window);
        var pageHost = host.GetType().GetField("_pageHost", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host) as ContentControl;
        if (pageHost?.Content is not Control page) return;
        var model = MultiSubjectProfileService.Load(project);

        var subject = Descendants(page).OfType<TextBox>().FirstOrDefault(x => x.Name == "VisualSubjectInstructions");
        if (subject is not null)
        {
            var label = Descendants(page).OfType<TextBlock>().FirstOrDefault(x =>
                (x.Text ?? string.Empty).StartsWith("Personaggio/i, soggetto/i", StringComparison.Ordinal) ||
                string.Equals(x.Text, "Tema / gruppo di soggetti", StringComparison.Ordinal) ||
                (x.Text ?? string.Empty).StartsWith("Descrizione — ", StringComparison.Ordinal));
            if (label is not null)
            {
                var current = MultiSubjectProfileService.ActiveSubject(model);
                Applying.Add(subject);
                try
                {
                    if (model.Enabled && current is not null)
                    {
                        label.Text = "Descrizione — " + current.Name;
                        subject.Watermark = "Descrivi solo questo soggetto/personaggio: aspetto, segni distintivi, età/proporzioni, caratteristiche da mantenere. Facoltativo.";
                        if (!string.Equals(subject.Text, current.Description, StringComparison.Ordinal))
                            subject.Text = current.Description;
                    }
                    else
                    {
                        label.Text = "Tema / gruppo di soggetti";
                        subject.Watermark = "Es. animali della giungla, fiori tropicali, piante grasse, dinosauri, veicoli. Usa gruppi/temi, non una lista di personaggi singoli.";
                        var group = model.GroupDescription ?? string.Empty;
                        if (!string.Equals(subject.Text, group, StringComparison.Ordinal))
                            subject.Text = group;
                    }
                }
                finally
                {
                    Applying.Remove(subject);
                }
            }
        }

        ApplyConsistencyVisibility(page, model.Enabled);

        var enabled = Descendants(page).OfType<CheckBox>().FirstOrDefault(x => x.Name == "MultiSubjectEnabled");
        if (enabled is not null && Wired.Add(enabled))
            enabled.IsCheckedChanged += (_, _) => Dispatcher.UIThread.Post(() => Refresh(window), DispatcherPriority.Background);
        var selector = Descendants(page).OfType<ComboBox>().FirstOrDefault(x => x.Name == "MultiSubjectSelector");
        if (selector is not null && Wired.Add(selector))
            selector.SelectionChanged += (_, _) => Dispatcher.UIThread.Post(() => Refresh(window), DispatcherPriority.Background);
        var name = Descendants(page).OfType<TextBox>().FirstOrDefault(x => x.Name == "MultiSubjectName");
        if (name is not null && Wired.Add(name))
            name.LostFocus += (_, _) => Dispatcher.UIThread.Post(() => Refresh(window), DispatcherPriority.Background);
        var consistent = Descendants(page).OfType<CheckBox>().FirstOrDefault(x => x.Name == "NativeConsistent");
        if (consistent is not null && Wired.Add(consistent))
            consistent.IsCheckedChanged += (_, _) => Dispatcher.UIThread.Post(() => Refresh(window), DispatcherPriority.Background);
    }

    public static bool IsApplying(TextBox? subject) => subject is not null && Applying.Contains(subject);

    private static void ApplyConsistencyVisibility(Control page, bool multiEnabled)
    {
        var panel = Descendants(page).OfType<StackPanel>().FirstOrDefault(x => x.Name == "DiezConsistencyCriteriaPanel");
        if (panel is null) return;
        var characterLevel = Descendants(panel).OfType<ComboBox>().FirstOrDefault(x => x.Name == "ConsistencyLevel_character");
        if (characterLevel is null) return;

        var directBlock = panel.Children
            .OfType<Control>()
            .FirstOrDefault(child => ReferenceEquals(child, characterLevel) || Descendants(child).Any(x => ReferenceEquals(x, characterLevel)));
        if (directBlock is not null)
            directBlock.IsVisible = !multiEnabled;
        else
            characterLevel.IsVisible = !multiEnabled;
    }

    private static bool TrySession(MainWindow window, out PreviewProject project)
    {
        project = typeof(MainWindow).GetField("_project", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(window) as PreviewProject ?? null!;
        return project is not null;
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        var stack = new Stack<Control>();
        var seen = new HashSet<Control>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!seen.Add(current)) continue;
            yield return current;
            switch (current)
            {
                case Panel p:
                    for (var i = p.Children.Count - 1; i >= 0; i--) stack.Push(p.Children[i]);
                    break;
                case Border b when b.Child is Control child: stack.Push(child); break;
                case ScrollViewer s when s.Content is Control child: stack.Push(child); break;
                case ContentControl c when c.Content is Control child: stack.Push(child); break;
            }
        }
    }
}
