using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace DiezPublishingStudio;

/// <summary>
/// Adds the independent Bold & Easy HARD parameter to the native Coloring profile.
/// The visual style remains a separate single-choice control. Fine/thin line weights force
/// Bold & Easy OFF and disable the contradictory ON choice while those line weights are active.
/// </summary>
internal static class SingleWindowColoringStylePolicyUi
{
    private const string ControlName = "ColoringBoldEasyHard";
    private const string StatusName = "ColoringBoldEasyHardStatus";
    private static readonly HashSet<Control> WiredLineControls = [];
    private static readonly HashSet<Control> WiredBoldControls = [];

    private const string OnLabel = "ON — Bold & Easy HARD";
    private const string OffLabel = "OFF — No Bold & Easy HARD";

    public static void Refresh(MainWindow window)
    {
        if (!TrySession(window, out var project, out var path)) return;
        object host;
        try { host = SingleWindowEntryPointUi.GetHost(window); }
        catch { return; }
        var pageHost = host.GetType().GetField("_pageHost", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host) as ContentControl;
        if (pageHost?.Content is not Control page) return;

        // After leaving the quantity/profile page, re-synchronize the profile property from the independent
        // policy store. This prevents the native page's older captured profile object from overwriting a
        // freshly changed Bold & Easy value during its normal save sequence.
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

        // The style ComboBox already reads BookTypePromptProfileService.ColoringStyles, so the expanded
        // one-style-per-entry catalogue is automatically authoritative here.
        var bold = Descendants(page).OfType<ComboBox>().FirstOrDefault(c =>
            string.Equals(c.Name, ControlName, StringComparison.Ordinal));
        var status = Descendants(page).OfType<TextBlock>().FirstOrDefault(t =>
            string.Equals(t.Name, StatusName, StringComparison.Ordinal));

        if (bold is null)
        {
            var p = BookTypePromptProfileService.LoadColoring(project);
            var enabled = ColoringBoldEasyPolicyStore.Resolve(project, line.SelectedItem?.ToString() ?? p.LineWeight, p.BoldEasy);
            bold = new ComboBox
            {
                Name = ControlName,
                ItemsSource = new[] { OnLabel, OffLabel },
                SelectedItem = enabled ? OnLabel : OffLabel,
                Width = 290,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            status = new TextBlock
            {
                Name = StatusName,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 780
            };

            var block = new StackPanel
            {
                Name = "ColoringBoldEasyHardBlock",
                Spacing = 5,
                Children =
                {
                    new TextBlock { Text = "Bold & Easy — parametro indipendente HARD", FontSize = 14 },
                    bold,
                    status
                }
            };

            var styleContainer = FindDirectContainer(profilePanel, style);
            var insertIndex = styleContainer is null ? 2 : profilePanel.Children.IndexOf(styleContainer) + 1;
            profilePanel.Children.Insert(Math.Clamp(insertIndex, 0, profilePanel.Children.Count), block);
        }

        if (WiredBoldControls.Add(bold))
        {
            bold.SelectionChanged += async (_, _) =>
            {
                var currentLine = line.SelectedItem?.ToString() ?? string.Empty;
                var requested = string.Equals(bold.SelectedItem?.ToString(), OnLabel, StringComparison.Ordinal);
                if (BookTypePromptProfileService.IsThinLineWeight(currentLine)) requested = false;
                ColoringBoldEasyPolicyStore.Save(project, requested, currentLine);
                var p = BookTypePromptProfileService.LoadColoring(project);
                p.BoldEasy = requested;
                p.LineWeight = currentLine;
                BookTypePromptProfileService.SaveColoring(project, p);
                try { await ProjectFileStore.SaveAsync(path, project); } catch { }
                ApplyConstraint(project, line, bold, status!);
            };
        }

        if (WiredLineControls.Add(line))
        {
            line.SelectionChanged += async (_, _) =>
            {
                ApplyConstraint(project, line, bold, status!);
                var currentLine = line.SelectedItem?.ToString() ?? string.Empty;
                var enabled = string.Equals(bold.SelectedItem?.ToString(), OnLabel, StringComparison.Ordinal);
                ColoringBoldEasyPolicyStore.Save(project, enabled, currentLine);
                var p = BookTypePromptProfileService.LoadColoring(project);
                p.BoldEasy = enabled;
                p.LineWeight = currentLine;
                BookTypePromptProfileService.SaveColoring(project, p);
                try { await ProjectFileStore.SaveAsync(path, project); } catch { }
            };
        }

        ApplyConstraint(project, line, bold, status!);
    }

    private static void ApplyConstraint(PreviewProject project, ComboBox line, ComboBox bold, TextBlock status)
    {
        var lineWeight = line.SelectedItem?.ToString() ?? string.Empty;
        var thin = BookTypePromptProfileService.IsThinLineWeight(lineWeight);
        if (thin)
        {
            if (!string.Equals(bold.SelectedItem?.ToString(), OffLabel, StringComparison.Ordinal))
                bold.SelectedItem = OffLabel;
            bold.IsEnabled = false;
            status.Text = "HARD: con linee Sottile/Fine o Molto sottile/Extra Fine, Bold & Easy è forzato OFF. Il renderer non può ispessire o semplificare la tavola in stile Bold & Easy.";
            ColoringBoldEasyPolicyStore.Save(project, false, lineWeight);
        }
        else
        {
            bold.IsEnabled = true;
            status.Text = string.Equals(bold.SelectedItem?.ToString(), OnLabel, StringComparison.Ordinal)
                ? "HARD ON: Bold & Easy deve essere visibilmente rispettato oltre allo stile selezionato."
                : "HARD OFF: il renderer non deve applicare automaticamente la semplificazione Bold & Easy; stile, spessore, complessità e densità restano autoritativi.";
        }
    }

    private static Control? FindDirectContainer(StackPanel root, Control descendant)
    {
        foreach (var child in root.Children.OfType<Control>())
        {
            if (ReferenceEquals(child, descendant) || Descendants(child).Any(c => ReferenceEquals(c, descendant)))
                return child;
        }
        return null;
    }

    private static void SyncPersistedProfile(PreviewProject project, string path)
    {
        if (!ColoringBoldEasyPolicyStore.TryLoad(project, out var enabled)) return;
        if (!string.Equals(BookTypeProfileService.Get(project), BookTypeProfileService.ColoringBook, StringComparison.OrdinalIgnoreCase)) return;
        var p = BookTypePromptProfileService.LoadColoring(project);
        var effective = BookTypePromptProfileService.IsThinLineWeight(p.LineWeight) ? false : enabled;
        if (p.BoldEasy == effective) return;
        p.BoldEasy = effective;
        BookTypePromptProfileService.SaveColoring(project, p);
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
