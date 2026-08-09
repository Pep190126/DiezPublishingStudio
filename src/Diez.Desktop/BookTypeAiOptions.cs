using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace DiezPublishingStudio;

internal enum BookTypeAiOptionKind
{
    Text,
    Number,
    Choice,
    Toggle
}

internal sealed record BookTypeAiOptionDefinition(
    string Key,
    string Label,
    BookTypeAiOptionKind Kind,
    string DefaultValue,
    IReadOnlyList<string>? Choices = null,
    string Help = "");

internal static class BookTypeAiOptionsService
{
    private const string EntityKind = "DiezAiOption";
    private const string StructureDecisionKey = "StructureDecision";
    private const string StructureKnown = "Known";
    private const string StructureFromProject = "FromProject";

    public static IReadOnlyList<BookTypeAiOptionDefinition> Definitions(PreviewProject project)
    {
        var type = BookTypeProfileService.Get(project);
        return type switch
        {
            BookTypeProfileService.WordSearch =>
            [
                N("PuzzleCount", "Numero di puzzle", "100"),
                N("WordsPerPuzzle", "Parole per puzzle", "20"),
                C("Language", "Lingua", "Come il progetto", "Come il progetto", "Italiano", "Inglese", "Spagnolo", "Francese", "Tedesco"),
                T("UseAvailableCategories", "Usa categorie, sottocategorie e serie disponibili", true),
                T("NoDuplicates", "Evita parole duplicate tra i puzzle", true),
                T("AllowPhrases", "Consenti anche frasi brevi", true),
                N("MaxWordLength", "Lunghezza massima parola/frase", "22")
            ],
            BookTypeProfileService.ColoringBook =>
            [
                N("ImageCount", "Numero di tavole", "50"),
                C("PageFormat", "Formato pagina", "8.5 x 11 in", "8.5 x 11 in", "8 x 10 in", "A4", "Quadrato", "Personalizzato nel box"),
                C("Orientation", "Orientamento", "Verticale", "Verticale", "Quadrato", "Orizzontale"),
                C("Resolution", "Qualità / risoluzione", "300 DPI", "300 DPI", "HD", "4K", "Personalizzata nel box"),
                C("Background", "Sfondo", "Bianco", "Bianco", "Trasparente", "Altro nel box"),
                C("LineStyle", "Tratto", "Linee pulite", "Linee pulite", "Linee spesse", "Linee sottili", "Molto dettagliato"),
                T("SeriesConsistency", "Mantieni coerente tutta la raccolta", true)
            ],
            BookTypeProfileService.ImageCollection =>
            [
                N("ImageCount", "Numero di immagini", "50"),
                C("Orientation", "Orientamento", "Verticale", "Verticale", "Quadrato", "Orizzontale", "Misto"),
                C("Resolution", "Qualità / risoluzione", "Alta", "Alta", "300 DPI", "HD", "4K", "Personalizzata nel box"),
                C("FileFormat", "Formato immagine preferito", "PNG", "PNG", "JPG", "WebP"),
                T("SeriesConsistency", "Mantieni coerente tutta la raccolta", true),
                T("CreateDescription", "Crea anche una descrizione per ogni immagine", false),
                C("DescriptionLength", "Lunghezza descrizione", "Dettagliata", "Breve", "Dettagliata", "Lunga", "Molto lunga / migliaia di parole")
            ],
            BookTypeProfileService.Novel =>
            [
                X("Genre", "Genere", ""),
                N("TargetWords", "Lunghezza indicativa totale (parole)", "70000"),
                N("PageCount", "Numero indicativo di pagine", "300"),
                N("ChapterCount", "Numero indicativo di capitoli", "20"),
                C("Structure", "Struttura", "Capitoli + scene", "Capitoli", "Parti + capitoli", "Capitoli + scene", "Parti + capitoli + scene"),
                C("PointOfView", "Punto di vista", "Terza persona limitata", "Prima persona", "Terza persona limitata", "Terza persona onnisciente", "Multiplo", "Decidi nel box"),
                C("VerbTense", "Tempo verbale", "Passato", "Passato", "Presente", "Misto", "Decidi nel box"),
                X("Tone", "Tono", ""),
                T("Continuity", "Mantieni coerenza di personaggi, luoghi, eventi e fili narrativi", true)
            ],
            BookTypeProfileService.IllustratedBook =>
            [
                N("PageCount", "Numero indicativo di pagine", "32"),
                N("ImageCount", "Numero indicativo di illustrazioni", "16"),
                C("Orientation", "Orientamento", "Verticale", "Verticale", "Quadrato", "Orizzontale"),
                C("TextAmount", "Quantità di testo per pagina", "Media", "Molto breve", "Breve", "Media", "Lunga"),
                T("CharacterConsistency", "Mantieni coerenti personaggi e ambienti ricorrenti", true),
                T("KeepOriginalImages", "Mantieni sempre gli originali separati", true)
            ],
            BookTypeProfileService.Quiz =>
            [
                N("QuestionCount", "Numero di domande", "100"),
                N("AnswersPerQuestion", "Risposte per domanda", "4"),
                C("Difficulty", "Difficoltà", "Mista", "Facile", "Media", "Difficile", "Mista"),
                X("Categories", "Categorie", ""),
                T("NoDuplicates", "Evita domande duplicate", true),
                T("Explanations", "Aggiungi spiegazione della risposta", false)
            ],
            BookTypeProfileService.DataCollection =>
            [
                N("TargetRows", "Numero indicativo di elementi", "500"),
                X("RequiredColumns", "Colonne / campi desiderati", ""),
                T("Deduplicate", "Unisci e rimuovi i doppioni", true),
                T("Normalize", "Uniforma valori e formati", true),
                T("KeepProvenance", "Mantieni l'origine dei dati", true)
            ],
            _ =>
            [
                N("ItemCount", "Numero indicativo di elementi", "1"),
                C("Language", "Lingua", "Come il progetto", "Come il progetto", "Italiano", "Inglese", "Spagnolo", "Francese", "Tedesco")
            ]
        };
    }

