from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[1]


def must_replace(text: str, old: str, new: str, label: str) -> str:
    if old not in text:
        raise SystemExit(f"Missing expected source block: {label}")
    return text.replace(old, new, 1)


def must_sub(text: str, pattern: str, replacement: str, label: str) -> str:
    new_text, count = re.subn(pattern, replacement, text, count=1, flags=re.S)
    if count != 1:
        raise SystemExit(f"Expected one match for {label}, found {count}")
    return new_text


# -----------------------------------------------------------------------------
# Publisher shell: no outer Production tabs, Italian material controls.
# -----------------------------------------------------------------------------
shell_path = ROOT / "src/Diez.Uno/DiezPublisherShellHost.cs"
shell = shell_path.read_text(encoding="utf-8")

shell = must_replace(
    shell,
    'NavButton("Produzione", ShowProduction)',
    'NavButton("Produzione AI", ShowProduction)',
    "sidebar Produzione AI",
)

shell = must_sub(
    shell,
    r"    private void RewrapVisualWorkspaceIfNeeded\(\)\n    \{.*?\n    \}\n\n    private static bool LooksLikeVisualWorkspace",
    '''    private void RewrapVisualWorkspaceIfNeeded()
    {
        if (_rewrapQueued || !string.Equals(_activeSection, "production", StringComparison.Ordinal)) return;
        var document = Document;
        if (document is null || !BookTypeCatalog.IsVisual(BookTypeCatalog.Normalize(document.BookType))) return;
        if (ContentHost?.Content is not StackPanel raw || !LooksLikeVisualWorkspace(raw)) return;

        var transient = PublisherProjectState.ReadUiInt(document, "Visual.ActivePhase", 0);
        var id = PublisherProjectState.ProjectId(document);
        if (transient is >= 1 and <= 4) _visualPhaseByProject[id] = transient;
        if (PublisherProjectState.RemoveUiKey(document, "Visual.ActivePhase"))
            _ = SaveTransientCleanupAsync();
    }

    private static bool LooksLikeVisualWorkspace''',
    "visual rewrap without outer tabs",
)

shell = must_sub(
    shell,
    r"    private void ShowVisualProductionTab\(int selected\)\n    \{.*?\n    \}\n\n    private void ShowReview\(\)",
    '''    private void ShowVisualProductionTab(int selected)
    {
        var document = Document;
        if (document is null) return;
        selected = Math.Clamp(selected, 1, 4);
        var projectId = PublisherProjectState.ProjectId(document);
        _visualPhaseByProject[projectId] = selected;

        // Visual.ActivePhase is only a transient handoff to the existing workspace builder.
        // It is removed immediately after the workspace has consumed it and must never become
        // canonical project/editorial state.
        document.SetUiInt("Visual.ActivePhase", selected);
        Invoke("ShowVisualWorkspace");
        if (PublisherProjectState.RemoveUiKey(document, "Visual.ActivePhase"))
            _ = SaveTransientCleanupAsync();
    }

    private void ShowReview()''',
    "four-phase visual production navigation",
)

shell = must_replace(
    shell,
    '''        var policy = Combo(["ALLOW", "REFERENCE_ONLY", "DIRECT_ASSET", "NEVER_SEND"], current.AiUsePolicy);
        var fidelity = Combo(["EXACT", "CLOSE", "GUIDED", "LOOSE", "NOT_APPLICABLE"], current.Fidelity);
        var info = new TextBlock { TextWrapping = TextWrapping.Wrap };
''',
    '''        var policyChoices = new[]
        {
            new PublisherChoice("ALLOW", "Può usarlo e modificarlo", "L'AI può usare questo materiale come input operativo e trasformarlo secondo il ruolo e le istruzioni definite."),
            new PublisherChoice("REFERENCE_ONLY", "Usalo solo come riferimento", "Il materiale guida identità, stile, composizione, ambiente o contenuto, ma non viene trattato automaticamente come asset da copiare o inserire nel libro."),
            new PublisherChoice("DIRECT_ASSET", "Usa il file direttamente", "Il file è già un asset editoriale: resta nel progetto/libro e non deve essere rigenerato automaticamente dall'AI."),
            new PublisherChoice("NEVER_SEND", "Non inviare all'AI", "Il materiale resta disponibile nel progetto, ma viene escluso dai Prompt Pack e dagli input inviati all'AI.")
        };
        var fidelityChoices = new[]
        {
            new PublisherChoice("EXACT", "Da rispettare esattamente", "Dati, termini, struttura o contenuto indicati sono vincolanti e non vanno reinterpretati liberamente."),
            new PublisherChoice("CLOSE", "Molto fedele", "Mantieni identità e caratteristiche molto vicine al materiale originale, salvo le modifiche esplicitamente richieste."),
            new PublisherChoice("GUIDED", "Fedele ma guidata", "Usa il materiale come base riconoscibile, con libertà controllata dalle istruzioni editoriali dell'utente."),
            new PublisherChoice("LOOSE", "Solo ispirazione", "Conta l'idea, lo stile o l'atmosfera generale; non è richiesta una replica fedele del contenuto."),
            new PublisherChoice("NOT_APPLICABLE", "Non applicabile", "Per questo ruolo non ha senso definire un livello di fedeltà.")
        };
        var policy = PublisherChoiceCombo(policyChoices, current.AiUsePolicy);
        var fidelity = PublisherChoiceCombo(fidelityChoices, current.Fidelity);
        var info = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var policyInfo = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var fidelityInfo = new TextBlock { TextWrapping = TextWrapping.Wrap };

        void RefreshChoiceDescriptions()
        {
            policyInfo.Text = (policy.SelectedItem as PublisherChoice)?.Description ?? string.Empty;
            fidelityInfo.Text = (fidelity.SelectedItem as PublisherChoice)?.Description ?? string.Empty;
        }
        policy.SelectionChanged += (_, _) => RefreshChoiceDescriptions();
        fidelity.SelectionChanged += (_, _) => RefreshChoiceDescriptions();
''',
    "Italian AI use and fidelity choices",
)

