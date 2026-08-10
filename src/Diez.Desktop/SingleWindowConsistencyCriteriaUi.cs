using System.Reflection;
using Avalonia.Controls;
using Avalonia.Layout;

namespace DiezPublishingStudio;

/// <summary>
/// Adds explicit visual-consistency criteria to the single-window image workflow.
/// The criteria panel is hidden while Consistent is OFF and becomes visible when ON.
/// Values are compiled into the existing coloring Rules field so the current Prompt Pack
/// pipeline receives the user's effective consistency choices without a second UI model.
/// </summary>
internal static class SingleWindowConsistencyCriteriaUi
{
    private const string PanelName = "DiezConsistencyCriteriaPanel";
    private static readonly Dictionary<MainWindow, CriteriaState> States = [];

    private static readonly Criterion[] Criteria =
    [
        new("character", "Personaggio", "LOCKED"),
        new("style", "Stile", "LOCKED"),
        new("palette", "Palette / colori", "PREFERRED"),
        new("line_detail", "Tratto / dettaglio", "LOCKED"),
        new("environment_objects", "Ambientazioni / oggetti ricorrenti", "PREFERRED"),
        new("composition", "Composizione", "PREFERRED")
    ];

    private static readonly LevelChoice[] Levels =
    [
        new("LOCKED", "Da mantenere"),
        new("PREFERRED", "Preferibilmente coerente"),
        new("FREE", "Può variare")
    ];

    public static void Attach(MainWindow window)
    {
        if (States.ContainsKey(window)) return;
        States[window] = new CriteriaState();

        var host = SingleWindowEntryPointUi.GetHost(window);
        var pageHost = host.GetType().GetField("_pageHost", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host) as ContentControl;
        if (pageHost is null) return;

        // React only to the logical page replacement. Bounds/layout changes must not
        // rebuild or scan the criteria UI.
        pageHost.PropertyChanged += (_, e) =>
        {
            if (e.Property == ContentControl.ContentProperty)
                EnsureCurrentPage(window);
        };
        window.Closed += (_, _) => States.Remove(window);
        EnsureCurrentPage(window);
    }

    internal static void EnsureCurrentPage(MainWindow window)
    {
        if (!States.TryGetValue(window, out var state)) return;
        var host = SingleWindowEntryPointUi.GetHost(window);
        var pageHost = host.GetType().GetField("_pageHost", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host) as ContentControl;
        if (pageHost?.Content is not Control page) return;

        if (!Descendants(page).OfType<TextBlock>().Any(t =>
                (t.Text ?? string.Empty).Contains("Quante immagini vuoi creare?", StringComparison.Ordinal)))
            return;

        if (Descendants(page).Any(c => string.Equals(c.Name, PanelName, StringComparison.Ordinal))) return;

        var consistent = Descendants(page).OfType<CheckBox>().FirstOrDefault(c =>
            (c.Content?.ToString() ?? string.Empty).StartsWith("Consistent", StringComparison.OrdinalIgnoreCase));
        if (consistent is null || consistent.Parent is not StackPanel parent) return;

        LoadFromHostIfNeeded(host, state);

        var index = parent.Children.IndexOf(consistent);
        if (index < 0) return;

        // Hide the former generic free-text rules box. Its value remains the internal
        // transport field, now compiled from the explicit criteria below.
        if (index + 1 < parent.Children.Count && parent.Children[index + 1] is TextBox legacyRules)
            legacyRules.IsVisible = false;

        var panel = BuildCriteriaPanel(host, state);
        parent.Children.Insert(index + 1, panel);
        panel.IsVisible = consistent.IsChecked == true;

        consistent.IsCheckedChanged += (_, _) =>
        {
            panel.IsVisible = consistent.IsChecked == true;
            state.Enabled = consistent.IsChecked == true;
            WriteRules(host, state);
        };

        state.Enabled = consistent.IsChecked == true;
        WriteRules(host, state);
    }

