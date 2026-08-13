using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace DiezPublishingStudio;

/// <summary>
/// Adds two independent bidirectional HARD Coloring parameters: Bold & Easy and Cozy.
/// Visual Style stays a separate single-choice dimension. Thin/fine line weights force Bold & Easy OFF;
/// Cozy remains independently selectable ON/OFF with either thin or thick line work.
/// </summary>
internal static class SingleWindowColoringStylePolicyUi
{
    private const string BoldControlName = "ColoringBoldEasyHard";
    private const string BoldStatusName = "ColoringBoldEasyHardStatus";
    private const string CozyControlName = "ColoringCozyHard";
    private const string CozyStatusName = "ColoringCozyHardStatus";

    private const string BoldOnLabel = "ON — Bold & Easy HARD";
    private const string BoldOffLabel = "OFF — No Bold & Easy HARD";
    private const string CozyOnLabel = "ON — Cozy HARD";
    private const string CozyOffLabel = "OFF — No Cozy HARD";

    private static readonly HashSet<MainWindow> Attached = [];
    private static readonly HashSet<Control> Wired = [];

    public static void Attach(MainWindow window)
    {
        if (!Attached.Add(window)) return;
        object host;
        try { host = SingleWindowEntryPointUi.GetHost(window); }
        catch { return; }
        var pageHost = host.GetType().GetField("_pageHost", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host) as ContentControl;
        if (pageHost is not null)
        {
            pageHost.PropertyChanged += (_, e) =>
            {
                if (e.Property != ContentControl.ContentProperty) return;
                Dispatcher.UIThread.Post(() => Refresh(window), DispatcherPriority.Loaded);
                Dispatcher.UIThread.Post(() => Refresh(window), DispatcherPriority.Background);
            };
        }
        window.Closed += (_, _) => Attached.Remove(window);
        Refresh(window);
    }