shell = must_replace(
    shell,
    '''            policy.SelectedItem = selected.DefaultPolicy;
            fidelity.SelectedItem = selected.DefaultFidelity;
''',
    '''            SelectPublisherChoice(policy, selected.DefaultPolicy);
            SelectPublisherChoice(fidelity, selected.DefaultFidelity);
            RefreshChoiceDescriptions();
''',
    "role defaults use internal codes",
)

shell = must_replace(
    shell,
    '''        info.Text = (intent.SelectedItem as IntentChoice)?.Description ?? string.Empty;
''',
    '''        info.Text = (intent.SelectedItem as IntentChoice)?.Description ?? string.Empty;
        RefreshChoiceDescriptions();
''',
    "initial material descriptions",
)

shell = must_replace(
    shell,
    '''                policy.SelectedItem?.ToString() ?? selected.DefaultPolicy,
                fidelity.SelectedItem?.ToString() ?? selected.DefaultFidelity);
''',
    '''                SelectedPublisherCode(policy, selected.DefaultPolicy),
                SelectedPublisherCode(fidelity, selected.DefaultFidelity));
''',
    "persist internal material codes",
)

shell = must_replace(
    shell,
    '''                Horizontal(Labeled("Uso AI", policy), Labeled("Fedeltà", fidelity)),
                new TextBlock { Text = "“Archivio / non inviare all'AI” e “Asset diretto” restano nel progetto ma non devono entrare silenziosamente nei Prompt Pack di generazione.", TextWrapping = TextWrapping.Wrap },
''',
    '''                Horizontal(
                    Vertical(Labeled("Uso dell'AI", policy), policyInfo),
                    Vertical(Labeled("Fedeltà", fidelity), fidelityInfo)),
                new TextBlock { Text = "“Non inviare all'AI” conserva il materiale nel progetto ma lo esclude dai Prompt Pack. “Usa il file direttamente” indica invece un asset editoriale già pronto, che non va rigenerato automaticamente.", TextWrapping = TextWrapping.Wrap },
''',
    "material descriptions under selectors",
)

shell = must_replace(
    shell,
    '''    private static IReadOnlyList<IntentChoice> IntentChoices(string kind)
''',
    '''    private static ComboBox PublisherChoiceCombo(IEnumerable<PublisherChoice> values, string selectedCode)
    {
        var items = values.ToList();
        var combo = new ComboBox { ItemsSource = items, MinWidth = 250, HorizontalAlignment = HorizontalAlignment.Left };
        combo.SelectedItem = items.FirstOrDefault(x => string.Equals(x.Code, selectedCode, StringComparison.OrdinalIgnoreCase)) ?? items.FirstOrDefault();
        return combo;
    }

    private static void SelectPublisherChoice(ComboBox combo, string code)
    {
        if (combo.ItemsSource is not IEnumerable<PublisherChoice> source) return;
        combo.SelectedItem = source.FirstOrDefault(x => string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase)) ?? source.FirstOrDefault();
    }

    private static string SelectedPublisherCode(ComboBox combo, string fallback) =>
        combo.SelectedItem is PublisherChoice selected ? selected.Code : fallback;

    private static IReadOnlyList<IntentChoice> IntentChoices(string kind)
''',
    "publisher choice helpers",
)

