using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace DiezPublishingStudio;

internal static class SingleWindowConsistencyCriteriaUi
{
    private const string PanelName = "DiezConsistencyCriteriaPanel";
    private const string PaletteKey = "palette";
    private static readonly Dictionary<MainWindow, CriteriaState> States = [];

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
        pageHost.PropertyChanged += (_, e) =>
        {
            if (e.Property == ContentControl.ContentProperty) EnsureCurrentPage(window);
        };
        window.Closed += (_, _) => States.Remove(window);
        EnsureCurrentPage(window);
    }

    internal static void EnsureCurrentPage(MainWindow window)
    {
        if (!States.TryGetValue(window, out var state) || !TrySession(window, out var project)) return;
        var host = SingleWindowEntryPointUi.GetHost(window);
        var pageHost = host.GetType().GetField("_pageHost", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host) as ContentControl;
        if (pageHost?.Content is not Control page) return;
        if (!Descendants(page).OfType<TextBlock>().Any(t => (t.Text ?? string.Empty).Contains("Quante immagini vuoi creare?", StringComparison.Ordinal))) return;
        if (Descendants(page).Any(c => string.Equals(c.Name, PanelName, StringComparison.Ordinal))) return;

        var isColoring = string.Equals(BookTypeProfileService.Get(project), BookTypeProfileService.ColoringBook, StringComparison.OrdinalIgnoreCase);
        var criteria = BuildCriteria(isColoring);
        var consistent = Descendants(page).OfType<CheckBox>().FirstOrDefault(c =>
            (c.Content?.ToString() ?? string.Empty).StartsWith("Consistent", StringComparison.OrdinalIgnoreCase));
        if (consistent is null || consistent.Parent is not StackPanel parent) return;

        LoadFromHostIfNeeded(host, state, criteria, isColoring);
        if (isColoring) state.Levels[PaletteKey] = "LOCKED";

        var index = parent.Children.IndexOf(consistent);
        if (index < 0) return;
        if (index + 1 < parent.Children.Count && parent.Children[index + 1] is TextBox legacyRules)
            legacyRules.IsVisible = false;

        var next = Descendants(page).OfType<Button>().FirstOrDefault(b =>
            (b.Content?.ToString() ?? string.Empty).Contains("Avanti", StringComparison.OrdinalIgnoreCase));
        var panel = BuildCriteriaPanel(host, state, criteria, isColoring, next);
        parent.Children.Insert(index + 1, panel);
        panel.IsVisible = consistent.IsChecked == true;

        consistent.IsCheckedChanged += (_, _) =>
        {
            panel.IsVisible = consistent.IsChecked == true;
            state.Enabled = consistent.IsChecked == true;
            if (isColoring) state.Levels[PaletteKey] = "LOCKED";
            WriteRules(host, state, criteria, isColoring);
            UpdateNextValidity(next, state, criteria);
        };

        state.Enabled = consistent.IsChecked == true;
        WriteRules(host, state, criteria, isColoring);
        UpdateNextValidity(next, state, criteria);
    }

    private static Criterion[] BuildCriteria(bool coloring) =>
    [
        new("character", "Personaggio / soggetto ricorrente", "LOCKED", false),
        new("style", "Stile", "LOCKED", false),
        new(PaletteKey, coloring ? "Palette / colori — fissa B/N" : "Resa cromatica / palette", coloring ? "LOCKED" : "PREFERRED", coloring),
        new("line_detail", "Tratto / dettaglio", "LOCKED", false),
        new("environment_objects", "Ambientazioni / oggetti ricorrenti", "PREFERRED", false),
        new("composition", "Composizione / inquadratura", "PREFERRED", false)
    ];

    private static StackPanel BuildCriteriaPanel(object host, CriteriaState state, Criterion[] criteria, bool coloring, Button? next)
    {
        var panel = new StackPanel
        {
            Name = PanelName,
            Spacing = 9,
            Margin = new Thickness(14, 4, 0, 6)
        };
        panel.Children.Add(new TextBlock { Text = "Quali aspetti devono restare coerenti?", FontSize = 17 });
        panel.Children.Add(new TextBlock
        {
            Text = coloring
                ? "Per ogni criterio scegli quanto deve essere vincolante. Nel Coloring la resa cromatica resta sempre nero/bianco puro. Se scegli “Può variare”, descrivi obbligatoriamente cosa può cambiare e come."
                : "Per ogni criterio scegli quanto deve essere vincolante. Se scegli “Può variare”, descrivi obbligatoriamente cosa può cambiare e come: questa diventa una libertà controllata per l'AI.",
            TextWrapping = TextWrapping.Wrap
        });

        var allLocked = new CheckBox
        {
            Content = "Tutto coerente — mantieni tutti i criteri variabili",
            IsChecked = criteria.Where(c => !c.Fixed).All(c => state.Levels.TryGetValue(c.Key, out var v) && v == "LOCKED")
        };
        panel.Children.Add(allLocked);

        var combos = new Dictionary<string, ComboBox>(StringComparer.Ordinal);
        var variationBoxes = new Dictionary<string, TextBox>(StringComparer.Ordinal);
        var changing = false;

        foreach (var criterion in criteria)
        {
            var combo = new ComboBox
            {
                Name = "ConsistencyLevel_" + criterion.Key,
                ItemsSource = criterion.Fixed ? new[] { Levels[0] } : Levels,
                Width = 230,
                HorizontalAlignment = HorizontalAlignment.Left,
                IsEnabled = !criterion.Fixed
            };
            var level = criterion.Fixed ? "LOCKED" : state.Levels.TryGetValue(criterion.Key, out var saved) ? saved : criterion.DefaultLevel;
            combo.SelectedItem = criterion.Fixed ? Levels[0] : Levels.First(x => x.Level == level);
            combos[criterion.Key] = combo;

            var variation = new TextBox
            {
                Name = "ConsistencyVariation_" + criterion.Key,
                Text = state.Variations.TryGetValue(criterion.Key, out var description) ? description : string.Empty,
                AcceptsReturn = true,
                MinHeight = 74,
                TextWrapping = TextWrapping.Wrap,
                Watermark = $"Cosa può variare e come per “{criterion.Label}”? Esempio: lo sfondo cambia per ogni tavola, ma personaggio, tratto e proporzioni restano invariati.",
                IsReadOnly = false,
                IsEnabled = true,
                IsHitTestVisible = true,
                Focusable = true,
                IsUndoEnabled = true,
                Background = Brushes.White,
                Foreground = Brushes.Black,
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(2),
                Padding = new Thickness(8, 6),
                IsVisible = !criterion.Fixed && string.Equals(level, "FREE", StringComparison.Ordinal)
            };
            variationBoxes[criterion.Key] = variation;

            if (!criterion.Fixed)
            {
                combo.SelectionChanged += (_, _) =>
                {
                    if (combo.SelectedItem is not LevelChoice choice) return;
                    state.Levels[criterion.Key] = choice.Level;
                    variation.IsVisible = choice.Level == "FREE";
                    if (!changing)
                    {
                        changing = true;
                        allLocked.IsChecked = criteria.Where(c => !c.Fixed).All(c => state.Levels.TryGetValue(c.Key, out var v) && v == "LOCKED");
                        changing = false;
                    }
                    WriteRules(host, state, criteria, coloring);
                    UpdateNextValidity(next, state, criteria);
                };

                variation.TextChanged += (_, _) =>
                {
                    state.Variations[criterion.Key] = variation.Text ?? string.Empty;
                    WriteRules(host, state, criteria, coloring);
                    UpdateNextValidity(next, state, criteria);
                };
            }

            panel.Children.Add(new StackPanel
            {
                Spacing = 5,
                Children =
                {
                    new Grid
                    {
                        ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                        ColumnSpacing = 10,
                        Children =
                        {
                            new TextBlock { Text = criterion.Label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap },
                            combo.WithGridColumn(1)
                        }
                    },
                    variation
                }
            });
        }

        var notes = new TextBox
        {
            Name = "ConsistencyNotes",
            Text = state.Notes,
            AcceptsReturn = true,
            MinHeight = 82,
            TextWrapping = TextWrapping.Wrap,
            Watermark = "Note generali facoltative sulla coerenza della serie.",
            IsReadOnly = false,
            IsEnabled = true,
            IsUndoEnabled = true,
            Background = Brushes.White,
            Foreground = Brushes.Black,
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(2),
            Padding = new Thickness(8, 6)
        };
        notes.TextChanged += (_, _) =>
        {
            state.Notes = notes.Text ?? string.Empty;
            WriteRules(host, state, criteria, coloring);
        };
        panel.Children.Add(new TextBlock { Text = "Note generali di coerenza (facoltative)" });
        panel.Children.Add(notes);

        allLocked.IsCheckedChanged += (_, _) =>
        {
            if (changing || allLocked.IsChecked != true) return;
            changing = true;
            foreach (var criterion in criteria.Where(c => !c.Fixed))
            {
                state.Levels[criterion.Key] = "LOCKED";
                combos[criterion.Key].SelectedItem = Levels[0];
                variationBoxes[criterion.Key].IsVisible = false;
            }
            if (coloring) state.Levels[PaletteKey] = "LOCKED";
            changing = false;
            WriteRules(host, state, criteria, coloring);
            UpdateNextValidity(next, state, criteria);
        };
        return panel;
    }

    private static void UpdateNextValidity(Button? next, CriteriaState state, Criterion[] criteria)
    {
        if (next is null) return;
        if (!state.Enabled)
        {
            next.IsEnabled = true;
            ToolTip.SetTip(next, null);
            return;
        }
        var missing = criteria
            .Where(c => !c.Fixed && state.Levels.TryGetValue(c.Key, out var level) && level == "FREE")
            .Where(c => !state.Variations.TryGetValue(c.Key, out var text) || string.IsNullOrWhiteSpace(text))
            .Select(c => c.Label)
            .ToList();
        next.IsEnabled = missing.Count == 0;
        ToolTip.SetTip(next, missing.Count == 0
            ? null
            : "Descrivi cosa può variare e come per: " + string.Join(", ", missing));
    }

    private static void LoadFromHostIfNeeded(object host, CriteriaState state, Criterion[] criteria, bool coloring)
    {
        if (state.LoadedFor == (coloring ? "coloring" : "images")) return;
        state.LoadedFor = coloring ? "coloring" : "images";
        state.Levels.Clear();
        state.Variations.Clear();
        foreach (var criterion in criteria) state.Levels[criterion.Key] = criterion.Fixed ? "LOCKED" : criterion.DefaultLevel;

        var rules = ReadHostRules(host);
        if (!string.IsNullOrWhiteSpace(rules))
        {
            foreach (var line in rules.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var criterion = criteria.FirstOrDefault(c => line.StartsWith(c.Label + ":", StringComparison.OrdinalIgnoreCase) ||
                    (c.Key == PaletteKey && line.StartsWith("Palette / colori:", StringComparison.OrdinalIgnoreCase)));
                if (criterion is not null && !criterion.Fixed)
                {
                    var value = line[(line.IndexOf(':') + 1)..].Trim();
                    var choice = Levels.FirstOrDefault(x => value.StartsWith(x.Label, StringComparison.OrdinalIgnoreCase));
                    if (choice is not null)
                    {
                        state.Levels[criterion.Key] = choice.Level;
                        if (choice.Level == "FREE")
                        {
                            var separator = value.IndexOf('—');
                            if (separator >= 0 && separator + 1 < value.Length)
                                state.Variations[criterion.Key] = value[(separator + 1)..].Trim();
                        }
                    }
                }
                else if (line.StartsWith("Note:", StringComparison.OrdinalIgnoreCase))
                    state.Notes = line[5..].Trim();
            }
        }
        if (coloring) state.Levels[PaletteKey] = "LOCKED";
    }

    private static void WriteRules(object host, CriteriaState state, Criterion[] criteria, bool coloring)
    {
        if (!state.Enabled)
        {
            SetHostRules(host, string.Empty);
            return;
        }
        if (coloring) state.Levels[PaletteKey] = "LOCKED";
        var lines = criteria.Select(c =>
        {
            if (c.Fixed && c.Key == PaletteKey)
                return "Palette / colori: Da mantenere — fisso nero puro #000000 e bianco puro #FFFFFF";
            var level = state.Levels.TryGetValue(c.Key, out var selected) ? selected : c.DefaultLevel;
            var label = Levels.First(x => x.Level == level).Label;
            if (level == "FREE" && state.Variations.TryGetValue(c.Key, out var variation) && !string.IsNullOrWhiteSpace(variation))
                return $"{c.Label}: {label} — {variation.Trim()}";
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

    private sealed class CriteriaState
    {
        public string LoadedFor { get; set; } = string.Empty;
        public bool Enabled { get; set; }
        public string Notes { get; set; } = string.Empty;
        public Dictionary<string, string> Levels { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> Variations { get; } = new(StringComparer.Ordinal);
    }

    private sealed record Criterion(string Key, string Label, string DefaultLevel, bool Fixed);
    private sealed record LevelChoice(string Level, string Label)
    {
        public override string ToString() => Label;
    }
}
