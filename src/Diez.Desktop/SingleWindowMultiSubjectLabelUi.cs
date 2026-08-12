using System.Reflection;
using Avalonia.Controls;
using Avalonia.Threading;

namespace DiezPublishingStudio;

/// <summary>
/// Keeps the one existing native subject TextBox and its label synchronized with the optional
/// structured multi-subject mode. This deliberately locates the native label by semantic text instead
/// of relying on Avalonia Parent timing, which may be unset while decorators are attached.
/// </summary>
internal static class SingleWindowMultiSubjectLabelUi
{
    private static readonly HashSet<MainWindow> Attached = [];
    private static readonly HashSet<Control> Wired = [];

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
        var subject = Descendants(page).OfType<TextBox>().FirstOrDefault(x => x.Name == "VisualSubjectInstructions");
        if (subject is null) return;

        var label = Descendants(page).OfType<TextBlock>().FirstOrDefault(x =>
            (x.Text ?? string.Empty).StartsWith("Personaggio/i, soggetto/i", StringComparison.Ordinal) ||
            string.Equals(x.Text, "Tema / gruppo di soggetti", StringComparison.Ordinal) ||
            (x.Text ?? string.Empty).StartsWith("Descrizione — ", StringComparison.Ordinal));
        if (label is null) return;

        var model = MultiSubjectProfileService.Load(project);
        var current = MultiSubjectProfileService.ActiveSubject(model);
        if (model.Enabled && current is not null)
        {
            label.Text = "Descrizione — " + current.Name;
            subject.Watermark = "Descrivi solo questo soggetto/personaggio: aspetto, segni distintivi, età/proporzioni, caratteristiche da mantenere. Facoltativo.";
        }
        else
        {
            label.Text = "Tema / gruppo di soggetti";
            subject.Watermark = "Es. animali della giungla, fiori tropicali, piante grasse, dinosauri, veicoli. Usa gruppi/temi, non una lista di personaggi singoli.";
        }

        var enabled = Descendants(page).OfType<CheckBox>().FirstOrDefault(x => x.Name == "MultiSubjectEnabled");
        if (enabled is not null && Wired.Add(enabled))
            enabled.IsCheckedChanged += (_, _) => Dispatcher.UIThread.Post(() => Refresh(window), DispatcherPriority.Background);
        var selector = Descendants(page).OfType<ComboBox>().FirstOrDefault(x => x.Name == "MultiSubjectSelector");
        if (selector is not null && Wired.Add(selector))
            selector.SelectionChanged += (_, _) => Dispatcher.UIThread.Post(() => Refresh(window), DispatcherPriority.Background);
        var name = Descendants(page).OfType<TextBox>().FirstOrDefault(x => x.Name == "MultiSubjectName");
        if (name is not null && Wired.Add(name))
            name.LostFocus += (_, _) => Dispatcher.UIThread.Post(() => Refresh(window), DispatcherPriority.Background);
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