shell = must_replace(
    shell,
    '''    private sealed record IntentChoice(string Code, string Label, string Description, string DefaultPolicy, string DefaultFidelity)
''',
    '''    private sealed record PublisherChoice(string Code, string Label, string Description)
    {
        public override string ToString() => Label;
    }

    private sealed record IntentChoice(string Code, string Label, string Description, string DefaultPolicy, string DefaultFidelity)
''',
    "publisher choice record",
)

shell_path.write_text(shell, encoding="utf-8")


# -----------------------------------------------------------------------------
# Visual workspace: four clickable ovals, Scene/Soggetti pre-prompt, HARD fields
# at the end of Definition, Prompt phase consumes the saved Definition.
# -----------------------------------------------------------------------------
visual_path = ROOT / "src/Diez.Uno/VisualBookWorkspace.cs"
visual = visual_path.read_text(encoding="utf-8")

visual = must_replace(
    visual,
    '''        root.Children.Add(PhaseStrip(phase));

        async Task GoToPhaseAsync(int target)
        {
            document.SetUiInt("Visual.ActivePhase", Math.Clamp(target, 1, 4));
            await save();
            refresh();
        }
''',
    '''        async Task GoToPhaseAsync(int target)
        {
            // Navigation is workspace/session state only. The shell consumes this transient value
            // to render the requested phase and removes it from project persistence immediately.
            document.SetUiInt("Visual.ActivePhase", Math.Clamp(target, 1, 4));
            refresh();
            await Task.CompletedTask;
        }

        root.Children.Add(PhaseStrip(phase, GoToPhaseAsync));
''',
    "clickable phase strip handoff",
)

visual = must_sub(
    visual,
    r"        var count = NumberInput\(Math.Clamp\(initialCount, 1, 500\), 1, 500, 1, 190\);.*?        root.Children.Add\(Card\(\"1/4 · Definizione del libro e Consistent\", planPanel\)\);",
    '''        var count = NumberInput(Math.Clamp(initialCount, 1, 500), 1, 500, 1, 190);
        var subject = Editor(FirstNonBlank(setup.Subject, document.GetUiString("Visual.Subject")), "Soggetto/i principali", 100);
        var environment = Editor(FirstNonBlank(setup.Environment, document.GetUiString("Visual.Environment")), "Ambientazione generica / scenario", 100);
        var consistent = Check("Consistent — mantieni coerenti soggetti, stile e regole fra le immagini", setup.Consistent || document.GetUiBool("Visual.Consistent"));
        var consistencyRules = Editor(
            FirstNonBlank(setup.ConsistencyRules, document.GetUiString("Visual.ConsistencyRules")),
            "Regole generali Consistent: proporzioni, stile, palette, elementi ricorrenti…",
            90);

        var sceneState = document.ReadVisualSceneState();
        var structuredInitial = sceneState.ScenesEnabled || sceneState.MultiSubjectEnabled;
        var genericMode = new RadioButton
        {
            GroupName = "VisualContentAuthoringMode",
            Content = "Soggetto + ambientazione generici",
            IsChecked = !structuredInitial
        };
        var structuredMode = new RadioButton
        {
            GroupName = "VisualContentAuthoringMode",
            Content = "Scene + soggetti strutturati",
            IsChecked = structuredInitial
        };
        var genericPanel = Vertical(
            Labeled("Soggetto/i", subject),
            Labeled("Ambientazione generica", environment),
            new TextBlock
            {
                Text = "Usa questa modalità quando una descrizione generale è sufficiente per l'intera serie.",
                TextWrapping = TextWrapping.Wrap
            });
        var structuredConsistency = BuildStructuredConsistencyEditor(document, save, report, refresh);

        void RefreshContentMode()
        {
            genericPanel.Visibility = genericMode.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            structuredConsistency.Visibility = structuredMode.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }
        genericMode.Checked += async (_, _) =>
        {
            RefreshContentMode();
            var state = document.ReadVisualSceneState();
            if (state.MultiSubjectEnabled) document.ConfigureVisualSubjects(false, Math.Max(1, state.SubjectCount));
            if (state.ScenesEnabled) document.ConfigureVisualScenes(false, Math.Max(1, state.SceneCount));
            await save();
            refresh();
        };
        structuredMode.Checked += async (_, _) =>
        {
            RefreshContentMode();
            var state = document.ReadVisualSceneState();
            if (!state.MultiSubjectEnabled) document.ConfigureVisualSubjects(true, Math.Max(1, state.SubjectCount));
            state = document.ReadVisualSceneState();
            if (!state.ScenesEnabled) document.ConfigureVisualScenes(true, Math.Max(1, state.SceneCount));
            await save();
            refresh();
        };
        RefreshContentMode();

        var planPanel = Vertical(
            Labeled("Numero esatto di immagini", count),
            new TextBlock
            {
                Text = "Usa le frecce del contatore oppure digita direttamente un valore da 1 a 500.",
                TextWrapping = TextWrapping.Wrap
            },
            new TextBlock { Text = "Come vuoi definire il contenuto?", FontSize = 17, TextWrapping = TextWrapping.Wrap },
            WrapRow(genericMode, structuredMode),
            genericPanel,
            structuredConsistency,
            new Separator(),
            consistent,
            Labeled("Regole Consistent generali", consistencyRules),
            new TextBlock
            {
                Text = "Consistent si combina con la modalità scelta: può mantenere identità, stile e altri LOCK fra immagini e anche fra lotti diversi, senza obbligare a ripetere posa o composizione se non richiesto.",
                TextWrapping = TextWrapping.Wrap
            });
        root.Children.Add(Card("1/4 · Definizione contenuto e Consistent", planPanel));''',
    "phase one content authoring mode",
)

