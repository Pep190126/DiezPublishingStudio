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
    private const string StrategyUser = "USER";
    private const string StrategyAi = "AI";
    private const string StrategyMixed = "MIXED";
    private static readonly Dictionary<MainWindow, CriteriaState> States = [];

    private static readonly LevelChoice[] Levels =
    [
        new("LOCKED", "Da mantenere"),
        new("PREFERRED", "Preferibilmente coerente"),
        new("FREE", "Può variare")
    ];

    private static readonly StrategyChoice[] Strategies =
    [
        new(StrategyUser, "La definisco io"),
        new(StrategyAi, "La decide l’AI"),
        new(StrategyMixed, "Mista: do indicazioni e l’AI completa")
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
                ? "Per ogni criterio scegli quanto deve essere vincolante. Nel Coloring la resa cromatica resta sempre nero/bianco puro. Con “Può variare” puoi decidere tu, lasciare decidere l’AI oppure dare indicazioni che l’AI completerà."
                : "Per ogni criterio scegli quanto deve essere vincolante. Con “Può variare” puoi definire tu la variazione, affidarla all’AI oppure usare una modalità mista.",
            TextWrapping = TextWrapping.Wrap
        });

        var allLocked = new CheckBox
        {
            Content = "Tutto coerente — mantieni tutti i criteri",
            IsChecked = criteria.Where(c => !c.Fixed).All(c => state.Levels.TryGetValue(c.Key, out var v) && v == "LOCKED")
        };
        panel.Children.Add(allLocked);

        var combos = new Dictionary<string, ComboBox>(StringComparer.Ordinal);
        var strategyCombos = new Dictionary<string, ComboBox>(StringComparer.Ordinal);
        var strategyLabels = new Dictionary<string, TextBlock>(StringComparer.Ordinal);
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

            var strategyLabel = new TextBlock
            {
                Text = "Chi decide come può variare?",
                FontSize = 14,
                IsVisible = !criterion.Fixed && string.Equals(level, "FREE", StringComparison.Ordinal)
            };
            strategyLabels[criterion.Key] = strategyLabel;

            var strategy = new ComboBox
            {
                Name = "ConsistencyVariationStrategy_" + criterion.Key,
                ItemsSource = Strategies,
                Width = 360,
                HorizontalAlignment = HorizontalAlignment.Left,
                IsVisible = !criterion.Fixed && string.Equals(level, "FREE", StringComparison.Ordinal)
            };
            var selectedStrategy = state.Strategies.TryGetValue(criterion.Key, out var strategyValue)
                ? NormalizeStrategy(strategyValue)
                : StrategyUser;
            state.Strategies[criterion.Key] = selectedStrategy;
            strategy.SelectedItem = Strategies.First(x => x.Strategy == selectedStrategy);
            strategyCombos[criterion.Key] = strategy;

            var variation = new TextBox
            {
                Name = "ConsistencyVariation_" + criterion.Key,
                Text = state.Variations.TryGetValue(criterion.Key, out var description) ? description : string.Empty,
                AcceptsReturn = true,
                MinHeight = 74,
                TextWrapping = TextWrapping.Wrap,
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
            ApplyVariationWatermark(variation, selectedStrategy, criterion.Label);
            variationBoxes[criterion.Key] = variation;

            if (!criterion.Fixed)
            {
                combo.SelectionChanged += (_, _) =>
                {
                    if (combo.SelectedItem is not LevelChoice choice) return;
                    state.Levels[criterion.Key] = choice.Level;
                    var free = choice.Level == "FREE";
                    strategyLabel.IsVisible = free;
                    strategy.IsVisible = free;
                    variation.IsVisible = free;
                    if (!changing)
                    {
                        changing = true;
                        allLocked.IsChecked = criteria.Where(c => !c.Fixed).All(c => state.Levels.TryGetValue(c.Key, out var v) && v == "LOCKED");
                        changing = false;
                    }
                    WriteRules(host, state, criteria, coloring);
                    UpdateNextValidity(next, state, criteria);
                };

                strategy.SelectionChanged += (_, _) =>
                {
                    if (strategy.SelectedItem is not StrategyChoice choice) return;
                    state.Strategies[criterion.Key] = choice.Strategy;
                    ApplyVariationWatermark(variation, choice.Strategy, criterion.Label);
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
                    strategyLabel,
                    strategy,
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
                strategyLabels[criterion.Key].IsVisible = false;
                strategyCombos[criterion.Key].IsVisible = false;
                variationBoxes[criterion.Key].IsVisible = false;
            }
            if (coloring) state.Levels[PaletteKey] = "LOCKED";
            changing = false;
            WriteRules(host, state, criteria, coloring);
            UpdateNextValidity(next, state, criteria);
        };
        return panel;
    }

    private static void ApplyVariationWatermark(TextBox box, string strategy, string criterionLabel)
    {
        box.Watermark = NormalizeStrategy(strategy) switch
        {
            StrategyAi => $"Facoltativo: dai all’AI preferenze o limiti per “{criterionLabel}”. Se lasci vuoto, l’AI decide liberamente entro le altre regole Consistent.",
            StrategyMixed => $"Obbligatorio: indica cosa vuoi guidare per “{criterionLabel}”; l’AI completerà ciò che non specifichi.",
            _ => $"Obbligatorio: descrivi tu cosa può variare e come per “{criterionLabel}”."
        };
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
            .Where(c => !string.Equals(GetStrategy(state, c.Key), StrategyAi, StringComparison.Ordinal))
            .Where(c => !state.Variations.TryGetValue(c.Key, out var text) || string.IsNullOrWhiteSpace(text))
            .Select(c => c.Label)
            .ToList();
        next.IsEnabled = missing.Count == 0;
        ToolTip.SetTip(next, missing.Count == 0
            ? null
            : "Descrivi cosa può variare e come per i criteri definiti da te o in modalità mista: " + string.Join(", ", missing));
    }

    private static void LoadFromHostIfNeeded(object host, CriteriaState state, Criterion[] criteria, bool coloring)
    {
        if (state.LoadedFor == (coloring ? "coloring" : "images")) return;
        state.LoadedFor = coloring ? "coloring" : "images";
        state.Levels.Clear();
        state.Variations.Clear();
        state.Strategies.Clear();
        foreach (var criterion in criteria)
        {
            state.Levels[criterion.Key] = criterion.Fixed ? "LOCKED" : criterion.DefaultLevel;
            state.Strategies[criterion.Key] = StrategyUser;
        }

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
                        if (choice.Level == "FREE") ParseFreeRule(value, state, criterion.Key);
                    }
                }
                else if (line.StartsWith("Note:", StringComparison.OrdinalIgnoreCase))
                    state.Notes = line[5..].Trim();
            }
        }
        if (coloring) state.Levels[PaletteKey] = "LOCKED";
    }

    private static void ParseFreeRule(string value, CriteriaState state, string key)
    {
        if (value.Contains("chi decide: AI", StringComparison.OrdinalIgnoreCase)) state.Strategies[key] = StrategyAi;
        else if (value.Contains("chi decide: MISTA", StringComparison.OrdinalIgnoreCase)) state.Strategies[key] = StrategyMixed;
        else if (value.Contains("chi decide: UTENTE", StringComparison.OrdinalIgnoreCase)) state.Strategies[key] = StrategyUser;
        else state.Strategies[key] = StrategyUser;

        var markers = new[] { "— variazione:", "— indicazioni:", "— indicazioni facoltative:" };
        foreach (var marker in markers)
        {
            var index = value.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                state.Variations[key] = value[(index + marker.Length)..].Trim();
                return;
            }
        }

        // Retrocompatibilità: il vecchio formato era “Può variare — descrizione”.
        var separator = value.IndexOf('—');
        if (separator >= 0 && separator + 1 < value.Length && !value[(separator + 1)..].Contains("chi decide:", StringComparison.OrdinalIgnoreCase))
            state.Variations[key] = value[(separator + 1)..].Trim();
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
            if (level != "FREE") return $"{c.Label}: {label}";

            var strategy = GetStrategy(state, c.Key);
            var decision = strategy switch
            {
                StrategyAi => "AI",
                StrategyMixed => "MISTA",
                _ => "UTENTE"
            };
            var variation = state.Variations.TryGetValue(c.Key, out var text) ? text.Trim() : string.Empty;
            if (string.IsNullOrWhiteSpace(variation))
                return $"{c.Label}: {label} — chi decide: {decision}";
            var detailLabel = strategy == StrategyUser ? "variazione" : strategy == StrategyAi ? "indicazioni facoltative" : "indicazioni";
            return $"{c.Label}: {label} — chi decide: {decision} — {detailLabel}: {variation}";
        }).ToList();
        if (!string.IsNullOrWhiteSpace(state.Notes)) lines.Add("Note: " + state.Notes.Trim());
        SetHostRules(host, string.Join(Environment.NewLine, lines));
    }

    private static string GetStrategy(CriteriaState state, string key) =>
        state.Strategies.TryGetValue(key, out var value) ? NormalizeStrategy(value) : StrategyUser;

    private static string NormalizeStrategy(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        StrategyAi => StrategyAi,
        StrategyMixed => StrategyMixed,
        _ => StrategyUser
    };

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
        public Dictionary<string, string> Strategies { get; } = new(StringComparer.Ordinal);
    }

    private sealed record Criterion(string Key, string Label, string DefaultLevel, bool Fixed);
    private sealed record LevelChoice(string Level, string Label)
    {
        public override string ToString() => Label;
    }
    private sealed record StrategyChoice(string Strategy, string Label)
    {
        public override string ToString() => Label;
    }
}