    private static StackPanel BuildCriteriaPanel(object host, CriteriaState state)
    {
        var panel = new StackPanel
        {
            Name = PanelName,
            Spacing = 8,
            Margin = new Avalonia.Thickness(14, 4, 0, 6)
        };

        panel.Children.Add(new TextBlock
        {
            Text = "Quali aspetti devono restare coerenti?",
            FontSize = 17
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Per ogni criterio scegli quanto deve essere vincolante nella serie.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });

        var allLocked = new CheckBox
        {
            Content = "Tutto coerente — mantieni tutti i criteri",
            IsChecked = state.Levels.Values.All(v => string.Equals(v, "LOCKED", StringComparison.Ordinal))
        };
        panel.Children.Add(allLocked);

        var combos = new Dictionary<string, ComboBox>(StringComparer.Ordinal);
        var changing = false;

        foreach (var criterion in Criteria)
        {
            var combo = new ComboBox
            {
                Name = "ConsistencyLevel_" + criterion.Key,
                ItemsSource = Levels,
                Width = 230,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            var level = state.Levels.TryGetValue(criterion.Key, out var saved) ? saved : criterion.DefaultLevel;
            combo.SelectedItem = Levels.First(x => string.Equals(x.Level, level, StringComparison.Ordinal));
            combos[criterion.Key] = combo;

            combo.SelectionChanged += (_, _) =>
            {
                if (combo.SelectedItem is not LevelChoice choice) return;
                state.Levels[criterion.Key] = choice.Level;
                if (!changing)
                {
                    changing = true;
                    allLocked.IsChecked = state.Levels.Values.All(v => string.Equals(v, "LOCKED", StringComparison.Ordinal));
                    changing = false;
                }
                WriteRules(host, state);
            };

            panel.Children.Add(new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                ColumnSpacing = 10,
                Children =
                {
                    new TextBlock
                    {
                        Text = criterion.Label,
                        VerticalAlignment = VerticalAlignment.Center,
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap
                    },
                    combo.WithGridColumn(1)
                }
            });
        }

        var notes = new TextBox
        {
            Name = "ConsistencyNotes",
            Text = state.Notes,
            AcceptsReturn = true,
            Height = 82,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Watermark = "Note facoltative, es. stesso cappello rosso; sfondi liberi.",
            IsUndoEnabled = true
        };
        notes.TextChanged += (_, _) =>
        {
            state.Notes = notes.Text ?? string.Empty;
            WriteRules(host, state);
        };
        panel.Children.Add(new TextBlock { Text = "Note di coerenza (facoltative)" });
        panel.Children.Add(notes);

        allLocked.IsCheckedChanged += (_, _) =>
        {
            if (changing || allLocked.IsChecked != true) return;
            changing = true;
            foreach (var criterion in Criteria)
            {
                state.Levels[criterion.Key] = "LOCKED";
                combos[criterion.Key].SelectedItem = Levels[0];
            }
            changing = false;
            WriteRules(host, state);
        };

        return panel;
    }

    private static void LoadFromHostIfNeeded(object host, CriteriaState state)
    {
        if (state.Loaded) return;
        state.Loaded = true;
        foreach (var criterion in Criteria) state.Levels[criterion.Key] = criterion.DefaultLevel;

        var rules = ReadHostRules(host);
        if (string.IsNullOrWhiteSpace(rules)) return;
        foreach (var line in rules.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var criterion = Criteria.FirstOrDefault(c => line.StartsWith(c.Label + ":", StringComparison.OrdinalIgnoreCase));
            if (criterion is not null)
            {
                var label = line[(line.IndexOf(':') + 1)..].Trim();
                var choice = Levels.FirstOrDefault(x => string.Equals(x.Label, label, StringComparison.OrdinalIgnoreCase));
                if (choice is not null) state.Levels[criterion.Key] = choice.Level;
            }
            else if (line.StartsWith("Note:", StringComparison.OrdinalIgnoreCase))
            {
                state.Notes = line[5..].Trim();
            }
        }
    }

    private static void WriteRules(object host, CriteriaState state)
    {
        if (!state.Enabled)
        {
            SetHostRules(host, string.Empty);
            return;
        }

        var lines = Criteria.Select(c =>
        {
            var level = state.Levels.TryGetValue(c.Key, out var selected) ? selected : c.DefaultLevel;
            var label = Levels.First(x => string.Equals(x.Level, level, StringComparison.Ordinal)).Label;
            return $"{c.Label}: {label}";
        }).ToList();
        if (!string.IsNullOrWhiteSpace(state.Notes)) lines.Add("Note: " + state.Notes.Trim());
        SetHostRules(host, string.Join(Environment.NewLine, lines));
    }

    private static string ReadHostRules(object host)
    {
        var coloring = host.GetType().GetField("_coloring", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host);
        return coloring?.GetType().GetProperty("Rules", BindingFlags.Instance | BindingFlags.Public)?.GetValue(coloring)?.ToString() ?? string.Empty;
    }

    private static void SetHostRules(object host, string value)
    {
        var coloring = host.GetType().GetField("_coloring", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host);
        coloring?.GetType().GetProperty("Rules", BindingFlags.Instance | BindingFlags.Public)?.SetValue(coloring, value);
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
                case Border b when b.Child is Control child:
                    stack.Push(child);
                    break;
                case ScrollViewer s when s.Content is Control child:
                    stack.Push(child);
                    break;
                case ContentControl c when c.Content is Control child:
                    stack.Push(child);
                    break;
            }
        }
    }

    private sealed class CriteriaState
    {
        public bool Loaded { get; set; }
        public bool Enabled { get; set; }
        public string Notes { get; set; } = string.Empty;
        public Dictionary<string, string> Levels { get; } = new(StringComparer.Ordinal);
    }

    private sealed record Criterion(string Key, string Label, string DefaultLevel);
    private sealed record LevelChoice(string Level, string Label)
    {
        public override string ToString() => Label;
    }
}
