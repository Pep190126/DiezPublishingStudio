using System.Collections;
using System.Reflection;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace DiezPublishingStudio;

/// <summary>
/// SW-FLOW-11 owns the essential logical screens directly. Essential editors are
/// created as native controls inside the active page and never depend on a later
/// decorator to become visible or editable.
/// </summary>
internal static class SingleWindowNativeV11Ui
{
    public const string Marker = "SW-FLOW-11";
    private static readonly HashSet<MainWindow> Attached = [];

    public static void Attach(MainWindow window)
    {
        if (!Attached.Add(window)) return;
        var host = SingleWindowEntryPointUi.GetHost(window);
        var pageHost = Field<ContentControl>(host, "_pageHost");
        if (pageHost is not null)
        {
            pageHost.PropertyChanged += (_, e) =>
            {
                if (e.Property != ContentControl.ContentProperty) return;
                StyleCurrentPage(window);
                Dispatcher.UIThread.Post(() => StyleCurrentPage(window), DispatcherPriority.Loaded);
            };
        }
        window.Closed += (_, _) => Attached.Remove(window);
    }

    internal static void ShowStart(MainWindow window)
    {
        var host = SingleWindowEntryPointUi.GetHost(window);
        ClearHistory(host);
        if (TrySession(window, out var project, out _))
        {
            PushProjectLanding(window, host, project);
            ShowBookType(window, host);
            return;
        }
        ShowWelcome(window, host);
    }