visual = must_replace(
    visual,
    '''        async Task<bool> SaveSetupAsync()
''',
    '''        var mustDo = Editor(document.GetUiString("Prompt.MustDo"), "Cosa DEVE essere presente o rispettato. Ogni istruzione qui è HARD.", 130);
        var mustNot = Editor(document.GetUiString("Prompt.MustNotDo"), "Cosa NON DEVE comparire o accadere. Ogni esclusione qui è HARD.", 130);
        root.Children.Add(Card("Ultimi vincoli prima del Prompt · HARD", Vertical(
            new TextBlock
            {
                Text = "Questi sono gli ultimi campi della Definizione. Diez li inserisce nel Prompt come USER REQUIREMENT / USER EXCLUSION HARD: non sono semplici note o preferenze.",
                TextWrapping = TextWrapping.Wrap
            },
            Labeled("DEVE FARE · HARD", mustDo),
            Labeled("NON DEVE FARE · HARD", mustNot))));

        async Task<bool> SaveSetupAsync()
''',
    "hard fields at end of definition",
)

visual = must_replace(
    visual,
    '''            document.SetUiString("Visual.ConsistencyRules", consistent.IsChecked == true ? consistencyRules.Text : string.Empty);
            await save();
''',
    '''            document.SetUiString("Visual.ConsistencyRules", consistent.IsChecked == true ? consistencyRules.Text : string.Empty);
            document.SetUiString("Prompt.MustDo", mustDo.Text);
            document.SetUiString("Prompt.MustNotDo", mustNot.Text);
            await save();
''',
    "persist hard fields with definition",
)

visual = must_sub(
    visual,
    r"    private static void BuildPhaseTwo\(.*?\n    \}\n\n    private static void BuildPhaseThree\(",
    '''    private static void BuildPhaseTwo(
        StackPanel root,
        DiezProjectDocument document,
        Func<Task> save,
        Action<string> report,
        Action refresh,
        Func<int, Task> goToPhase)
    {
        var provider = Combo(["ChatGPT / OpenAI", "Gemini", "Altra / nuova AI"], document.GetUiString("AI.Provider", "ChatGPT / OpenAI"));
        var advanced = Check("Usa il modello immagini più avanzato disponibile", document.GetUiBool("AI.PreferAdvanced", true));
        var promptPreview = Editor(string.Empty, "Prompt compilato", 420);
        promptPreview.IsReadOnly = true;

        void CompilePreview()
        {
            try
            {
                var pack = document.BuildVisualPromptPack(
                    document.GetUiString("Prompt.MustDo"),
                    document.GetUiString("Prompt.MustNotDo"),
                    ProviderId(provider.SelectedItem?.ToString()),
                    advanced.IsChecked == true);
                promptPreview.Text = pack.MasterPrompt;
                report($"Prompt compilato dalla Definizione: {pack.Items.Count} posizioni visuali.");
            }
            catch (Exception ex)
            {
                promptPreview.Text = "Prompt non disponibile: " + ex.GetBaseException().Message;
            }
        }
        CompilePreview();

        root.Children.Add(Card("2/4 · Prompt", Vertical(
            new TextBlock
            {
                Text = "Il Prompt deriva dalle scelte della fase Definizione: profilo del tipo libro, modalità generica oppure Scene/Soggetti, Consistent e i vincoli DEVE FARE / NON DEVE FARE HARD. Per cambiarne il contenuto torna alla Definizione e ricompila.",
                TextWrapping = TextWrapping.Wrap
            },
            WrapRow(Labeled("Provider AI", provider), advanced),
            Labeled("Prompt compilato", promptPreview),
            WrapRow(
                ActionButton("Copia Prompt", () => Copy(promptPreview.Text ?? string.Empty)),
                ActionButton("Ricompila dalla Definizione", CompilePreview)))));

        async Task SavePromptPreparationAsync()
        {
            document.SetUiString("AI.Provider", provider.SelectedItem?.ToString());
            document.SetUiBool("AI.PreferAdvanced", advanced.IsChecked == true);
            document.BuildVisualPromptPack(
                document.GetUiString("Prompt.MustDo"),
                document.GetUiString("Prompt.MustNotDo"),
                ProviderId(provider.SelectedItem?.ToString()),
                advanced.IsChecked == true);
            await save();
        }

        root.Children.Add(NavigationRow(
            AsyncButton("← Indietro", async () => await goToPhase(1)),
            AsyncButton("Continua → Produzione AI", async () =>
            {
                await SavePromptPreparationAsync();
                report("Prompt aggiornato dalla Definizione e pronto per la Produzione AI.");
                await goToPhase(3);
            })));
    }

    private static void BuildPhaseThree(''',
    "phase two compiled prompt",
)

