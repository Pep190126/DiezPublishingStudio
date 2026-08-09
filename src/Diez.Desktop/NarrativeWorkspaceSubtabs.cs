using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace DiezPublishingStudio;

internal static class NarrativeWorkspaceSubtabs
{
    private const string AiOptionEntityKind = "DiezAiOption";
    private const string StructureDecisionKey = "StructureDecision";
    private const string StructureKnown = "Known";
    private const string StructureFromProject = "FromProject";

    private static readonly HashSet<string> CharacterKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "Character", "Person", "Persona", "Personaggio"
    };

    private static readonly HashSet<string> PlaceKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "Place", "Location", "Luogo", "Ambientazione"
    };

    private static readonly HashSet<string> EventKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "Event", "Evento"
    };

    private static readonly HashSet<string> ThreadKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "PlotThread", "Thread", "StoryArc", "Arc", "FiloNarrativo"
    };

    public static IReadOnlyList<object?> Build(MainWindow window, PreviewProject project, bool illustrated)
    {
        return
        [
            BuildDatabase(project, illustrated),
            BuildBookType(window, project, illustrated),
            BuildChecks(project),
            BuildAi(project),
            BuildExport(project)
        ];
    }

    private static Control BuildDatabase(PreviewProject project, bool illustrated)
    {
        var textMaterials = project.Materials
            .Where(m => !IllustrationPlanService.IsImage(m))
            .OrderBy(m => m.ImportedAtLocal, StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var items = new List<TabItem>
        {
            new() { Header = "Manoscritti", Content = MaterialList(textMaterials) },
            new() { Header = "Personaggi", Content = EntityList(project, CharacterKinds, "Nessun personaggio registrato.") },
            new() { Header = "Luoghi", Content = EntityList(project, PlaceKinds, "Nessun luogo registrato.") },
            new() { Header = "Eventi", Content = EntityList(project, EventKinds, "Nessun evento registrato.") },
            new() { Header = "Fili narrativi", Content = EntityList(project, ThreadKinds, "Nessun filo narrativo registrato.") }
        };

        if (illustrated)
        {
            var images = project.Materials
                .Where(IllustrationPlanService.IsImage)
                .OrderBy(m => m.FileName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            items.Add(new TabItem { Header = "Illustrazioni", Content = MaterialList(images, "Nessuna illustrazione nel progetto.") });
        }

        return SubTabs(items);
    }

    private static Control BuildBookType(MainWindow window, PreviewProject project, bool illustrated)
    {
        var structure = new StackPanel { Spacing = 8, Margin = new Thickness(10) };
        structure.Children.Add(new TextBlock
        {
            Text = "Conosci già la struttura e il numero di pagine?",
            FontSize = 19,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });

        var yes = new RadioButton
        {
            Content = "Sì",
            GroupName = "narrative-structure-choice",
            IsChecked = StructureIsKnown(project)
        };
        var no = new RadioButton
        {
            Content = "No, definiscili in base al progetto",
            GroupName = "narrative-structure-choice",
            IsChecked = !StructureIsKnown(project)
        };
        var numericPanel = new StackPanel { Spacing = 7, IsVisible = StructureIsKnown(project) };
        var automaticPanel = BuildAutomaticStructurePanel(project, illustrated);
        automaticPanel.IsVisible = !StructureIsKnown(project);

        void RefreshChoice()
        {
            numericPanel.IsVisible = StructureIsKnown(project);
            automaticPanel.IsVisible = !StructureIsKnown(project);
            SaveCurrent(window, project);
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

        structure.Children.Add(yes);
        structure.Children.Add(no);
        structure.Children.Add(new TextBlock
        {
            Text = "Se scegli No, Diez parte dai manoscritti e dagli altri materiali, controlla il progetto e ti propone struttura e numeri prima dell'approvazione.",
            FontSize = 12,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });

        var structuralKeys = illustrated
            ? new HashSet<string>(new[] { "PageCount", "ImageCount" }, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(new[] { "TargetWords", "PageCount", "ChapterCount", "Structure" }, StringComparer.OrdinalIgnoreCase);
        AddOptionControls(numericPanel, window, project, structuralKeys);
        structure.Children.Add(numericPanel);
        structure.Children.Add(automaticPanel);

        var settings = new StackPanel { Spacing = 8, Margin = new Thickness(10) };
        settings.Children.Add(new TextBlock
        {
            Text = illustrated ? "Impostazioni del libro illustrato" : "Impostazioni narrative",
            FontSize = 19
        });
        settings.Children.Add(new TextBlock
        {
            Text = "Qui restano le scelte editoriali che non dipendono dal numero di capitoli o pagine.",
            FontSize = 12,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });
        var allKeys = BookTypeAiOptionsService.Definitions(project).Select(d => d.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        allKeys.ExceptWith(structuralKeys);
        AddOptionControls(settings, window, project, allKeys);

        return SubTabs
        ([
            new TabItem { Header = "Struttura", Content = new ScrollViewer { Content = structure, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto } },
            new TabItem { Header = "Impostazioni", Content = new ScrollViewer { Content = settings, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto } }
        ]);
    }

    private static Control BuildChecks(PreviewProject project)
    {
        var chapters = project.ContentNodes
            .Where(n => string.Equals(n.Kind, "Chapter", StringComparison.OrdinalIgnoreCase))
            .OrderBy(n => n.Ordinal)
            .ToList();
        var scenes = project.ContentNodes
            .Where(n => string.Equals(n.Kind, "Scene", StringComparison.OrdinalIgnoreCase))
            .OrderBy(n => n.Ordinal)
            .ToList();
        var openIssues = project.ConsistencyIssues
            .Where(i => string.Equals(i.Status, "Open", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var contradictionIssues = openIssues
            .Where(i => (i.Code ?? string.Empty).Contains("contrad", StringComparison.OrdinalIgnoreCase) ||
                        (i.Message ?? string.Empty).Contains("contrad", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var structureText = chapters.Count == 0
            ? "La struttura non è ancora stata approvata. Quando Diez la propone, qui vedrai Parti → Capitoli → Scene e i relativi numeri."
            : $"Struttura corrente: {chapters.Count} capitoli · {scenes.Count} scene.";

        return SubTabs
        ([
            new TabItem { Header = "Struttura", Content = MessagePanel("Struttura", structureText) },
            new TabItem { Header = "Continuità", Content = IssueList(openIssues, "Nessuna segnalazione di continuità aperta.") },
            new TabItem { Header = "Contraddizioni", Content = IssueList(contradictionIssues, "Nessuna contraddizione aperta registrata.") },
            new TabItem { Header = "Da decidere", Content = IssueList(openIssues, "Non ci sono decisioni aperte.") }
        ]);
    }

    private static Control BuildAi(PreviewProject project)
    {
        return SubTabs
        ([
            new TabItem
            {
                Header = "Crea",
                Content = MessagePanel("Crea", "Qui vivranno DEVE FARE / NON DEVE FARE e i controlli del Tipo libro. Nessuna proposta viene applicata automaticamente.")
            },
            new TabItem
            {
                Header = "Correggi",
                Content = MessagePanel("Correggi", "Qui Diez prepara correzioni mirate su capitolo, scena, personaggio o altro elemento selezionato, senza riscrivere parti non coinvolte.")
            },
            new TabItem
            {
                Header = "Provider e modalità",
                Content = MessagePanel("Provider e modalità", "Scegli Prompt pack, API o Chiedi ogni volta. I provider disponibili vengono dal catalogo AI di Diez e non sono scritti a mano nella schermata.")
            }
        ]);
    }

    private static Control BuildExport(PreviewProject project)
    {
        return SubTabs
        ([
            new TabItem
            {
                Header = "Output",
                Content = MessagePanel("Output", "Documento modificabile (DOCX) e gli altri output pertinenti al progetto vengono preparati qui.")
            },
            new TabItem
            {
                Header = "Google",
                Content = MessagePanel("Google", "Dopo la creazione del file puoi scegliere Drive / Google Documenti / Fogli Google senza cambiare il contenuto editoriale dell'output.")
            },
            new TabItem
            {
                Header = "Libri finalizzati",
                Content = MessagePanel("Libri finalizzati", "Qui verranno assorbite Copia identica, Rigenera output e Riprova su Google della Libreria finalizzati, sempre nella MainWindow.")
            }
        ]);
    }

    private static Control MaterialList(IReadOnlyList<MaterialEntry> materials, string empty = "Nessun manoscritto importato.")
    {
        var rows = materials.Count == 0
            ? new List<string> { empty }
            : materials.Select((m, i) => $"{i + 1}. {m.FileName} · {FormatBytes(m.SizeBytes)}").ToList();
        return new ListBox { Margin = new Thickness(8), ItemsSource = rows };
    }

    private static Control EntityList(PreviewProject project, HashSet<string> kinds, string empty)
    {
        var entities = project.Entities
            .Where(e => kinds.Contains(e.Kind ?? string.Empty))
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .Select(e => string.IsNullOrWhiteSpace(e.Notes) ? e.Name : $"{e.Name} — {OneLine(e.Notes)}")
            .ToList();
        if (entities.Count == 0) entities.Add(empty);
        return new ListBox { Margin = new Thickness(8), ItemsSource = entities };
    }

    private static StackPanel BuildAutomaticStructurePanel(PreviewProject project, bool illustrated)
    {
        var chapters = project.ContentNodes.Count(n => string.Equals(n.Kind, "Chapter", StringComparison.OrdinalIgnoreCase));
        var scenes = project.ContentNodes.Count(n => string.Equals(n.Kind, "Scene", StringComparison.OrdinalIgnoreCase));
        var manuscripts = project.Materials.Count(m => !IllustrationPlanService.IsImage(m));
        var images = project.Materials.Count(IllustrationPlanService.IsImage);
        var current = chapters > 0 || scenes > 0
            ? $"Struttura attualmente riconosciuta: {chapters} capitoli · {scenes} scene."
            : "La struttura non è ancora stata proposta.";
        if (illustrated) current += $" Immagini presenti: {images}.";

        return new StackPanel
        {
            Spacing = 6,
            Margin = new Thickness(0, 8, 0, 0),
            Children =
            {
                new TextBlock { Text = "Diez decide in base al progetto", FontSize = 17 },
                new TextBlock
                {
                    Text = $"Manoscritti/materiali testuali presenti: {manuscripts}. {current} Diez analizzerà ordine, continuità e dimensione del materiale e ti presenterà la proposta prima di renderla struttura del libro.",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                }
            }
        };
    }

    private static void AddOptionControls(StackPanel panel, MainWindow window, PreviewProject project, HashSet<string> keys)
    {
        foreach (var definition in BookTypeAiOptionsService.Definitions(project).Where(d => keys.Contains(d.Key)))
        {
            Control control;
            switch (definition.Kind)
            {
                case BookTypeAiOptionKind.Toggle:
                {
                    var check = new CheckBox
                    {
                        Content = definition.Label,
                        IsChecked = string.Equals(BookTypeAiOptionsService.Get(project, definition), "true", StringComparison.OrdinalIgnoreCase)
                    };
                    check.IsCheckedChanged += (_, _) =>
                    {
                        BookTypeAiOptionsService.Set(project, definition, check.IsChecked == true ? "true" : "false");
                        SaveCurrent(window, project);
                    };
                    control = check;
                    break;
                }
                case BookTypeAiOptionKind.Choice:
                {
                    var combo = new ComboBox
                    {
                        ItemsSource = definition.Choices,
                        SelectedItem = BookTypeAiOptionsService.Get(project, definition),
                        MinWidth = 220
                    };
                    if (combo.SelectedIndex < 0 && definition.Choices is { Count: > 0 }) combo.SelectedIndex = 0;
                    combo.SelectionChanged += (_, _) =>
                    {
                        BookTypeAiOptionsService.Set(project, definition, combo.SelectedItem?.ToString());
                        SaveCurrent(window, project);
                    };
                    control = Field(definition.Label, combo);
                    break;
                }
                default:
                {
                    var text = new TextBox
                    {
                        Text = BookTypeAiOptionsService.Get(project, definition),
                        MinWidth = 220,
                        Watermark = definition.Kind == BookTypeAiOptionKind.Number ? "Numero" : "Facoltativo"
                    };
                    text.LostFocus += (_, _) =>
                    {
                        BookTypeAiOptionsService.Set(project, definition, text.Text);
                        SaveCurrent(window, project);
                    };
                    control = Field(definition.Label, text);
                    break;
                }
            }
            if (!string.IsNullOrWhiteSpace(definition.Help)) ToolTip.SetTip(control, definition.Help);
            panel.Children.Add(control);
        }
    }

    private static Control IssueList(IReadOnlyList<ConsistencyIssue> issues, string empty)
    {
        var rows = issues.Count == 0
            ? new List<string> { empty }
            : issues.Select(i => string.IsNullOrWhiteSpace(i.Message) ? i.Code : i.Message).ToList();
        return new ListBox { Margin = new Thickness(8), ItemsSource = rows };
    }

    private static TabControl SubTabs(IReadOnlyList<TabItem> items) => new()
    {
        ItemsSource = items,
        SelectedIndex = 0,
        Margin = new Thickness(4)
    };

    private static StackPanel MessagePanel(string title, string text) => new()
    {
        Margin = new Thickness(12),
        Spacing = 9,
        Children =
        {
            new TextBlock { Text = title, FontSize = 19 },
            new TextBlock { Text = text, TextWrapping = Avalonia.Media.TextWrapping.Wrap }
        }
    };

    private static StackPanel Field(string label, Control control) => new()
    {
        Spacing = 3,
        Children = { new TextBlock { Text = label }, control }
    };

    private static bool StructureIsKnown(PreviewProject project)
    {
        var type = BookTypeProfileService.Get(project);
        var key = $"{type}|{StructureDecisionKey}";
        var value = project.Entities.FirstOrDefault(e =>
            string.Equals(e.Kind, AiOptionEntityKind, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(e.Name, key, StringComparison.OrdinalIgnoreCase))?.Notes;
        return string.Equals(value, StructureKnown, StringComparison.OrdinalIgnoreCase);
    }

    private static void SetStructureDecision(PreviewProject project, bool known)
    {
        var type = BookTypeProfileService.Get(project);
        var key = $"{type}|{StructureDecisionKey}";
        var matches = project.Entities.Where(e =>
            string.Equals(e.Kind, AiOptionEntityKind, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(e.Name, key, StringComparison.OrdinalIgnoreCase)).ToList();
        var entity = matches.FirstOrDefault();
        if (entity is null)
        {
            entity = new GraphEntity
            {
                Kind = AiOptionEntityKind,
                Name = key,
                Notes = known ? StructureKnown : StructureFromProject,
                IsCandidate = false
            };
            project.Entities.Add(entity);
        }
        else entity.Notes = known ? StructureKnown : StructureFromProject;
        foreach (var duplicate in matches.Skip(1)) project.Entities.Remove(duplicate);
    }

    private static void SaveCurrent(MainWindow window, PreviewProject project)
    {
        var path = typeof(MainWindow).GetField("_currentProjectPath", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(window) as string;
        if (string.IsNullOrWhiteSpace(path)) return;
        _ = SaveSafeAsync(path, project);
    }

    private static async Task SaveSafeAsync(string path, PreviewProject project)
    {
        try { await ProjectFileStore.SaveAsync(path, project); }
        catch { }
    }

    private static string OneLine(string value)
    {
        var normalized = string.Join(" ", (value ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)).Trim();
        return normalized.Length <= 120 ? normalized : normalized[..117] + "...";
    }

    private static string FormatBytes(long value) => value switch
    {
        < 1024 => $"{value} B",
        < 1024 * 1024 => $"{value / 1024d:0.0} KB",
        _ => $"{value / 1024d / 1024d:0.0} MB"
    };
}
