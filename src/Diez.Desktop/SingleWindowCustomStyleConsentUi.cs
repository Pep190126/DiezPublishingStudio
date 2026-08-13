using System.Reflection;
using Avalonia.Controls;
using Avalonia.Threading;

namespace DiezPublishingStudio;

/// <summary>
/// Owns the native Custom-style editor contract without relying on Avalonia Parent timing.
/// Custom text is always the project's HARD style authority; reuse across future projects happens only
/// after explicit consent for the exact current definition.
/// </summary>
internal static class SingleWindowCustomStyleConsentUi
{
    private const string ConsentName = "ColoringSaveCustomStyle";
    private static readonly HashSet<MainWindow> Attached = [];
    private static readonly HashSet<Control> Wired = [];
    private static readonly HashSet<Control> Applying = [];

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
        if (!TrySession(window, out var project, out var path)) return;
        var host = SingleWindowEntryPointUi.GetHost(window);
        var pageHost = host.GetType().GetField("_pageHost", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host) as ContentControl;
        if (pageHost?.Content is not Control page) return;

        var custom = Descendants(page).OfType<TextBox>().FirstOrDefault(x => x.Name == "ColoringCustomStyleNotes");
        var style = Descendants(page).OfType<ComboBox>().FirstOrDefault(x => x.Name == "ColoringStyle");
        if (custom is null || style is null) return;

        var container = Descendants(page).OfType<StackPanel>()
            .FirstOrDefault(p => p.Children.OfType<Control>().Any(x => ReferenceEquals(x, custom)));
        if (container is null) return;

        var label = container.Children.OfType<TextBlock>().FirstOrDefault();
        if (label is not null) label.Text = "Stile Custom — descrizione HARD";
        custom.Watermark = "Descrivi lo stile visivo esatto da rispettare. Questo testo diventa STYLE — HARD LOCK.";

        var consent = Descendants(container).OfType<CheckBox>().FirstOrDefault(x => x.Name == ConsentName);
        if (consent is null)
        {
            consent = new CheckBox
            {
                Name = ConsentName,
                Content = "Salva questo stile tra i miei stili personalizzati"
            };
            container.Children.Add(consent);
        }

        var selectable = ColoringIndependentHardProfileService.SelectableStyles.ToArray();
        var currentSelection = style.SelectedItem?.ToString() ?? string.Empty;
        style.ItemsSource = selectable;
        if (!selectable.Contains(currentSelection, StringComparer.OrdinalIgnoreCase))
        {
            var persisted = BookTypePromptProfileService.LoadColoring(project);
            currentSelection = string.Equals(persisted.Style, "Custom", StringComparison.OrdinalIgnoreCase)
                ? "Custom"
                : selectable.FirstOrDefault(x => string.Equals(x, persisted.Style, StringComparison.OrdinalIgnoreCase)) ?? "Clean Line Art";
            Applying.Add(style);
            try { style.SelectedItem = selectable.FirstOrDefault(x => string.Equals(x, currentSelection, StringComparison.OrdinalIgnoreCase)) ?? "Clean Line Art"; }
            finally { Applying.Remove(style); }
        }

        ApplyVisibility(style, custom, consent, container);

        if (Wired.Add(style))
        {
            style.SelectionChanged += async (_, _) =>
            {
                if (Applying.Contains(style)) return;
                var selected = style.SelectedItem?.ToString() ?? "Clean Line Art";
                var profile = BookTypePromptProfileService.LoadColoring(project);
                if (CustomStyleLibraryService.TryResolve(selected, out var definition))
                {
                    profile.Style = "Custom";
                    profile.CustomStyleNotes = definition;
                    Applying.Add(custom);
                    try { custom.Text = definition; }
                    finally { Applying.Remove(custom); }
                }
                else
                {
                    profile.Style = BookTypePromptProfileService.NormalizeColoringStyle(selected);
                }
                BookTypePromptProfileService.SaveColoring(project, profile);
                await SafeProjectAutosave.SaveAsync(path, project, "custom-style-selection");
                ApplyVisibility(style, custom, consent, container);
            };
        }

        if (Wired.Add(custom))
        {
            custom.GotFocus += (_, _) =>
            {
                // Consent applies to an exact definition. Editing invalidates consent before TextChanged.
                if (consent.IsChecked == true) consent.IsChecked = false;
            };
            custom.TextChanged += async (_, _) =>
            {
                if (Applying.Contains(custom)) return;
                var selected = style.SelectedItem?.ToString() ?? string.Empty;
                var customSelected = string.Equals(selected, "Custom", StringComparison.OrdinalIgnoreCase) ||
                                     CustomStyleLibraryService.TryResolve(selected, out _);
                if (!customSelected) return;

                var profile = BookTypePromptProfileService.LoadColoring(project);
                profile.Style = "Custom";
                profile.CustomStyleNotes = custom.Text ?? string.Empty;
                BookTypePromptProfileService.SaveColoring(project, profile);

                if (!string.Equals(selected, "Custom", StringComparison.OrdinalIgnoreCase))
                {
                    Applying.Add(style);
                    try { style.SelectedItem = style.ItemsSource?.Cast<object>().FirstOrDefault(x => string.Equals(x?.ToString(), "Custom", StringComparison.OrdinalIgnoreCase)); }
                    finally { Applying.Remove(style); }
                }
                await SafeProjectAutosave.SaveAsync(path, project, "custom-style-hard-definition");
            };
        }

        if (Wired.Add(consent))
        {
            consent.IsCheckedChanged += (_, _) =>
            {
                if (consent.IsChecked != true) return;
                var definition = (custom.Text ?? string.Empty).Trim();
                if (definition.Length == 0)
                {
                    consent.IsChecked = false;
                    return;
                }
                CustomStyleLibraryService.Add(definition);
                // Refresh only the catalog; keep the current project selection as Custom and its full HARD text.
                var values = ColoringIndependentHardProfileService.SelectableStyles.ToArray();
                style.ItemsSource = values;
                if (style.SelectedItem is null)
                {
                    Applying.Add(style);
                    try { style.SelectedItem = values.FirstOrDefault(x => string.Equals(x, "Custom", StringComparison.OrdinalIgnoreCase)); }
                    finally { Applying.Remove(style); }
                }
            };
        }
    }

    private static void ApplyVisibility(ComboBox style, TextBox custom, CheckBox consent, StackPanel container)
    {
        var selected = style.SelectedItem?.ToString() ?? string.Empty;
        var reusable = CustomStyleLibraryService.TryResolve(selected, out _);
        var customSelected = string.Equals(selected, "Custom", StringComparison.OrdinalIgnoreCase) || reusable;
        container.IsVisible = customSelected;
        custom.IsVisible = customSelected;
        consent.IsVisible = string.Equals(selected, "Custom", StringComparison.OrdinalIgnoreCase);
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