    private static void ShowWelcome(MainWindow window, object host)
    {
        var create = Button("Nuovo progetto", 180);
        var open = Button("Apri progetto .diez", 190);
        create.Click += async (_, _) =>
        {
            await InvokeMainTaskAsync(window, "CreateProjectAsync");
            if (TrySession(window, out _, out _)) ShowBookType(window, host);
        };
        open.Click += async (_, _) =>
        {
            await InvokeMainTaskAsync(window, "OpenProjectAsync");
            if (TrySession(window, out _, out _)) ShowBookType(window, host);
        };

        Push(host, $"Inizia · {Marker}", new StackPanel
        {
            Spacing = 14,
            Children =
            {
                new TextBlock { Text = "Diez Publishing Studio", FontSize = 28 },
                new TextBlock
                {
                    Text = "Crea o apri un progetto. Il percorso del libro resta sempre in questa finestra e ogni passo può tornare al precedente.",
                    TextWrapping = TextWrapping.Wrap
                },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { create, open } }
            }
        }, Preview("Qui compariranno paradigmi, immagini generate, confronti e descrizioni."),
            "Crea o apri un progetto per iniziare.");
    }

    private static void PushProjectLanding(MainWindow window, object host, PreviewProject project)
    {
        var type = Button("Scegli / controlla Tipo libro", 220);
        type.Click += (_, _) => ShowBookType(window, host);
        Push(host, $"Progetto · {Marker}", new StackPanel
        {
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = "Progetto aperto", FontSize = 25 },
                new TextBlock { Text = project.Name, FontSize = 18, TextWrapping = TextWrapping.Wrap },
                new TextBlock
                {
                    Text = "Da qui puoi entrare nel percorso editoriale. Questa schermata è anche la destinazione di Indietro quando sei nella scelta del Tipo libro.",
                    TextWrapping = TextWrapping.Wrap
                },
                type
            }
        }, Preview("Il riquadro anteprima resta disponibile durante tutto il percorso."),
            "Progetto pronto.");
    }

    internal static void ShowBookType(MainWindow window, object host)
    {
        if (!TrySession(window, out var project, out var path)) return;
        var choices = new[]
        {
            BookTypeProfileService.ColoringBook,
            BookTypeProfileService.ImageCollection,
            BookTypeProfileService.IllustratedBook,
            BookTypeProfileService.EssayManual,
            BookTypeProfileService.WordSearch,
            BookTypeProfileService.Crossword,
            BookTypeProfileService.Quiz,
            BookTypeProfileService.Novel,
            BookTypeProfileService.DataCollection,
            BookTypeProfileService.Other
        };
        var current = BookTypeProfileService.Get(project);
        var combo = new ComboBox
        {
            Name = "DiezNativeBookTypeCombo",
            ItemsSource = choices,
            SelectedItem = choices.FirstOrDefault(x => string.Equals(x, current, StringComparison.OrdinalIgnoreCase)) ?? BookTypeProfileService.ColoringBook,
            Width = 360,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        var apply = Button("Usa questo Tipo libro", 190);
        apply.Name = "DiezNativeBookTypeApply";
        apply.Click += async (_, _) =>
        {
            apply.IsEnabled = false;
            try
            {
                var chosen = combo.SelectedItem?.ToString() ?? BookTypeProfileService.Other;
                BookTypeProfileService.Set(project, chosen);
                await ProjectFileStore.SaveAsync(path, project);
                if (IsVisualType(chosen)) ShowQuantity(window, host);
                else SingleWindowEntryPointUi.Invoke(host, "OpenCurrentBook");
            }
            catch (Exception ex)
            {
                Report(window, host, "Non riesco ad applicare il Tipo libro: " + ex.GetBaseException().Message);
            }
            finally { apply.IsEnabled = true; }
        };

        Push(host, "Tipo libro", new StackPanel
        {
            Name = "DiezNativeBookTypePage",
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = "Quale libro stai preparando?", FontSize = 24 },
                new TextBlock
                {
                    Text = "Scegli il Tipo libro e continua. Per cambiarlo in seguito usa Indietro: non serve un secondo pulsante nella pagina 1/4.",
                    TextWrapping = TextWrapping.Wrap
                },
                combo,
                apply
            }
        }, Preview("Dopo la scelta, controlli e anteprima si adattano al Tipo libro."),
            "Seleziona il Tipo libro e conferma.");
    }

    internal static void ShowQuantity(MainWindow window, object host)
    {
        if (!TrySession(window, out var project, out var path)) return;
        var type = BookTypeProfileService.Get(project);
        var coloring = string.Equals(type, BookTypeProfileService.ColoringBook, StringComparison.OrdinalIgnoreCase);
        var existing = ImageJobs(project).Count;
        var hostCount = ReadHostString(host, "Count");
        var initialCount = int.TryParse(hostCount, out var parsed) && parsed is >= 1 and <= 500 ? parsed : Math.Max(1, existing);

        var count = new NumericUpDown
        {
            Name = "ExactImageCount",
            Value = initialCount,
            Minimum = 1,
            Maximum = 500,
            Increment = 1,
            FormatString = "0",
            Width = 180,
            MinHeight = 42,
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = Brushes.White,
            Foreground = Brushes.Black,
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(2)
        };
        count.ValueChanged += (_, _) => SetHostString(host, "Count", CountValue(count).ToString());

        var (subjectText, environmentText) = LoadDescriptions(project, type);
        var subject = Editor("VisualSubjectInstructions", subjectText, 112,
            "Descrivi personaggio/i o soggetto/i. Puoi indicare eccezioni locali, es. “Immagine 3: aggiungi un gatto”.");
        var environment = Editor("VisualEnvironmentInstructions", environmentText, 112,
            "Descrivi ambiente/scenario. Puoi indicare variazioni locali, es. “Immagine 1: parco; immagine 3: cucina”.");

        var consistentRules = ImageCollectionWorkspaceService.GetConsistencyRules(project);
        var consistent = new CheckBox
        {
            Name = "NativeConsistent",
            Content = "Consistent — mantieni coerenti le immagini",
            IsChecked = !string.IsNullOrWhiteSpace(consistentRules)
        };
        var consistency = new NativeConsistencyEditor(consistentRules, coloring);
        consistency.SetEnabled(consistent.IsChecked == true);
        consistent.IsCheckedChanged += (_, _) => consistency.SetEnabled(consistent.IsChecked == true);

        var profilePanel = BuildNativeBookProfile(project, type, subject, environment, out var saveProfile);
        var next = Button("Avanti → istruzioni", 175);
        consistency.BindNext(next);
        next.Click += async (_, _) =>
        {
            var imageCount = CountValue(count);
            if (imageCount is < 1 or > 500)
            {
                Report(window, host, "Inserisci il numero preciso di immagini, da 1 a 500.");
                count.Focus();
                return;
            }
            if (consistent.IsChecked == true && !consistency.Validate(out var error))
            {
                Report(window, host, error);
                return;
            }

            saveProfile();
            var rules = consistent.IsChecked == true ? consistency.Serialize() : string.Empty;
            SetHostString(host, "Count", imageCount.ToString());
            SetHostBool(host, "Consistent", consistent.IsChecked == true);
            SetHostString(host, "Rules", rules);
            ImageCollectionWorkspaceService.SetConsistencyRules(project, rules);
            var exchange = AiExchangeStateStore.Load(project);
            AiExchangeStateStore.EnsureVisualConsistencyContext(project, exchange, consistent.IsChecked == true, rules);
            AiExchangeStateStore.Save(project, exchange);
            await ProjectFileStore.SaveAsync(path, project);
            ShowPrompt(window, host, imageCount);
        };

        var root = new StackPanel
        {
            Name = "DiezNativeV11QuantityPage",
            Spacing = 11,
            Children =
            {
                new TextBlock { Text = $"{VisualTypeLabel(type)} — quantità e contenuto", FontSize = 24 },
                new TextBlock
                {
                    Text = "Indica il numero esatto di immagini e descrivi direttamente soggetto e ambientazione. Le eccezioni “Immagine N” valgono solo per quella posizione.",
                    TextWrapping = TextWrapping.Wrap
                },
                Labeled("Quante immagini vuoi creare?", count),
                Labeled("Personaggio/i, soggetto/i ed eventuali variazioni per singola immagine", subject),
                Labeled("Ambientazione / scenario ed eventuali variazioni per singola immagine", environment),
                profilePanel,
                new Separator(),
                consistent,
                consistency.Panel,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { next } }
            }
        };

        Push(host, $"{VisualTypeLabel(type)} · 1/4 Quantità · {initialCount} {(initialCount == 1 ? "immagine" : "immagini")}",
            new ScrollViewer
            {
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                Content = root
            },
            CollectionPreview(type, existing, consistent.IsChecked == true),
            existing == 0 ? "Nessuna immagine ancora preparata." : $"Immagini già presenti: {existing}.");

        StyleCurrentPage(window);
    }

    internal static void ShowPrompt(MainWindow window, object host, int count)
    {
        if (!TrySession(window, out var project, out _)) return;
        var type = BookTypeProfileService.Get(project);
        var mustDo = Editor("MustDoEditor", ReadHostString(host, "MustDo"), 125,
            "Cosa devono rappresentare e come devono essere i risultati.");
        var mustNotDo = Editor("MustNotDoEditor", ReadHostString(host, "MustNotDo"), 110,
            "Cosa deve essere evitato. Puoi lasciare vuoto.");
        var prompt = Editor("PromptEditor", ReadHostString(host, "Prompt"), 250,
            "Premi Prepara prompt, poi modificalo liberamente se vuoi.");

        mustDo.TextChanged += (_, _) => SetHostString(host, "MustDo", mustDo.Text ?? string.Empty);
        mustNotDo.TextChanged += (_, _) => SetHostString(host, "MustNotDo", mustNotDo.Text ?? string.Empty);
        prompt.TextChanged += (_, _) => SetHostString(host, "Prompt", prompt.Text ?? string.Empty);

        void Prepare()
        {
            SetHostString(host, "MustDo", mustDo.Text ?? string.Empty);
            SetHostString(host, "MustNotDo", mustNotDo.Text ?? string.Empty);
            var built = BuildMasterPrompt(project, host, count, mustDo.Text, mustNotDo.Text);
            SetHostString(host, "Prompt", built);
            prompt.Text = built;
            Report(window, host, "Prompt preparato. DEVE FARE, NON DEVE FARE e PROMPT sono box nativi editabili con Ctrl+Z/Ctrl+Y.");
        }

        var prepare = Button("Prepara prompt", 155);
        var copy = Button("Copia prompt", 145);
        var next = Button("Avanti → Prompt Pack", 190);
        prepare.Click += (_, _) => Prepare();
        copy.Click += async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(prompt.Text)) Prepare();
            var clipboard = TopLevel.GetTopLevel(window)?.Clipboard;
            if (clipboard is null)
            {
                Report(window, host, "Appunti di Windows non disponibili.");
                return;
            }
            await clipboard.SetTextAsync(prompt.Text ?? string.Empty);
            Report(window, host, "Prompt copiato esattamente dal box modificabile.");
        };
        next.Click += async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(mustDo.Text))
            {
                Report(window, host, "Compila DEVE FARE prima di continuare.");
                mustDo.Focus();
                return;
            }
            if (string.IsNullOrWhiteSpace(prompt.Text)) Prepare();
            SyncHostPromptState(host, count, mustDo.Text, mustNotDo.Text, prompt.Text);
            if (SingleWindowEntryPointUi.Invoke(host, "EnsureSeriesAsync", count) is not Task<bool> ensure || !await ensure) return;
            SingleWindowEntryPointUi.Invoke(host, "OpenPromptPack");
            Dispatcher.UIThread.Post(() => StyleCurrentPage(window), DispatcherPriority.Loaded);
        };

        Push(host, $"{VisualTypeLabel(type)} · 2/4 Istruzioni · {count} {(count == 1 ? "immagine" : "immagini")}",
            new ScrollViewer
            {
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                Content = new StackPanel
                {
                    Name = "DiezNativeV11PromptPage",
                    Spacing = 9,
                    Children =
                    {
                        Labeled("DEVE FARE", mustDo),
                        Labeled("NON DEVE FARE", mustNotDo),
                        Labeled("PROMPT — modificabile", prompt),
                        new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { prepare, copy, next } }
                    }
                }
            },
            CollectionPreview(type, ImageJobs(project).Count, ReadHostBool(host, "Consistent")),
            "I tre editor sono creati direttamente dalla pagina attiva, non da un decoratore successivo.");
        StyleCurrentPage(window);
    }

    private static Control BuildNativeBookProfile(PreviewProject project, string type, TextBox subject, TextBox environment, out Action save)
    {
        if (string.Equals(type, BookTypeProfileService.ColoringBook, StringComparison.OrdinalIgnoreCase))
        {
            var p = BookTypePromptProfileService.LoadColoring(project);
            var style = Combo("ColoringStyle", BookTypePromptProfileService.ColoringStyles, p.Style, 285);
            var audience = Combo("ColoringAudience", BookTypePromptProfileService.TargetAudiences, p.TargetAudience, 235);
            var difficulty = Combo("ColoringDifficulty", BookTypePromptProfileService.Difficulties, p.Difficulty, 170);
            var line = Combo("ColoringLineWeight", BookTypePromptProfileService.LineWeights, p.LineWeight, 390);
            var complexity = Combo("ColoringComplexity", BookTypePromptProfileService.Complexities, p.Complexity, 170);
            var density = Combo("ColoringDensity", BookTypePromptProfileService.Densities, p.ElementDensity, 170);
            var background = Combo("ColoringBackground", BookTypePromptProfileService.Backgrounds, p.Background, 225);
            var white = Combo("ColoringWhiteSpace", BookTypePromptProfileService.WhiteSpaces, p.WhiteSpace, 170);
            var closed = Check("Aree chiuse e facili da colorare", p.ClosedAreas);
            var noTiny = Check("Evita aree e dettagli minuscoli", p.AvoidTinyAreas);
            var clean = Check("Contorni puliti e continui", p.CleanContours);
            var noText = Check("Niente testo o numeri nell'immagine", p.NoTextInsideImage);
            var separated = Check("Soggetto ben separato dallo sfondo", p.SubjectClearlySeparated);
            var notes = Editor("ColoringCustomStyleNotes", p.CustomStyleNotes, 80,
                "Note facoltative sullo stile, es. occhi grandi, bordi molto spessi, niente sfondo.");

            save = () =>
            {
                p.SubjectDescription = subject.Text ?? string.Empty;
                p.EnvironmentDescription = environment.Text ?? string.Empty;
                p.Style = style.SelectedItem?.ToString() ?? p.Style;
                p.TargetAudience = audience.SelectedItem?.ToString() ?? p.TargetAudience;
                p.Difficulty = difficulty.SelectedItem?.ToString() ?? p.Difficulty;
                p.LineWeight = line.SelectedItem?.ToString() ?? p.LineWeight;
                p.Complexity = complexity.SelectedItem?.ToString() ?? p.Complexity;
                p.ElementDensity = density.SelectedItem?.ToString() ?? p.ElementDensity;
                p.Background = background.SelectedItem?.ToString() ?? p.Background;
                p.WhiteSpace = white.SelectedItem?.ToString() ?? p.WhiteSpace;
                p.ClosedAreas = closed.IsChecked == true;
                p.AvoidTinyAreas = noTiny.IsChecked == true;
                p.CleanContours = clean.IsChecked == true;
                p.NoTextInsideImage = noText.IsChecked == true;
                p.SubjectClearlySeparated = separated.IsChecked == true;
                p.CustomStyleNotes = notes.Text ?? string.Empty;
                BookTypePromptProfileService.SaveColoring(project, p);
            };

            return new StackPanel
            {
                Name = "DiezNativeColoringProfile",
                Spacing = 8,
                Children =
                {
                    new Separator(),
                    new TextBlock { Text = "Stile e leggibilità del Coloring", FontSize = 19 },
                    new Border
                    {
                        Padding = new Thickness(10),
                        BorderBrush = Brushes.Gray,
                        BorderThickness = new Thickness(1),
                        Child = new TextBlock
                        {
                            Text = "Vincolo fisso Coloring: SOLO 2 COLORI — nero puro (#000000) e bianco puro (#FFFFFF). Nessun grigio, colore, ombra o sfumatura.",
                            TextWrapping = TextWrapping.Wrap
                        }
                    },
                    Labeled("Stile", style),
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { Labeled("Pubblico", audience), Labeled("Difficoltà", difficulty) } },
                    Labeled("Spessore linee", line),
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { Labeled("Complessità", complexity), Labeled("Densità", density) } },
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { Labeled("Sfondo", background), Labeled("Spazio bianco", white) } },
                    new StackPanel { Spacing = 4, Children = { closed, noTiny, clean, noText, separated } },
                    Labeled("Note stile (facoltative)", notes)
                }
            };
        }

        var profile = ImageCollectionPromptProfileService.Load(project);
        var use = Combo("ImageCollectionEditorialUse", ImageCollectionPromptProfileService.EditorialUses, profile.EditorialUse, 330);
        var color = Combo("ImageCollectionColorMode", ImageCollectionPromptProfileService.ColorModes, profile.ColorMode, 330);
        var detail = Combo("ImageCollectionDetailLevel", ImageCollectionPromptProfileService.DetailLevels, profile.DetailLevel, 190);
        var lineTreatment = Combo("ImageCollectionLineTreatment", ImageCollectionPromptProfileService.LineTreatments, profile.LineTreatment, 290);
        var rendering = Combo("ImageCollectionRenderingStyle", ImageCollectionPromptProfileService.RenderingStyles, profile.RenderingStyle, 270);
        var backgroundMode = Combo("ImageCollectionBackground", ImageCollectionPromptProfileService.Backgrounds, profile.Background, 280);
        var viewpoint = Combo("ImageCollectionViewpoint", ImageCollectionPromptProfileService.Viewpoints, profile.Viewpoint, 310);
        var readable = Check("Soggetto sempre chiaramente leggibile", profile.KeepSubjectReadable);
        var noTextInside = Check("Evita testo/etichette dentro l'immagine salvo richiesta", profile.AvoidTextInsideImage);
        var clarity = Check("Priorità alla chiarezza editoriale", profile.EditorialClarity);
        var sameScale = Check("Mantieni scala/inquadratura comparabili nelle serie", profile.SameScaleWhenSeries);
        var profileNotes = Editor("ImageCollectionNotes", profile.Notes, 80,
            "Note aggiuntive sul tipo di illustrazione o sulla serie.");

        save = () =>
        {
            profile.SubjectDescription = subject.Text ?? string.Empty;
            profile.EnvironmentDescription = environment.Text ?? string.Empty;
            profile.EditorialUse = use.SelectedItem?.ToString() ?? profile.EditorialUse;
            profile.ColorMode = color.SelectedItem?.ToString() ?? profile.ColorMode;
            profile.DetailLevel = detail.SelectedItem?.ToString() ?? profile.DetailLevel;
            profile.LineTreatment = lineTreatment.SelectedItem?.ToString() ?? profile.LineTreatment;
            profile.RenderingStyle = rendering.SelectedItem?.ToString() ?? profile.RenderingStyle;
            profile.Background = backgroundMode.SelectedItem?.ToString() ?? profile.Background;
            profile.Viewpoint = viewpoint.SelectedItem?.ToString() ?? profile.Viewpoint;
            profile.KeepSubjectReadable = readable.IsChecked == true;
            profile.AvoidTextInsideImage = noTextInside.IsChecked == true;
            profile.EditorialClarity = clarity.IsChecked == true;
            profile.SameScaleWhenSeries = sameScale.IsChecked == true;
            profile.Notes = profileNotes.Text ?? string.Empty;
            ImageCollectionPromptProfileService.Save(project, profile);
        };

        return new StackPanel
        {
            Name = "DiezNativeImageCollectionProfile",
            Spacing = 8,
            Children =
            {
                new Separator(),
                new TextBlock { Text = string.Equals(type, BookTypeProfileService.IllustratedBook, StringComparison.OrdinalIgnoreCase) ? "Profilo delle illustrazioni del Libro illustrato" : "Profilo della Raccolta immagini", FontSize = 19 },
                Labeled("Uso editoriale", use),
                Labeled("Resa cromatica", color),
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { Labeled("Dettaglio", detail), Labeled("Linee / contorno", lineTreatment) } },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { Labeled("Stile resa", rendering), Labeled("Sfondo", backgroundMode) } },
                Labeled("Punto di vista", viewpoint),
                new StackPanel { Spacing = 4, Children = { readable, noTextInside, clarity, sameScale } },
                Labeled("Note (facoltative)", profileNotes)
            }
        };
    }

    private static string BuildMasterPrompt(PreviewProject project, object host, int count, string? mustDo, string? mustNotDo)
    {
        var type = BookTypeProfileService.Get(project);
        var sb = new StringBuilder();
        var common = (project.AiProduction?.ProjectBrief ?? string.Empty).Trim();
        if (common.Length > 0) sb.AppendLine("REGOLE COMUNI DEL PROGETTO:").AppendLine(common).AppendLine();
        sb.AppendLine($"Crea {count} {(count == 1 ? "immagine" : "immagini")} per {VisualTypeLabel(type)}.").AppendLine();
        sb.AppendLine("DEVE FARE:").AppendLine((mustDo ?? string.Empty).Trim()).AppendLine();
        sb.AppendLine("NON DEVE FARE:").AppendLine((mustNotDo ?? string.Empty).Trim());
        if (ReadHostBool(host, "Consistent"))
            sb.AppendLine().AppendLine("CONSISTENT:").AppendLine(ReadHostString(host, "Rules"));
        sb.AppendLine().AppendLine(string.Equals(type, BookTypeProfileService.ColoringBook, StringComparison.OrdinalIgnoreCase)
            ? BookTypePromptProfileService.BuildColoringBlock(BookTypePromptProfileService.LoadColoring(project))
            : ImageCollectionPromptProfileService.BuildPromptBlock(project));
        sb.AppendLine().AppendLine(SingleWindowImageSpecsUi.BuildPromptBlock(project));
        sb.AppendLine().AppendLine("Ogni immagine deve essere distinta e non deve contenere ID, numeri o nomi file dentro l'immagine.");
        return sb.ToString().Trim();
    }

    private static void SyncHostPromptState(object host, int count, string? mustDo, string? mustNotDo, string? prompt)
    {
        SetHostString(host, "Count", count.ToString());
        SetHostString(host, "MustDo", mustDo ?? string.Empty);
        SetHostString(host, "MustNotDo", mustNotDo ?? string.Empty);
        SetHostString(host, "Prompt", prompt ?? string.Empty);
    }

    private static (string Subject, string Environment) LoadDescriptions(PreviewProject project, string type)
    {
        if (string.Equals(type, BookTypeProfileService.ColoringBook, StringComparison.OrdinalIgnoreCase))
        {
            var p = BookTypePromptProfileService.LoadColoring(project);
            return (p.SubjectDescription, p.EnvironmentDescription);
        }
        var i = ImageCollectionPromptProfileService.Load(project);
        return (i.SubjectDescription, i.EnvironmentDescription);
    }

    private static int CountValue(NumericUpDown count) => Math.Clamp((int)(count.Value ?? 1), 1, 500);

    private static bool IsVisualType(string type) =>
        string.Equals(type, BookTypeProfileService.ColoringBook, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, BookTypeProfileService.ImageCollection, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, BookTypeProfileService.IllustratedBook, StringComparison.OrdinalIgnoreCase);

    private static string VisualTypeLabel(string type) => type switch
    {
        var t when string.Equals(t, BookTypeProfileService.ImageCollection, StringComparison.OrdinalIgnoreCase) => "Raccolta immagini",
        var t when string.Equals(t, BookTypeProfileService.IllustratedBook, StringComparison.OrdinalIgnoreCase) => "Libro illustrato · Illustrazioni",
        _ => "Coloring Book"
    };

    private static List<AiProductionJob> ImageJobs(PreviewProject project) => project.AiProductionJobs
        .Where(j => string.Equals(j.OutputType, AiProductionService.TypeImage, StringComparison.OrdinalIgnoreCase))
        .OrderBy(j => j.Code, StringComparer.OrdinalIgnoreCase).ToList();

    private static Control CollectionPreview(string type, int count, bool consistent) => new StackPanel
    {
        Spacing = 10,
        Children =
        {
            new TextBlock { Text = VisualTypeLabel(type), FontSize = 22 },
            new TextBlock { Text = $"Posizioni immagine: {count}" },
            new TextBlock { Text = consistent ? "Consistent: ON" : "Consistent: OFF" },
            new TextBlock { Text = "Paradigmi e risultati selezionati compariranno qui senza aprire un'altra finestra Diez.", TextWrapping = TextWrapping.Wrap }
        }
    };

    private static TextBox Editor(string name, string text, double height, string watermark) => new()
    {
        Name = name,
        Text = text,
        Height = height,
        MinHeight = height,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        Watermark = watermark,
        IsReadOnly = false,
        IsEnabled = true,
        IsHitTestVisible = true,
        Focusable = true,
        IsUndoEnabled = true,
        Background = Brushes.White,
        Foreground = Brushes.Black,
        BorderBrush = Brushes.Gray,
        BorderThickness = new Thickness(2),
        Padding = new Thickness(9, 7),
        HorizontalAlignment = HorizontalAlignment.Stretch
    };

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
        Spacing = 4,
        Children = { new TextBlock { Text = label, FontSize = 15, TextWrapping = TextWrapping.Wrap }, control }
    };

    private static Button Button(string text, double width) => new()
    {
        Content = text,
        Width = width,
        HorizontalContentAlignment = HorizontalAlignment.Center
    };

    private static Control Preview(string text) => new Border
    {
        Padding = new Thickness(18),
        Child = new TextBlock { Text = text, FontSize = 17, TextWrapping = TextWrapping.Wrap }
    };

    private static void Push(object host, string title, Control content, Control preview, string status) =>
        SingleWindowEntryPointUi.Invoke(host, "Push", title, content, preview, status);

    private static void ClearHistory(object host)
    {
        if (host.GetType().GetField("_history", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host) is IList history)
            history.Clear();
    }

    private static object? HostColoring(object host) =>
        host.GetType().GetField("_coloring", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host);

    private static string ReadHostString(object host, string property) =>
        HostColoring(host)?.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public)?.GetValue(HostColoring(host))?.ToString() ?? string.Empty;

    private static bool ReadHostBool(object host, string property) =>
        HostColoring(host)?.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public)?.GetValue(HostColoring(host)) is bool value && value;

    private static void SetHostString(object host, string property, string value)
    {
        var state = HostColoring(host);
        state?.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public)?.SetValue(state, value);
    }

    private static void SetHostBool(object host, string property, bool value)
    {
        var state = HostColoring(host);
        state?.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public)?.SetValue(state, value);
    }

    private static T? Field<T>(object host, string name) where T : class =>
        host.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host) as T;

    private static bool TrySession(MainWindow window, out PreviewProject project, out string path)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        project = typeof(MainWindow).GetField("_project", flags)?.GetValue(window) as PreviewProject ?? null!;
        path = typeof(MainWindow).GetField("_currentProjectPath", flags)?.GetValue(window) as string ?? string.Empty;
        return project is not null && !string.IsNullOrWhiteSpace(path);
    }

    private static async Task InvokeMainTaskAsync(MainWindow window, string methodName)
    {
        var method = typeof(MainWindow).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(typeof(MainWindow).Name, methodName);
        if (method.Invoke(window, null) is Task task) await task;
    }

    private static void Report(MainWindow window, object host, string text)
    {
        if (Field<TextBlock>(host, "_status") is TextBlock status) status.Text = text;
        if (typeof(MainWindow).GetField("_status", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(window) is TextBlock mainStatus)
            mainStatus.Text = text;
    }

    private static void StyleCurrentPage(MainWindow window)
    {
        object host;
        try { host = SingleWindowEntryPointUi.GetHost(window); }
        catch { return; }
        if (Field<ContentControl>(host, "_pageHost")?.Content is not Control page) return;
        foreach (var box in Descendants(page).OfType<TextBox>())
        {
            if (!box.IsEnabled || box.IsReadOnly) continue;
            box.Opacity = 1;
            box.Background = Brushes.White;
            box.Foreground = Brushes.Black;
            box.BorderBrush = Brushes.Gray;
            box.BorderThickness = new Thickness(2);
            box.Padding = new Thickness(9, 7);
            box.MinHeight = Math.Max(box.MinHeight, box.AcceptsReturn ? 70 : 38);
            if (double.IsNaN(box.Width)) box.HorizontalAlignment = HorizontalAlignment.Stretch;
        }
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

    private sealed class NativeConsistencyEditor
    {
        private const string PaletteKey = "palette";
        private const string User = "USER";
        private const string Ai = "AI";
        private const string Mixed = "MIXED";
        private readonly bool _coloring;
        private readonly Criterion[] _criteria;
        private readonly Dictionary<string, string> _levels = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _strategies = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _variations = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ComboBox> _levelControls = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ComboBox> _strategyControls = new(StringComparer.Ordinal);
        private readonly Dictionary<string, TextBox> _variationControls = new(StringComparer.Ordinal);
        private readonly Dictionary<string, TextBlock> _strategyLabels = new(StringComparer.Ordinal);
        private Button? _next;
        private bool _enabled;
        private string _notes = string.Empty;

        private static readonly LevelChoice[] Levels =
        [
            new("LOCKED", "Da mantenere"),
            new("PREFERRED", "Preferibilmente coerente"),
            new("FREE", "Può variare")
        ];

        private static readonly StrategyChoice[] Strategies =
        [
            new(User, "La definisco io"),
            new(Ai, "La decide l’AI"),
            new(Mixed, "Mista: do indicazioni e l’AI completa")
        ];

        public StackPanel Panel { get; }

        public NativeConsistencyEditor(string? existingRules, bool coloring)
        {
            _coloring = coloring;
            _criteria =
            [
                new("character", "Personaggio / soggetto ricorrente", "LOCKED", false),
                new("style", "Stile", "LOCKED", false),
                new(PaletteKey, coloring ? "Palette / colori — fissa B/N" : "Resa cromatica / palette", coloring ? "LOCKED" : "PREFERRED", coloring),
                new("line_detail", "Tratto / dettaglio", "LOCKED", false),
                new("environment_objects", "Ambientazioni / oggetti ricorrenti", "PREFERRED", false),
                new("composition", "Composizione / inquadratura", "PREFERRED", false)
            ];
            foreach (var c in _criteria)
            {
                _levels[c.Key] = c.Fixed ? "LOCKED" : c.DefaultLevel;
                _strategies[c.Key] = User;
            }
            Parse(existingRules);
            if (_coloring) _levels[PaletteKey] = "LOCKED";

            Panel = new StackPanel
            {
                Name = "DiezConsistencyCriteriaPanel",
                Spacing = 9,
                Margin = new Thickness(14, 4, 0, 6)
            };
            Panel.Children.Add(new TextBlock { Text = "Quali aspetti devono restare coerenti?", FontSize = 17 });
            Panel.Children.Add(new TextBlock
            {
                Text = "Per ogni criterio scegli il vincolo. Se scegli “Può variare”, puoi definirlo tu, affidarlo all’AI oppure dare indicazioni che l’AI completerà.",
                TextWrapping = TextWrapping.Wrap
            });

            foreach (var criterion in _criteria) AddCriterion(criterion);
            var notes = Editor("ConsistencyNotes", _notes, 88, "Note generali facoltative sulla coerenza della serie.");
            notes.TextChanged += (_, _) => { _notes = notes.Text ?? string.Empty; UpdateNext(); };
            Panel.Children.Add(Labeled("Note generali di coerenza (facoltative)", notes));
        }

        public void SetEnabled(bool enabled)
        {
            _enabled = enabled;
            Panel.IsVisible = enabled;
            UpdateNext();
        }

        public void BindNext(Button next)
        {
            _next = next;
            UpdateNext();
        }

        public bool Validate(out string error)
        {
            if (!_enabled)
            {
                error = string.Empty;
                return true;
            }
            var missing = _criteria
                .Where(c => !c.Fixed && _levels[c.Key] == "FREE" && _strategies[c.Key] != Ai)
                .Where(c => !_variations.TryGetValue(c.Key, out var value) || string.IsNullOrWhiteSpace(value))
                .Select(c => c.Label)
                .ToList();
            if (missing.Count == 0)
            {
                error = string.Empty;
                return true;
            }
            error = "Descrivi cosa può variare per: " + string.Join(", ", missing) + ". Oppure scegli “La decide l’AI”.";
            return false;
        }

        public string Serialize()
        {
            if (!_enabled) return string.Empty;
            if (_coloring) _levels[PaletteKey] = "LOCKED";
            var lines = new List<string>();
            foreach (var c in _criteria)
            {
                if (c.Fixed && c.Key == PaletteKey)
                {
                    lines.Add("Palette / colori: Da mantenere — fisso nero puro #000000 e bianco puro #FFFFFF");
                    continue;
                }
                var level = _levels[c.Key];
                var levelLabel = Levels.First(x => x.Level == level).Label;
                if (level != "FREE")
                {
                    lines.Add($"{c.Label}: {levelLabel}");
                    continue;
                }
                var strategy = _strategies[c.Key];
                var variation = _variations.TryGetValue(c.Key, out var v) ? v.Trim() : string.Empty;
                if (strategy == Ai)
                    lines.Add(string.IsNullOrWhiteSpace(variation)
                        ? $"{c.Label}: Può variare — chi decide: AI"
                        : $"{c.Label}: Può variare — chi decide: AI — indicazioni facoltative: {variation}");
                else if (strategy == Mixed)
                    lines.Add($"{c.Label}: Può variare — chi decide: MISTA — indicazioni: {variation}");
                else
                    lines.Add($"{c.Label}: Può variare — chi decide: UTENTE — variazione: {variation}");
            }
            if (!string.IsNullOrWhiteSpace(_notes)) lines.Add("Note: " + _notes.Trim());
            return string.Join(Environment.NewLine, lines);
        }

        private void AddCriterion(Criterion criterion)
        {
            var level = new ComboBox
            {
                Name = "ConsistencyLevel_" + criterion.Key,
                ItemsSource = criterion.Fixed ? new[] { Levels[0] } : Levels,
                Width = 230,
                HorizontalAlignment = HorizontalAlignment.Left,
                IsEnabled = !criterion.Fixed
            };
            level.SelectedItem = criterion.Fixed ? Levels[0] : Levels.First(x => x.Level == _levels[criterion.Key]);
            _levelControls[criterion.Key] = level;

            var strategyLabel = new TextBlock { Text = "Chi decide come può variare?", FontSize = 14 };
            var strategy = new ComboBox
            {
                Name = "ConsistencyVariationStrategy_" + criterion.Key,
                ItemsSource = Strategies,
                SelectedItem = Strategies.First(x => x.Strategy == _strategies[criterion.Key]),
                Width = 360,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            var variation = Editor("ConsistencyVariation_" + criterion.Key,
                _variations.TryGetValue(criterion.Key, out var saved) ? saved : string.Empty,
                80,
                VariationWatermark(_strategies[criterion.Key], criterion.Label));

            _strategyLabels[criterion.Key] = strategyLabel;
            _strategyControls[criterion.Key] = strategy;
            _variationControls[criterion.Key] = variation;

            void Refresh()
            {
                var free = !criterion.Fixed && _levels[criterion.Key] == "FREE";
                strategyLabel.IsVisible = free;
                strategy.IsVisible = free;
                variation.IsVisible = free;
            }

            level.SelectionChanged += (_, _) =>
            {
                if (level.SelectedItem is LevelChoice selected) _levels[criterion.Key] = selected.Level;
                Refresh();
                UpdateNext();
            };
            strategy.SelectionChanged += (_, _) =>
            {
                if (strategy.SelectedItem is StrategyChoice selected)
                {
                    _strategies[criterion.Key] = selected.Strategy;
                    variation.Watermark = VariationWatermark(selected.Strategy, criterion.Label);
                }
                UpdateNext();
            };
            variation.TextChanged += (_, _) =>
            {
                _variations[criterion.Key] = variation.Text ?? string.Empty;
                UpdateNext();
            };

            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10 };
            var label = new TextBlock { Text = criterion.Label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(level, 1);
            row.Children.Add(label);
            row.Children.Add(level);
            Panel.Children.Add(new StackPanel { Spacing = 5, Children = { row, strategyLabel, strategy, variation } });
            Refresh();
        }

        private void UpdateNext()
        {
            if (_next is null) return;
            _next.IsEnabled = Validate(out var error);
            ToolTip.SetTip(_next, string.IsNullOrWhiteSpace(error) ? null : error);
        }

        private void Parse(string? rules)
        {
            if (string.IsNullOrWhiteSpace(rules)) return;
            foreach (var line in rules.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (line.StartsWith("Note:", StringComparison.OrdinalIgnoreCase))
                {
                    _notes = line[5..].Trim();
                    continue;
                }
                var criterion = _criteria.FirstOrDefault(c => line.StartsWith(c.Label + ":", StringComparison.OrdinalIgnoreCase) ||
                    (c.Key == PaletteKey && line.StartsWith("Palette / colori:", StringComparison.OrdinalIgnoreCase)));
                if (criterion is null || criterion.Fixed) continue;
                if (line.Contains("Può variare", StringComparison.OrdinalIgnoreCase)) _levels[criterion.Key] = "FREE";
                else if (line.Contains("Preferibilmente coerente", StringComparison.OrdinalIgnoreCase)) _levels[criterion.Key] = "PREFERRED";
                else if (line.Contains("Da mantenere", StringComparison.OrdinalIgnoreCase)) _levels[criterion.Key] = "LOCKED";

                if (_levels[criterion.Key] != "FREE") continue;
                if (line.Contains("chi decide: AI", StringComparison.OrdinalIgnoreCase)) _strategies[criterion.Key] = Ai;
                else if (line.Contains("chi decide: MISTA", StringComparison.OrdinalIgnoreCase)) _strategies[criterion.Key] = Mixed;
                else _strategies[criterion.Key] = User;
                foreach (var marker in new[] { "— variazione:", "— indicazioni:", "— indicazioni facoltative:" })
                {
                    var index = line.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                    if (index >= 0)
                    {
                        _variations[criterion.Key] = line[(index + marker.Length)..].Trim();
                        break;
                    }
                }
            }
        }

        private static string VariationWatermark(string strategy, string label) => strategy switch
        {
            Ai => $"Facoltativo: dai preferenze o limiti per “{label}”. Se lasci vuoto, decide l’AI entro gli altri vincoli.",
            Mixed => $"Obbligatorio: indica cosa vuoi guidare per “{label}”; l’AI completa ciò che non specifichi.",
            _ => $"Obbligatorio: descrivi tu cosa può variare e come per “{label}”."
        };

        private sealed record Criterion(string Key, string Label, string DefaultLevel, bool Fixed);
        private sealed record LevelChoice(string Level, string Label) { public override string ToString() => Label; }
        private sealed record StrategyChoice(string Strategy, string Label) { public override string ToString() => Label; }
    }
}
