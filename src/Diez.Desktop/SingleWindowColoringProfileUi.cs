using System.Reflection;
using Avalonia.Controls;
using Avalonia.Layout;

namespace DiezPublishingStudio;

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

        var subject = new TextBox
        {
            Name = "ColoringSubjectDescription",
            Text = profile.SubjectDescription,
            Height = 110,
            AcceptsReturn = true,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Watermark = "Scegli e descrivi il soggetto o i soggetti: chi/cosa sono, caratteristiche, età/aspetto, azione o posa, elementi obbligatori, variazioni ammesse.",
            IsUndoEnabled = true
        };
        var environment = new TextBox
        {
            Name = "ColoringEnvironmentDescription",
            Text = profile.EnvironmentDescription,
            Height = 110,
            AcceptsReturn = true,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Watermark = "Descrivi ambiente/scenario: luogo, sfondo, oggetti presenti, elementi ricorrenti, relazione col soggetto e ciò che deve o non deve comparire.",
            IsUndoEnabled = true
        };

        var style = Combo("ColoringStyle", BookTypePromptProfileService.ColoringStyles, profile.Style, 285);
        var audience = Combo("ColoringAudience", BookTypePromptProfileService.TargetAudiences, profile.TargetAudience, 235);
        var difficulty = Combo("ColoringDifficulty", BookTypePromptProfileService.Difficulties, profile.Difficulty, 170);
        var lineWeight = Combo("ColoringLineWeight", BookTypePromptProfileService.LineWeights, profile.LineWeight, 390);
        var complexity = Combo("ColoringComplexity", BookTypePromptProfileService.Complexities, profile.Complexity, 170);
        var density = Combo("ColoringDensity", BookTypePromptProfileService.Densities, profile.ElementDensity, 170);
        var background = Combo("ColoringBackground", BookTypePromptProfileService.Backgrounds, profile.Background, 225);
        var whiteSpace = Combo("ColoringWhiteSpace", BookTypePromptProfileService.WhiteSpaces, profile.WhiteSpace, 170);

        var closed = Check("Aree chiuse e facili da colorare", profile.ClosedAreas);
        var noTiny = Check("Evita aree e dettagli minuscoli", profile.AvoidTinyAreas);
        var clean = Check("Contorni puliti e continui", profile.CleanContours);
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
            profile.SubjectDescription = subject.Text ?? string.Empty;
            profile.EnvironmentDescription = environment.Text ?? string.Empty;
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
            profile.BlackAndWhiteOnly = true;
            profile.NoGray = true;
            profile.NoShadows = true;
            profile.NoTextInsideImage = noText.IsChecked == true;
            profile.SubjectClearlySeparated = separate.IsChecked == true;
            profile.CustomStyleNotes = notes.Text ?? string.Empty;
            BookTypePromptProfileService.SaveColoring(project, profile);
            await ProjectFileStore.SaveAsync(path, project);
        }

        subject.TextChanged += async (_, _) => await SaveAsync();
        environment.TextChanged += async (_, _) => await SaveAsync();
        foreach (var combo in new[] { style, audience, difficulty, lineWeight, complexity, density, background, whiteSpace })
            combo.SelectionChanged += async (_, _) => await SaveAsync();
        foreach (var check in new[] { closed, noTiny, clean, noText, separate })
            check.IsCheckedChanged += async (_, _) => await SaveAsync();
        notes.TextChanged += async (_, _) => await SaveAsync();

        style.SelectionChanged += (_, _) => ApplyStyleDefaults(style.SelectedItem?.ToString() ?? profile.Style,
            difficulty, lineWeight, complexity, density, background, whiteSpace, closed, noTiny, clean, noText, separate);

        var fixedColorRule = new Border
        {
            Name = "ColoringBinaryColorRule",
            Padding = new Avalonia.Thickness(10),
            Child = new TextBlock
            {
                Text = "Vincolo fisso Coloring: SOLO 2 COLORI — nero puro (#000000) e bianco puro (#FFFFFF). Nessun grigio, colore, ombra, sfumatura o valore intermedio.",
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            }
        };

        var checks = new StackPanel
        {
            Spacing = 4,
            Children = { closed, noTiny, clean, noText, separate }
        };

        var panel = new StackPanel
        {
            Name = PanelName,
            Spacing = 8,
            Children =
            {
                new Separator(),
                new TextBlock { Text = "Contenuto e stile del Coloring", FontSize = 19 },
                new TextBlock
                {
                    Text = "Descrivi prima ciò che vuoi vedere; Diez aggiunge poi le regole editoriali e tecniche del Coloring Book.",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                },
                new TextBlock { Text = "Soggetto/i — scelta e descrizione", FontSize = 15 },
                subject,
                new TextBlock { Text = "Ambiente / scenario — descrizione", FontSize = 15 },
                environment,
                fixedColorRule,
                Labeled("Stile", style),
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children = { Labeled("Pubblico", audience), Labeled("Difficoltà", difficulty) }
                },
                Labeled("Spessore linee", lineWeight),
                new TextBlock
                {
                    Text = "Lo spessore è indipendente dallo stile: anche una Line Art dettagliata può usare linee sottili o molto sottili, purché restino nere, nitide e stampabili.",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children = { Labeled("Complessità", complexity), Labeled("Densità", density) }
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children = { Labeled("Sfondo", background), Labeled("Spazio bianco", whiteSpace) }
                },
                checks,
                new TextBlock { Text = "Note stile (facoltative)", FontSize = 13 },
                notes
            }
        };

        var actions = root.Children.OfType<StackPanel>().LastOrDefault(p =>
            p.Orientation == Orientation.Horizontal && p.Children.OfType<Button>().Any());
        var index = actions is null ? root.Children.Count : root.Children.IndexOf(actions);
        root.Children.Insert(Math.Max(0, index), panel);
        if (actions is not null)
        {
            var next = actions.Children.OfType<Button>().FirstOrDefault(b =>
                (b.Content?.ToString() ?? string.Empty).Contains("Avanti", StringComparison.OrdinalIgnoreCase));
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
        foreach (var button in Descendants(page).OfType<Button>().Where(b =>
                     (b.Content?.ToString() ?? string.Empty).StartsWith("Prepara prompt", StringComparison.OrdinalIgnoreCase)))
            button.Click += (_, _) => EnsureBlock();

        var marker = new TextBlock
        {
            Name = "DiezColoringPromptProfileMarker",
            Text = "Diez aggiunge automaticamente soggetto/i, ambiente, stile Coloring, spessore linee, livello di dettaglio e il vincolo fisso a due soli colori: nero e bianco.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        };
        var root = Descendants(page).OfType<StackPanel>().FirstOrDefault(p => p.Children.Contains(prompt));
        if (root is not null)
        {
            var idx = root.Children.IndexOf(prompt);
            root.Children.Insert(Math.Min(root.Children.Count, idx + 1), marker);
        }
    }

    private static void ApplyStyleDefaults(string style,
        ComboBox difficulty, ComboBox lineWeight, ComboBox complexity, ComboBox density,
        ComboBox background, ComboBox whiteSpace, CheckBox closed, CheckBox noTiny,
        CheckBox clean, CheckBox noText, CheckBox separate)
    {
        if (style == "Bold & Easy")
        {
            difficulty.SelectedItem = "Facile";
            lineWeight.SelectedItem = "Molto spesso — Extra Bold";
            complexity.SelectedItem = "Bassa";
            density.SelectedItem = "Bassa";
            background.SelectedItem = "Semplice / minimo";
            whiteSpace.SelectedItem = "Ampio";
            closed.IsChecked = true;
            noTiny.IsChecked = true;
        }
        else if (style == "Line Art pulita")
        {
            lineWeight.SelectedItem = "Medio";
            complexity.SelectedItem = "Media";
            density.SelectedItem = "Media";
            background.SelectedItem = "Contestuale leggero";
            whiteSpace.SelectedItem = "Medio";
        }
        else if (style == "Line Art dettagliata")
        {
            difficulty.SelectedItem = "Impegnativa";
            lineWeight.SelectedItem = "Sottile — Fine";
            complexity.SelectedItem = "Alta";
            density.SelectedItem = "Alta";
            background.SelectedItem = "Dettagliato";
            whiteSpace.SelectedItem = "Compatto";
        }
        else if (style == "Kawaii / Cartoon")
        {
            difficulty.SelectedItem = "Facile";
            lineWeight.SelectedItem = "Spesso — Bold";
            complexity.SelectedItem = "Bassa";
            density.SelectedItem = "Bassa";
            background.SelectedItem = "Semplice / minimo";
            whiteSpace.SelectedItem = "Ampio";
        }
        else if (style == "Mandala / Pattern")
        {
            difficulty.SelectedItem = "Media";
            lineWeight.SelectedItem = "Sottile — Fine";
            complexity.SelectedItem = "Alta";
            density.SelectedItem = "Alta";
            background.SelectedItem = "Nessuno / bianco";
            whiteSpace.SelectedItem = "Medio";
        }

        clean.IsChecked = true;
        noText.IsChecked = true;
        separate.IsChecked = true;
    }

    private static ComboBox Combo(string name, IEnumerable<string> values, string selected, double width) => new()
    {
        Name = name,
        ItemsSource = values.ToArray(),
        SelectedItem = selected,
        Width = width,
        HorizontalAlignment = HorizontalAlignment.Left
    };

    private static CheckBox Check(string label, bool value) => new() { Content = label, IsChecked = value };

    private static StackPanel Labeled(string label, Control control) => new()
    {
        Spacing = 3,
        Children = { new TextBlock { Text = label, FontSize = 13 }, control }
    };

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
}