    public static string Get(PreviewProject project, BookTypeAiOptionDefinition definition)
    {
        var type = BookTypeProfileService.Get(project);
        var key = StorageKey(type, definition.Key);
        var entity = project.Entities.FirstOrDefault(e =>
            string.Equals(e.Kind, EntityKind, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(e.Name, key, StringComparison.OrdinalIgnoreCase));
        return entity is null ? definition.DefaultValue : entity.Notes ?? definition.DefaultValue;
    }

    public static void Set(PreviewProject project, BookTypeAiOptionDefinition definition, string? value)
    {
        var type = BookTypeProfileService.Get(project);
        var key = StorageKey(type, definition.Key);
        var normalized = NormalizeValue(definition, value);
        SetRaw(project, key, normalized);
    }

    public static IReadOnlyList<string> PromptLines(PreviewProject project)
    {
        var lines = new List<string>();
        var type = BookTypeProfileService.Get(project);
        if (UsesStructureQuestion(type) && !StructureIsKnown(project))
        {
            lines.Add("Struttura e numero di pagine: da definire in base al progetto e ai materiali disponibili");
            return lines;
        }

        foreach (var definition in Definitions(project))
        {
            var value = Get(project, definition);
            if (definition.Kind == BookTypeAiOptionKind.Toggle)
            {
                if (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
                    lines.Add($"{definition.Label}: sì");
                else
                    lines.Add($"{definition.Label}: no");
                continue;
            }
            if (!string.IsNullOrWhiteSpace(value)) lines.Add($"{definition.Label}: {value}");
        }
        return lines;
    }

    public static Control BuildEditor(PreviewProject project, Action? changed = null)
    {
        var outer = new StackPanel { Spacing = 8 };
        var type = BookTypeProfileService.Get(project);

        if (UsesStructureQuestion(type))
        {
            outer.Children.Add(new TextBlock
            {
                Text = "Conosci già la struttura e il numero di pagine?",
                FontSize = 17,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            });

            var yes = new RadioButton
            {
                Content = "Sì",
                GroupName = "diez-structure-choice",
                IsChecked = StructureIsKnown(project)
            };
            var no = new RadioButton
            {
                Content = "No, definiscili in base al progetto",
                GroupName = "diez-structure-choice",
                IsChecked = !StructureIsKnown(project)
            };
            var choices = new StackPanel { Spacing = 6 };

            void RefreshChoice()
            {
                choices.IsVisible = StructureIsKnown(project);
                changed?.Invoke();
            }

            yes.IsCheckedChanged += (_, _) =>
            {
                if (yes.IsChecked != true) return;
                SetStructureDecision(project, true);
                RefreshChoice();
            };
            no.IsCheckedChanged += (_, _) =>
            {
                if (no.IsChecked != true) return;
                SetStructureDecision(project, false);
                RefreshChoice();
            };

            outer.Children.Add(yes);
            outer.Children.Add(no);
            outer.Children.Add(new TextBlock
            {
                Text = "Se scegli No, Diez parte dai materiali del progetto, propone la struttura e ti mostra i numeri risultanti prima che tu li approvi.",
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                FontSize = 12
            });

            BuildOptionsPanel(project, choices, type, changed);
            choices.IsVisible = StructureIsKnown(project);
            outer.Children.Add(choices);
        }
        else
        {
            BuildOptionsPanel(project, outer, type, changed);
        }

        return new Border
        {
            Padding = new Thickness(10),
            Child = outer
        };
    }

    private static void BuildOptionsPanel(PreviewProject project, StackPanel panel, string type, Action? changed)
    {
        panel.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(type) ? "Scelte del contenuto" : $"Scelte per {type}",
            FontSize = 17
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Usa questi controlli per le cose ripetitive. I due box servono solo per le indicazioni che non entrano bene qui.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            FontSize = 12
        });

        foreach (var definition in Definitions(project))
        {
            Control input;
            switch (definition.Kind)
            {
                case BookTypeAiOptionKind.Toggle:
                {
                    var check = new CheckBox
                    {
                        Content = definition.Label,
                        IsChecked = string.Equals(Get(project, definition), "true", StringComparison.OrdinalIgnoreCase)
                    };
                    check.IsCheckedChanged += (_, _) =>
                    {
                        Set(project, definition, check.IsChecked == true ? "true" : "false");
                        changed?.Invoke();
                    };
                    input = check;
                    break;
                }
                case BookTypeAiOptionKind.Choice:
                {
                    var combo = new ComboBox
                    {
                        ItemsSource = definition.Choices,
                        SelectedItem = Get(project, definition),
                        MinWidth = 190
                    };
                    if (combo.SelectedIndex < 0 && definition.Choices is { Count: > 0 }) combo.SelectedIndex = 0;
                    combo.SelectionChanged += (_, _) =>
                    {
                        Set(project, definition, combo.SelectedItem?.ToString());
                        changed?.Invoke();
                    };
                    input = Field(definition.Label, combo);
                    break;
                }
                default:
                {
                    var text = new TextBox
                    {
                        Text = Get(project, definition),
                        MinWidth = 190,
                        Watermark = definition.Kind == BookTypeAiOptionKind.Number ? "Numero" : "Facoltativo"
                    };
                    text.TextChanged += (_, _) =>
                    {
                        Set(project, definition, text.Text);
                        changed?.Invoke();
                    };
                    input = Field(definition.Label, text);
                    break;
                }
            }
            if (!string.IsNullOrWhiteSpace(definition.Help)) ToolTip.SetTip(input, definition.Help);
            panel.Children.Add(input);
        }
    }

