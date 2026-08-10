using System.Reflection;
using Avalonia.Controls;
using Avalonia.Layout;

namespace DiezPublishingStudio;

/// <summary>
/// Exposes the native Coloring Book profile: the user chooses how the coloring
/// pages should be drawn instead of relying on the words "Coloring Book" alone.
/// The same profile enriches generic and provider-specific prompts.
/// </summary>
internal static class SingleWindowColoringProfileUi
{
    private const string PanelName = "DiezColoringProfilePanel";

    public static void Attach(MainWindow window)
    {
        var host = SingleWindowEntryPointUi.GetHost(window);
        var pageHost = host.GetType().GetField("_pageHost", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host) as ContentControl;
        if (pageHost is null) return;
        pageHost.PropertyChanged += (_, e) =>
        {
            if (e.Property == ContentControl.ContentProperty) EnsureCurrentPage(window);
        };
        EnsureCurrentPage(window);
    }

    internal static void EnsureCurrentPage(MainWindow window)
    {
        var host = SingleWindowEntryPointUi.GetHost(window);
        var pageHost = host.GetType().GetField("_pageHost", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host) as ContentControl;
        if (pageHost?.Content is not Control page || !TrySession(window, out var project, out var path)) return;
        var texts = Descendants(page).OfType<TextBlock>().Select(t => t.Text ?? string.Empty).ToList();

        if (texts.Any(t => t.Contains("Quante immagini vuoi creare?", StringComparison.Ordinal)))
            EnsureQuantityProfile(page, project, path);
        else if (texts.Any(t => string.Equals(t, "PROMPT — modificabile", StringComparison.Ordinal)))
            EnsurePromptProfile(page, project);
    }