visual = visual.replace("3/4 · Prompt Pack e produzione", "3/4 · Prompt Pack e Produzione AI")
visual = visual.replace("Produzione con AI", "Produzione AI")
visual = visual.replace("3 · Produzione", "3 · Produzione AI")

visual = must_sub(
    visual,
    r"    private static UIElement PhaseStrip\(int active\)\n    \{.*?\n    \}\n\n    private static Border Card",
    '''    private static UIElement PhaseStrip(int active, Func<int, Task> goToPhase)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var names = new[] { "1 · Definizione", "2 · Prompt", "3 · Produzione AI", "4 · Revisione" };
        for (var i = 0; i < names.Length; i++)
        {
            var target = i + 1;
            var button = new Button
            {
                Content = (target == active ? "● " : "○ ") + names[i],
                Padding = new Thickness(12, 7),
                BorderThickness = new Thickness(target == active ? 2 : 1),
                CornerRadius = new CornerRadius(18),
                MinHeight = 38,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            ToolTipService.SetToolTip(button, $"Vai alla fase {target}: {names[i]}");
            button.Click += async (_, _) =>
            {
                if (target == active) return;
                await goToPhase(target);
            };
            panel.Children.Add(button);
        }
        return panel;
    }

    private static Border Card''',
    "clickable four oval phase navigation",
)

visual_path.write_text(visual, encoding="utf-8")


# -----------------------------------------------------------------------------
# Guide: explicit Round 3 validation checklist.
# -----------------------------------------------------------------------------
guide_path = ROOT / "docs/GUIDA_DIEZ.md"
if guide_path.exists():
    guide = guide_path.read_text(encoding="utf-8")
    marker = "## Round 3 · verifica del nuovo flusso Produzione AI"
    if marker not in guide:
        guide += '''\n\n---\n\n## Round 3 · verifica del nuovo flusso Produzione AI\n\nQuesta candidata modifica il percorso visuale senza dichiararlo ancora consolidato. Verificare fisicamente:\n\n1. la sidebar mostra **Produzione AI**;\n2. non esistono più i cinque tab superiori della Round 2;\n3. i soli quattro ovali `Definizione → Prompt → Produzione AI → Revisione` sono cliccabili e consentono anche navigazione non sequenziale senza loop;\n4. in **Definizione** l'utente sceglie fra `Soggetto + ambientazione generici` e `Scene + soggetti strutturati`;\n5. `Consistent` resta combinabile con la modalità scelta;\n6. `DEVE FARE · HARD` e `NON DEVE FARE · HARD` sono gli ultimi campi della Definizione;\n7. la fase Prompt mostra il Prompt compilato dagli stessi dati senza richiedere nuovamente i due campi HARD;\n8. nei materiali, **Uso dell'AI** e **Fedeltà** mostrano etichette italiane e una breve descrizione dinamica;\n9. salvare, chiudere, riaprire e cambiare progetto non deve far ricomparire loop o fasi contaminate.\n\nSolo dopo questa prova le parti nuove possono avanzare verso **CONSOLIDATO**.\n'''
        guide_path.write_text(guide, encoding="utf-8")

print("Round 3 source patch applied successfully.")