    private static bool UsesStructureQuestion(string type) =>
        string.Equals(type, BookTypeProfileService.Novel, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, BookTypeProfileService.IllustratedBook, StringComparison.OrdinalIgnoreCase);

    private static bool StructureIsKnown(PreviewProject project)
    {
        var type = BookTypeProfileService.Get(project);
        var value = GetRaw(project, StorageKey(type, StructureDecisionKey));
        return string.Equals(value, StructureKnown, StringComparison.OrdinalIgnoreCase);
    }

    private static void SetStructureDecision(PreviewProject project, bool known)
    {
        var type = BookTypeProfileService.Get(project);
        SetRaw(project, StorageKey(type, StructureDecisionKey), known ? StructureKnown : StructureFromProject);
    }

    private static string? GetRaw(PreviewProject project, string key) =>
        project.Entities.FirstOrDefault(e =>
            string.Equals(e.Kind, EntityKind, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(e.Name, key, StringComparison.OrdinalIgnoreCase))?.Notes;

    private static void SetRaw(PreviewProject project, string key, string value)
    {
        var matches = project.Entities.Where(e =>
            string.Equals(e.Kind, EntityKind, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(e.Name, key, StringComparison.OrdinalIgnoreCase)).ToList();
        var entity = matches.FirstOrDefault();
        if (entity is null)
        {
            entity = new GraphEntity
            {
                Kind = EntityKind,
                Name = key,
                Notes = value,
                IsCandidate = false
            };
            project.Entities.Add(entity);
        }
        else entity.Notes = value;
        foreach (var duplicate in matches.Skip(1)) project.Entities.Remove(duplicate);
    }

    private static string NormalizeValue(BookTypeAiOptionDefinition definition, string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (definition.Kind == BookTypeAiOptionKind.Toggle)
            return string.Equals(text, "true", StringComparison.OrdinalIgnoreCase) ? "true" : "false";
        if (definition.Kind == BookTypeAiOptionKind.Number && !string.IsNullOrWhiteSpace(text))
            return int.TryParse(text, out var number) ? Math.Max(0, number).ToString() : definition.DefaultValue;
        return text;
    }

    private static string StorageKey(string type, string key) => $"{type}|{key}";

    private static StackPanel Field(string label, Control control) => new()
    {
        Spacing = 3,
        Children = { new TextBlock { Text = label }, control }
    };

    private static BookTypeAiOptionDefinition N(string key, string label, string value) =>
        new(key, label, BookTypeAiOptionKind.Number, value);
    private static BookTypeAiOptionDefinition X(string key, string label, string value) =>
        new(key, label, BookTypeAiOptionKind.Text, value);
    private static BookTypeAiOptionDefinition T(string key, string label, bool value) =>
        new(key, label, BookTypeAiOptionKind.Toggle, value ? "true" : "false");
    private static BookTypeAiOptionDefinition C(string key, string label, string value, params string[] choices) =>
        new(key, label, BookTypeAiOptionKind.Choice, value, choices);
}

internal static class BookTypeAiOptionsUi
{
    private static readonly HashSet<Window> Attached = [];

    public static void Attach(MainWindow mainWindow)
    {
        if (Application.Current?.ApplicationLifetime is not Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop) return;
        var timer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        timer.Tick += (_, _) =>
        {
            foreach (var window in desktop.Windows.ToList())
            {
                if (window is not (AiJobEditorWindow or SimpleAiCreationWindow)) continue;
                if (Attached.Contains(window)) continue;
                if (!TryAttach(window)) continue;
                Attached.Add(window);
                window.Closed += (_, _) => Attached.Remove(window);
            }
        };
        mainWindow.Closed += (_, _) => timer.Stop();
        timer.Start();
    }

    private static bool TryAttach(Window window)
    {
        var project = window.GetType().GetField("_project", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(window) as PreviewProject;
        if (project is null) return false;

        var mustNotDoLabel = Descendants(window).OfType<TextBlock>()
            .FirstOrDefault(t => string.Equals(t.Text, "NON DEVE FARE", StringComparison.Ordinal));
        if (mustNotDoLabel is null) return false;

        var root = FindMainStack(window);
        if (root is null) return false;
        var labelIndex = root.Children.IndexOf(mustNotDoLabel);
        if (labelIndex >= 0)
        {
            var insertAt = Math.Min(root.Children.Count, labelIndex + 2);
            root.Children.Insert(insertAt, BookTypeAiOptionsService.BuildEditor(project));
            return true;
        }

        var request = window.GetType().GetField("_request", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(window) as TextBox;
        if (request is null) return false;
        var requestField = Descendants(window).OfType<StackPanel>().FirstOrDefault(p => p.Children.Contains(request));
        if (requestField is null) return false;
        var parent = Descendants(window).OfType<StackPanel>().FirstOrDefault(p => p.Children.Contains(requestField));
        if (parent is null) return false;
        var index = parent.Children.IndexOf(requestField);
        parent.Children.Insert(index + 1, BookTypeAiOptionsService.BuildEditor(project));
        return true;
    }

    private static StackPanel? FindMainStack(Window window)
    {
        if (window.Content is Border border)
        {
            if (border.Child is StackPanel stack) return stack;
            if (border.Child is ScrollViewer scroll && scroll.Content is StackPanel scrollStack) return scrollStack;
        }
        return null;
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        yield return root;
        if (root is Panel panel)
            foreach (var child in panel.Children.SelectMany(Descendants)) yield return child;
        if (root is Border border && border.Child is Control borderChild)
            foreach (var child in Descendants(borderChild)) yield return child;
        if (root is ScrollViewer scroll && scroll.Content is Control scrollChild)
            foreach (var child in Descendants(scrollChild)) yield return child;
        if (root is ContentControl content && content.Content is Control contentChild)
            foreach (var child in Descendants(contentChild)) yield return child;
    }
}