    public static void Refresh(MainWindow window)
    {
        if (!TrySession(window, out var project, out var path)) return;
        object host;
        try { host = SingleWindowEntryPointUi.GetHost(window); }
        catch { return; }
        var pageHost = host.GetType().GetField("_pageHost", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host) as ContentControl;
        if (pageHost?.Content is not Control page) return;

        // Multi-subject and Custom-style controls share this native-page refresh lifecycle.
        SingleWindowSubjectStyleUi.Refresh(window);

        if (!Descendants(page).Any(c => string.Equals(c.Name, "DiezNativeColoringProfile", StringComparison.Ordinal)))
        {
            SyncPersistedProfile(project, path);
            return;
        }

        var profilePanel = Descendants(page).OfType<StackPanel>().FirstOrDefault(p =>
            string.Equals(p.Name, "DiezNativeColoringProfile", StringComparison.Ordinal));
        var line = Descendants(page).OfType<ComboBox>().FirstOrDefault(c =>
            string.Equals(c.Name, "ColoringLineWeight", StringComparison.Ordinal));
        var style = Descendants(page).OfType<ComboBox>().FirstOrDefault(c =>
            string.Equals(c.Name, "ColoringStyle", StringComparison.Ordinal));
        if (profilePanel is null || line is null || style is null) return;

        var hard = ColoringIndependentHardProfileService.Resolve(project);
        var persisted = BookTypePromptProfileService.LoadColoring(project);
        var styles = ColoringIndependentHardProfileService.SelectableStyles;
        style.ItemsSource = styles;
        var selectedStyle = persisted.Style;
        if (string.Equals(selectedStyle, "Custom", StringComparison.OrdinalIgnoreCase))
            selectedStyle = "Custom";
        style.SelectedItem = styles.FirstOrDefault(x => string.Equals(x, selectedStyle, StringComparison.OrdinalIgnoreCase))
                             ?? "Clean Line Art";

        var bold = Descendants(page).OfType<ComboBox>().FirstOrDefault(c => string.Equals(c.Name, BoldControlName, StringComparison.Ordinal));
        var boldStatus = Descendants(page).OfType<TextBlock>().FirstOrDefault(t => string.Equals(t.Name, BoldStatusName, StringComparison.Ordinal));
        if (bold is null)
        {
            bold = new ComboBox
            {
                Name = BoldControlName,
                ItemsSource = new[] { BoldOnLabel, BoldOffLabel },
                SelectedItem = hard.BoldEasy ? BoldOnLabel : BoldOffLabel,
                Width = 310,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            boldStatus = new TextBlock { Name = BoldStatusName, TextWrapping = TextWrapping.Wrap, MaxWidth = 780 };
            InsertAfterStyle(profilePanel, style, new StackPanel
            {
                Name = "ColoringBoldEasyHardBlock",
                Spacing = 5,
                Children =
                {
                    new TextBlock { Text = "Bold & Easy — parametro indipendente HARD", FontSize = 14 },
                    bold,
                    boldStatus
                }
            });
        }

        var cozy = Descendants(page).OfType<ComboBox>().FirstOrDefault(c => string.Equals(c.Name, CozyControlName, StringComparison.Ordinal));
        var cozyStatus = Descendants(page).OfType<TextBlock>().FirstOrDefault(t => string.Equals(t.Name, CozyStatusName, StringComparison.Ordinal));
        if (cozy is null)
        {
            cozy = new ComboBox
            {
                Name = CozyControlName,
                ItemsSource = new[] { CozyOnLabel, CozyOffLabel },
                SelectedItem = hard.Cozy ? CozyOnLabel : CozyOffLabel,
                Width = 310,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            cozyStatus = new TextBlock { Name = CozyStatusName, TextWrapping = TextWrapping.Wrap, MaxWidth = 780 };
            var boldBlock = Descendants(profilePanel).OfType<StackPanel>()
                .FirstOrDefault(x => string.Equals(x.Name, "ColoringBoldEasyHardBlock", StringComparison.Ordinal));
            var index = boldBlock is null ? 2 : profilePanel.Children.IndexOf(boldBlock) + 1;
            profilePanel.Children.Insert(Math.Clamp(index, 0, profilePanel.Children.Count), new StackPanel
            {
                Name = "ColoringCozyHardBlock",
                Spacing = 5,
                Children =
                {
                    new TextBlock { Text = "Cozy — parametro indipendente HARD", FontSize = 14 },
                    cozy,
                    cozyStatus
                }
            });
        }

        ApplyConstraints(line, bold, boldStatus!, cozy, cozyStatus!);

        void Persist()
        {
            var lineWeight = line.SelectedItem?.ToString() ?? hard.LineWeight;
            var boldEnabled = string.Equals(bold.SelectedItem?.ToString(), BoldOnLabel, StringComparison.Ordinal);
            if (BookTypePromptProfileService.IsThinLineWeight(lineWeight)) boldEnabled = false;
            var cozyEnabled = string.Equals(cozy.SelectedItem?.ToString(), CozyOnLabel, StringComparison.Ordinal);
            ColoringIndependentHardProfileService.PersistResolvedState(
                project,
                style.SelectedItem?.ToString(),
                lineWeight,
                boldEnabled,
                cozyEnabled);
            _ = ProjectFileStore.SaveAsync(path, project);
        }

        if (Wired.Add(style))
            style.SelectionChanged += (_, _) => Persist();
        if (Wired.Add(bold))
            bold.SelectionChanged += (_, _) =>
            {
                ApplyConstraints(line, bold, boldStatus!, cozy, cozyStatus!);
                Persist();
            };
        if (Wired.Add(cozy))
            cozy.SelectionChanged += (_, _) =>
            {
                ApplyConstraints(line, bold, boldStatus!, cozy, cozyStatus!);
                Persist();
            };
        if (Wired.Add(line))
            line.SelectionChanged += (_, _) =>
            {
                ApplyConstraints(line, bold, boldStatus!, cozy, cozyStatus!);
                Persist();
            };

        // Custom HARD text + explicit library consent are part of the same native style profile.
        // Rebind them after the final style catalog/selection has been applied above.
        SingleWindowCustomStyleConsentUi.Refresh(window);
        SingleWindowSubjectStyleUi.Refresh(window);
    }

    private static void ApplyConstraints(
        ComboBox line,
        ComboBox bold,
        TextBlock boldStatus,
        ComboBox cozy,
        TextBlock cozyStatus)
    {
        var lineWeight = line.SelectedItem?.ToString() ?? string.Empty;
        var thin = BookTypePromptProfileService.IsThinLineWeight(lineWeight);
        if (thin)
        {
            if (!string.Equals(bold.SelectedItem?.ToString(), BoldOffLabel, StringComparison.Ordinal))
                bold.SelectedItem = BoldOffLabel;
            bold.IsEnabled = false;
            boldStatus.Text = "HARD OFF: linee Sottile/Fine o Molto sottile/Extra Fine vietano Bold & Easy. Il renderer non può ispessire né semplificare la tavola in Bold & Easy.";
        }
        else
        {
            bold.IsEnabled = true;
            boldStatus.Text = string.Equals(bold.SelectedItem?.ToString(), BoldOnLabel, StringComparison.Ordinal)
                ? "HARD ON: Bold & Easy deve essere visibilmente rispettato oltre allo stile selezionato."
                : "HARD OFF: niente semplificazione o ispessimento automatico Bold & Easy; stile, spessore, complessità e densità restano autoritativi.";
        }

        cozy.IsEnabled = true;
        cozyStatus.Text = string.Equals(cozy.SelectedItem?.ToString(), CozyOnLabel, StringComparison.Ordinal)
            ? "HARD ON: la pagina deve risultare visibilmente Cozy, oltre allo stile selezionato."
            : "HARD OFF: il renderer non deve trasformare automaticamente atmosfera, scena o decorazioni in una resa Cozy.";
    }

    private static void InsertAfterStyle(StackPanel root, Control style, Control block)
    {
        var styleContainer = FindDirectContainer(root, style);
        var index = styleContainer is null ? 2 : root.Children.IndexOf(styleContainer) + 1;
        root.Children.Insert(Math.Clamp(index, 0, root.Children.Count), block);
    }

    private static Control? FindDirectContainer(StackPanel root, Control descendant)
    {
        foreach (var child in root.Children.OfType<Control>())
            if (ReferenceEquals(child, descendant) || Descendants(child).Any(c => ReferenceEquals(c, descendant)))
                return child;
        return null;
    }

    private static void SyncPersistedProfile(PreviewProject project, string path)
    {
        if (!string.Equals(BookTypeProfileService.Get(project), BookTypeProfileService.ColoringBook, StringComparison.OrdinalIgnoreCase)) return;
        var hard = ColoringIndependentHardProfileService.Resolve(project);
        var p = BookTypePromptProfileService.LoadColoring(project);
        // Do not feed the resolved Custom definition back through the style selector; preserve Style=Custom + definition.
        var selected = string.Equals(p.Style, "Custom", StringComparison.OrdinalIgnoreCase) ? "Custom" : hard.Style;
        ColoringIndependentHardProfileService.PersistResolvedState(project, selected, hard.LineWeight, hard.BoldEasy, hard.Cozy);
        _ = ProjectFileStore.SaveAsync(path, project);
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
                case Panel panel:
                    for (var i = panel.Children.Count - 1; i >= 0; i--) stack.Push(panel.Children[i]);
                    break;
                case Border border when border.Child is Control child: stack.Push(child); break;
                case ScrollViewer scroll when scroll.Content is Control child: stack.Push(child); break;
                case ContentControl content when content.Content is Control child: stack.Push(child); break;
            }
        }
    }
}