    private static void EnsureQuantityProfile(Control page, PreviewProject project, string path)
    {
        if (Descendants(page).Any(c => string.Equals(c.Name, PanelName, StringComparison.Ordinal))) return;
        if (!string.Equals(BookTypeProfileService.Get(project), BookTypeProfileService.ColoringBook, StringComparison.OrdinalIgnoreCase)) return;

        var root = Descendants(page).OfType<StackPanel>().FirstOrDefault(p =>
            p.Children.OfType<TextBlock>().Any(t => (t.Text ?? string.Empty).Contains("Coloring Book — quantità", StringComparison.Ordinal)));
        if (root is null) return;

        var profile = BookTypePromptProfileService.LoadColoring(project);
        var style = Combo("ColoringStyle", BookTypePromptProfileService.ColoringStyles, profile.Style, 270);
        var audience = Combo("ColoringAudience", BookTypePromptProfileService.TargetAudiences, profile.TargetAudience, 230);
        var difficulty = Combo("ColoringDifficulty", BookTypePromptProfileService.Difficulties, profile.Difficulty, 160);
        var lineWeight = Combo("ColoringLineWeight", BookTypePromptProfileService.LineWeights, profile.LineWeight, 160);
        var complexity = Combo("ColoringComplexity", BookTypePromptProfileService.Complexities, profile.Complexity, 160);
        var density = Combo("ColoringDensity", BookTypePromptProfileService.Densities, profile.ElementDensity, 160);
        var background = Combo("ColoringBackground", BookTypePromptProfileService.Backgrounds, profile.Background, 220);
        var whiteSpace = Combo("ColoringWhiteSpace", BookTypePromptProfileService.WhiteSpaces, profile.WhiteSpace, 160);

        var closed = Check("Aree chiuse e facili da colorare", profile.ClosedAreas);
        var noTiny = Check("Evita aree e dettagli minuscoli", profile.AvoidTinyAreas);
        var clean = Check("Contorni puliti e continui", profile.CleanContours);
        var bw = Check("Solo bianco e nero puro", profile.BlackAndWhiteOnly);
        var noGray = Check("Niente grigi / mezzetinte", profile.NoGray);
        var noShadow = Check("Niente ombre / sfumature", profile.NoShadows);
        var noText = Check("Niente testo o numeri nell'immagine", profile.NoTextInsideImage);
        var separate = Check("Soggetto ben separato dallo sfondo", profile.SubjectClearlySeparated);
        var notes = new TextBox
        {
            Name = "ColoringCustomStyleNotes",
            Text = profile.CustomStyleNotes,
            Height = 72,
            AcceptsReturn = true,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Watermark = "Note facoltative sullo stile, es. occhi grandi, bordi molto spessi, niente sfondo.",
            IsUndoEnabled = true
        };

        async Task SaveAsync()
        {
            profile.Style = style.SelectedItem?.ToString() ?? profile.Style;
            profile.TargetAudience = audience.SelectedItem?.ToString() ?? profile.TargetAudience;
            profile.Difficulty = difficulty.SelectedItem?.ToString() ?? profile.Difficulty;
            profile.LineWeight = lineWeight.SelectedItem?.ToString() ?? profile.LineWeight;
            profile.Complexity = complexity.SelectedItem?.ToString() ?? profile.Complexity;
            profile.ElementDensity = density.SelectedItem?.ToString() ?? profile.ElementDensity;
            profile.Background = background.SelectedItem?.ToString() ?? profile.Background;
            profile.WhiteSpace = whiteSpace.SelectedItem?.ToString() ?? profile.WhiteSpace;
            profile.ClosedAreas = closed.IsChecked == true;
            profile.AvoidTinyAreas = noTiny.IsChecked == true;
            profile.CleanContours = clean.IsChecked == true;
            profile.BlackAndWhiteOnly = bw.IsChecked == true;
            profile.NoGray = noGray.IsChecked == true;
            profile.NoShadows = noShadow.IsChecked == true;
            profile.NoTextInsideImage = noText.IsChecked == true;
            profile.SubjectClearlySeparated = separate.IsChecked == true;
            profile.CustomStyleNotes = notes.Text ?? string.Empty;
            BookTypePromptProfileService.SaveColoring(project, profile);
            await ProjectFileStore.SaveAsync(path, project);
        }

        foreach (var combo in new[] { style, audience, difficulty, lineWeight, complexity, density, background, whiteSpace })
            combo.SelectionChanged += async (_, _) => await SaveAsync();
        foreach (var check in new[] { closed, noTiny, clean, bw, noGray, noShadow, noText, separate })
            check.IsCheckedChanged += async (_, _) => await SaveAsync();
        notes.TextChanged += async (_, _) => await SaveAsync();

        style.SelectionChanged += (_, _) => ApplyStyleDefaults(profile, style.SelectedItem?.ToString() ?? profile.Style,
            difficulty, lineWeight, complexity, density, background, whiteSpace, closed, noTiny, clean, bw, noGray, noShadow, noText, separate);

        var panel = new StackPanel
        {
            Name = PanelName,
            Spacing = 8,
            Children =
            {
                new Separator(),
                new TextBlock { Text = "Stile e livello del Coloring", FontSize = 19 },
                new TextBlock
                {
                    Text = "Coloring Book è il tipo di libro; qui scegli come devono essere realmente disegnate le pagine.",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                },
                Labeled("Stile", style),
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { Labeled("Pubblico", audience), Labeled("Difficoltà", difficulty) } },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { Labeled("Spessore linea", lineWeight), Labeled("Complessità", complexity), Labeled("Densità", density) } },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { Labeled("Sfondo", background), Labeled("Spazio bianco", whiteSpace) } },
                new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 12, LineSpacing = 5, Children = { closed, noTiny, clean, bw, noGray, noShadow, noText, separate } },
                new TextBlock { Text = "Note stile (facoltative)", FontSize = 13 },
                notes
            }
        };

        var actions = root.Children.OfType<StackPanel>().LastOrDefault(p => p.Orientation == Orientation.Horizontal && p.Children.OfType<Button>().Any());
        var index = actions is null ? root.Children.Count : root.Children.IndexOf(actions);
        root.Children.Insert(Math.Max(0, index), panel);
        if (actions is not null)
        {
            var next = actions.Children.OfType<Button>().FirstOrDefault(b => (b.Content?.ToString() ?? string.Empty).Contains("Avanti", StringComparison.OrdinalIgnoreCase));
            if (next is not null) next.Click += async (_, _) => await SaveAsync();
        }
    }

    private static void EnsurePromptProfile(Control page, PreviewProject project)
    {
        if (Descendants(page).Any(c => string.Equals(c.Name, "DiezColoringPromptProfileMarker", StringComparison.Ordinal))) return;
        var editors = Descendants(page).OfType<TextBox>().Where(t => t.IsVisible && t.IsEnabled && !t.IsReadOnly).ToList();
        if (editors.Count < 3) return;
        var prompt = editors[2];
        var changing = false;

        void EnsureBlock()
        {
            if (changing) return;
            var text = prompt.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text) || text.Contains("PROFILO EDITORIALE COLORING BOOK:", StringComparison.Ordinal)) return;
            changing = true;
            prompt.Text = text.TrimEnd() + Environment.NewLine + Environment.NewLine + BookTypePromptProfileService.BuildBookTypeBlock(project);
            changing = false;
        }

        prompt.TextChanged += (_, _) => EnsureBlock();
        foreach (var button in Descendants(page).OfType<Button>().Where(b => (b.Content?.ToString() ?? string.Empty).StartsWith("Prepara prompt", StringComparison.OrdinalIgnoreCase)))
            button.Click += (_, _) => EnsureBlock();

        var marker = new TextBlock
        {
            Name = "DiezColoringPromptProfileMarker",
            Text = "Diez aggiunge automaticamente il profilo Coloring scelto (es. Bold & Easy / Line Art), difficoltà, tratto e regole di colorabilità.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        };
        var root = Descendants(page).OfType<StackPanel>().FirstOrDefault(p => p.Children.Contains(prompt));
        if (root is not null)
        {
            var idx = root.Children.IndexOf(prompt);
            root.Children.Insert(Math.Min(root.Children.Count, idx + 1), marker);
        }
    }

    private static void ApplyStyleDefaults(BookTypePromptProfileService.ColoringProfile profile, string style,
        ComboBox difficulty, ComboBox lineWeight, ComboBox complexity, ComboBox density, ComboBox background, ComboBox whiteSpace,
        CheckBox closed, CheckBox noTiny, CheckBox clean, CheckBox bw, CheckBox noGray, CheckBox noShadow, CheckBox noText, CheckBox separate)
    {
        if (style == "Bold & Easy")
        {
            difficulty.SelectedItem = "Facile"; lineWeight.SelectedItem = "Spesso"; complexity.SelectedItem = "Bassa";
            density.SelectedItem = "Bassa"; background.SelectedItem = "Semplice / minimo"; whiteSpace.SelectedItem = "Ampio";
            closed.IsChecked = true; noTiny.IsChecked = true;
        }
        else if (style == "Line Art pulita")
        {
            lineWeight.SelectedItem = "Medio"; complexity.SelectedItem = "Media"; density.SelectedItem = "Media";
            background.SelectedItem = "Contestuale leggero"; whiteSpace.SelectedItem = "Medio";
        }
        else if (style == "Line Art dettagliata")
        {
            difficulty.SelectedItem = "Impegnativa"; lineWeight.SelectedItem = "Sottile"; complexity.SelectedItem = "Alta";
            density.SelectedItem = "Alta"; background.SelectedItem = "Dettagliato"; whiteSpace.SelectedItem = "Compatto";
        }
        else if (style == "Kawaii / Cartoon")
        {
            difficulty.SelectedItem = "Facile"; lineWeight.SelectedItem = "Spesso"; complexity.SelectedItem = "Bassa";
            density.SelectedItem = "Bassa"; background.SelectedItem = "Semplice / minimo"; whiteSpace.SelectedItem = "Ampio";
        }
        else if (style == "Mandala / Pattern")
        {
            difficulty.SelectedItem = "Media"; lineWeight.SelectedItem = "Medio"; complexity.SelectedItem = "Alta";
            density.SelectedItem = "Alta"; background.SelectedItem = "Nessuno / bianco"; whiteSpace.SelectedItem = "Medio";
        }
        clean.IsChecked = true; bw.IsChecked = true; noGray.IsChecked = true; noShadow.IsChecked = true; noText.IsChecked = true; separate.IsChecked = true;
    }

    private static ComboBox Combo(string name, IEnumerable<string> values, string selected, double width) => new()
    {
        Name = name, ItemsSource = values.ToArray(), SelectedItem = selected, Width = width, HorizontalAlignment = HorizontalAlignment.Left
    };

    private static CheckBox Check(string label, bool value) => new() { Content = label, IsChecked = value };
    private static StackPanel Labeled(string label, Control control) => new() { Spacing = 3, Children = { new TextBlock { Text = label, FontSize = 13 }, control } };

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
