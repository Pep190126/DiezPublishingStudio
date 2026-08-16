using DiezPublishingStudio;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace DiezPublishingStudio.UnoSpike;

internal static class AiCenterWorkspace
{
    private sealed record DestinationChoice(Guid? ContentId, string Label)
    {
        public override string ToString() => Label;
    }

    public static UIElement Build(
        DiezProjectDocument document,
        Func<Task> save,
        Action<string> report,
        Action showAiCenter,
        Action showVision,
        Action routeCurrentBook)
    {
        var root = PageRoot(
            "Produzione con AI",
            "Prepara i Prompt, crea il Prompt Pack o usa un trasporto API quando realmente disponibile, controlla le versioni e scegli esplicitamente quando un risultato approvato deve entrare nel libro.");

        var provider = Combo(
            ["ChatGPT / OpenAI", "Gemini", "Altra / nuova AI"],
            document.GetUiString("AI.Provider", "ChatGPT / OpenAI"));
        var brief = Editor(document.GetUiString("AI.ProjectBrief"), "Regole comuni del progetto.", 140);
        var prompt = Editor(document.GetUiString("AI.HumanPrompt"), "Prompt modificabile per testo, immagini o dati.", 180);
        var outputType = Combo(["Image", "Text", "Data"], document.GetUiString("AI.OutputType", "Image"));

        var jobModels = document.AiJobs().ToList();
        var jobs = new ListView
        {
            Height = 220,
            ItemsSource = jobModels.Select(JobLabel).ToList()
        };
        var selectedJob = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var response = Editor(document.GetUiString("AI.LastResponseDraft"), "Incolla qui la risposta ricevuta dall’AI.", 180);
        var versions = new ListView { Height = 175 };
        List<DiezAiFrontendVersion> versionModels = [];

        var destinations = new List<DestinationChoice>
        {
            new(null, "Automatico — crea o riusa la destinazione Diez")
        };
        destinations.AddRange(document.EditorialDestinations()
            .Select(x => new DestinationChoice(x.ContentId, $"{x.Kind} · {x.Title}")));
        var destination = new ComboBox
        {
            ItemsSource = destinations,
            SelectedIndex = 0,
            MinWidth = 360,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        var applicationInfo = new TextBlock
        {
            Text = "Approvare non modifica il libro. “Porta nel libro” è un secondo atto esplicito e idempotente.",
            TextWrapping = TextWrapping.Wrap
        };

        void RefreshSelectedJob()
        {
            if (jobs.SelectedIndex < 0 || jobs.SelectedIndex >= jobModels.Count)
            {
                selectedJob.Text = jobModels.Count == 0 ? "Nessuna attività AI creata." : "Seleziona un’attività AI.";
                versions.ItemsSource = Array.Empty<string>();
                versionModels = [];
                response.IsEnabled = false;
                return;
            }

            var job = jobModels[jobs.SelectedIndex];
            selectedJob.Text = $"{job.Code} · {job.DisplayType} · {job.DisplayStatus}\n{job.Title}";
            var image = string.Equals(job.OutputType, "Image", StringComparison.OrdinalIgnoreCase);
            response.IsEnabled = !image;
            response.PlaceholderText = image
                ? "Per le immagini importa e approva la versione nell’area Vision."
                : "Incolla qui la risposta ricevuta dall’AI.";

            versionModels = job.WorkUnitId.HasValue
                ? document.AiVersions(job.WorkUnitId.Value).ToList()
                : [];
            versions.ItemsSource = versionModels
                .Select(v => $"v{v.VersionNumber} · {v.DisplayStatus}")
                .ToList();
            versions.SelectedIndex = versionModels.Count > 0 ? 0 : -1;
        }

        jobs.SelectionChanged += (_, _) => RefreshSelectedJob();
        jobs.SelectedIndex = jobModels.Count > 0 ? 0 : -1;
        RefreshSelectedJob();

        root.Children.Add(Card("Impostazioni AI", Vertical(
            Labeled("Provider AI", provider),
            Labeled("Tipo di risultato", outputType),
            Labeled("Regole comuni", brief),
            Labeled("Prompt", prompt))));

        if (BookTypeCatalog.IsVisual(document.BookType))
        {
            var apiInfo = new TextBlock { TextWrapping = TextWrapping.Wrap };
            var apiButton = new Button
            {
                Content = "Via API · non configurata",
                Padding = new Thickness(14, 8),
                IsEnabled = false
            };

            void RefreshApiCapability()
            {
                var capability = DiezAiTransportFrontendBridge.Provider(provider.SelectedItem?.ToString());
                apiInfo.Text = capability.SupportsDirectApi
                    ? $"{capability.DisplayName} dichiara supporto API nel catalogo Core, ma l’executor della Uno non è ancora collegato: il pulsante resta disabilitato per non simulare una generazione."
                    : $"{capability.DisplayName}: il Core attuale non dichiara ancora un trasporto API diretto. La strada resta visibile ma non finge di essere operativa.";
                apiButton.Content = capability.SupportsDirectApi
                    ? "Via API · executor da collegare"
                    : "Via API · non configurata";
                apiButton.IsEnabled = false;
            }

            provider.SelectionChanged += (_, _) => RefreshApiCapability();
            RefreshApiCapability();

            root.Children.Add(Card("Prompt Pack e modalità di generazione", Vertical(
                new TextBlock
                {
                    Text = "Per i libri con immagini ci sono due strade che devono convergere sulle stesse Work Unit: Manuale e Via API. La strada Manuale crea UN SOLO Prompt Pack ZIP da consegnare/uploadare all’AI; lo ZIP contiene PROMPT.md, manifest, istruzioni ed eventuali reference.",
                    TextWrapping = TextWrapping.Wrap
                },
                Horizontal(
                    AsyncButton("Crea Prompt Pack ZIP · Manuale", async () =>
                    {
                        document.SetUiString("AI.Provider", provider.SelectedItem?.ToString());
                        document.SetUiBool("AI.PreferAdvanced", document.GetUiBool("AI.PreferAdvanced", true));

                        var sync = document.EnsureVisualReadyJobs(
                            document.GetUiString("Prompt.MustDo"),
                            document.GetUiString("Prompt.MustNotDo"),
                            ProviderId(provider.SelectedItem?.ToString()),
                            document.GetUiBool("AI.PreferAdvanced", true));
                        if (!sync.Success)
                        {
                            report(sync.Message);
                            return;
                        }

                        var workUnitIds = sync.Jobs
                            .Where(x => x.WorkUnitId.HasValue)
                            .Select(x => x.WorkUnitId!.Value)
                            .Distinct()
                            .ToList();
                        if (workUnitIds.Count == 0)
                        {
                            report("Il piano visuale non contiene ancora Work Unit pronte per il Prompt Pack.");
                            return;
                        }

                        await save();
                        try
                        {
                            var picker = new FileSavePicker
                            {
                                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                                SuggestedFileName = SafeFileName(document.EditionTitle) + "-prompt-pack"
                            };
                            picker.FileTypeChoices.Add("Prompt Pack Diez", new List<string> { ".zip" });
                            var file = await picker.PickSaveFileAsync();
                            if (file is null) return;

                            var result = await document.CreateManualPromptPackAsync(workUnitIds, file.Path);
                            if (result.Success) await save();
                            report(result.Message);
                            if (result.Success) showAiCenter();
                        }
                        catch (Exception ex)
                        {
                            report("Creazione Prompt Pack non riuscita: " + ex.GetBaseException().Message);
                        }
                    }),
                    apiButton),
                apiInfo,
                new TextBlock
                {
                    Text = "Copia Prompt resta una utility di emergenza. Il percorso Manuale normale è il file ZIP unico, non N copie/incolla e non N chat obbligatorie.",
                    TextWrapping = TextWrapping.Wrap
                })));
        }

        root.Children.Add(Card("Attività AI", Vertical(jobs, selectedJob)));

        root.Children.Add(Card("Risposta e versioni", Vertical(
            new TextBlock
            {
                Text = "Testo e Dati possono essere importati qui come versioni candidate. Le immagini passano da Vision per l'approvazione HARD.",
                TextWrapping = TextWrapping.Wrap
            },
            Labeled("Risposta ricevuta", response),
            Labeled("Versioni dell’attività selezionata", versions),
            Horizontal(
                AsyncButton("Importa come candidato", async () =>
                {
                    if (!TrySelectedJob(jobs, jobModels, report, out var job)) return;
                    if (!job.WorkUnitId.HasValue)
                    {
                        report("Questa attività non ha ancora una Work Unit AI Exchange valida.");
                        return;
                    }
                    if (string.Equals(job.OutputType, "Image", StringComparison.OrdinalIgnoreCase))
                    {
                        report("Per un risultato Immagine usa Vision: descrizione e controlli obbligatori non possono essere saltati.");
                        return;
                    }

                    document.SetUiString("AI.LastResponseDraft", response.Text);
                    var result = await document.IngestAiTextResultAsync(job.WorkUnitId.Value, response.Text);
                    if (result.Status is "IMPORTED" or "UPDATED" or "DUPLICATE")
                        document.SetUiString("AI.LastResponseDraft", "");
                    await save();
                    showAiCenter();
                    report(result.Message);
                }),
                AsyncButton("Approva versione", async () =>
                {
                    if (!TrySelectedVersion(versions, versionModels, report, out var version)) return;
                    if (string.Equals(jobModels.ElementAtOrDefault(jobs.SelectedIndex)?.OutputType, "Image", StringComparison.OrdinalIgnoreCase))
                    {
                        report("Le immagini possono essere approvate solo dopo i controlli Vision.");
                        return;
                    }
                    var result = document.ApproveAiVersion(version.VersionId);
                    await save();
                    showAiCenter();
                    report(result.Message);
                }),
                ActionButton("Apri Vision immagini", showVision)))));

        root.Children.Add(Card("Porta il risultato nel libro", Vertical(
            applicationInfo,
            Labeled("Destinazione editoriale opzionale", destination),
            new TextBlock
            {
                Text = "Se lasci Automatico, Diez riusa la destinazione già associata alla stessa attività oppure ne crea una appropriata. Word Search e Cruciverba strutturati entrano nei loro database canonici.",
                TextWrapping = TextWrapping.Wrap
            },
            Horizontal(
                AsyncButton("Porta nel libro", async () =>
                {
                    if (!TrySelectedVersion(versions, versionModels, report, out var version)) return;
                    var target = (destination.SelectedItem as DestinationChoice)?.ContentId;
                    var result = document.PromoteAiVersion(version.VersionId, target);
                    await save();
                    showAiCenter();
                    report(result.Message);
                }),
                ActionButton("Apri il workspace del libro", routeCurrentBook)))));

        root.Children.Add(Horizontal(
            AsyncButton("Prepara attività AI", async () =>
            {
                document.SetUiString("AI.Provider", provider.SelectedItem?.ToString());
                document.SetUiString("AI.OutputType", outputType.SelectedItem?.ToString());
                document.SetUiString("AI.ProjectBrief", brief.Text);
                document.SetUiString("AI.HumanPrompt", prompt.Text);
                document.AddAiJob("Prompt", outputType.SelectedItem?.ToString() ?? "Image", prompt.Text ?? "");
                await save();
                showAiCenter();
                report("Attività AI creata e pronta da generare.");
            }),
            ActionButton("Copia Prompt · utility", () => CopyText(prompt.Text ?? string.Empty))));

        return root;
    }

    private static bool TrySelectedJob(
        ListView jobs,
        IReadOnlyList<DiezAiFrontendJob> models,
        Action<string> report,
        out DiezAiFrontendJob job)
    {
        if (jobs.SelectedIndex >= 0 && jobs.SelectedIndex < models.Count)
        {
            job = models[jobs.SelectedIndex];
            return true;
        }
        report("Seleziona prima un’attività AI.");
        job = default!;
        return false;
    }

    private static bool TrySelectedVersion(
        ListView versions,
        IReadOnlyList<DiezAiFrontendVersion> models,
        Action<string> report,
        out DiezAiFrontendVersion version)
    {
        if (versions.SelectedIndex >= 0 && versions.SelectedIndex < models.Count)
        {
            version = models[versions.SelectedIndex];
            return true;
        }
        report("Seleziona una versione.");
        version = default!;
        return false;
    }

    private static string JobLabel(DiezAiFrontendJob job) =>
        $"{job.Code} · {job.DisplayType} · {job.DisplayStatus} · {job.Title}";

    private static StackPanel PageRoot(string title, string description)
    {
        var root = new StackPanel
        {
            Spacing = 16,
            Margin = new Thickness(28),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        root.Children.Add(new TextBlock { Text = title, FontSize = 28, TextWrapping = TextWrapping.Wrap });
        root.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap });
        root.Children.Add(new Separator());
        return root;
    }

    private static Border Card(string title, UIElement content) => new()
    {
        Padding = new Thickness(16),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(6),
        HorizontalAlignment = HorizontalAlignment.Stretch,
        Child = Vertical(new TextBlock { Text = title, FontSize = 19, TextWrapping = TextWrapping.Wrap }, content)
    };

    private static StackPanel Vertical(params UIElement[] items)
    {
        var panel = new StackPanel { Spacing = 9, HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var item in items) panel.Children.Add(item);
        return panel;
    }

    private static StackPanel Horizontal(params UIElement[] items)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 9, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var item in items) panel.Children.Add(item);
        return panel;
    }

    private static StackPanel Labeled(string label, UIElement control) =>
        Vertical(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap }, control);

    private static TextBox Editor(string text, string placeholder, double minHeight) => new()
    {
        Text = text ?? string.Empty,
        PlaceholderText = placeholder,
        MinHeight = minHeight,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };

    private static ComboBox Combo(IEnumerable<string> values, string selected)
    {
        var items = values.ToList();
        var combo = new ComboBox
        {
            ItemsSource = items,
            MinWidth = 230,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        combo.SelectedItem = items.FirstOrDefault(x => string.Equals(x, selected, StringComparison.OrdinalIgnoreCase))
            ?? items.FirstOrDefault();
        return combo;
    }

    private static Button ActionButton(string text, Action action)
    {
        var button = new Button { Content = text, Padding = new Thickness(14, 8) };
        button.Click += (_, _) => action();
        return button;
    }

    private static Button AsyncButton(string text, Func<Task> action)
    {
        var button = new Button { Content = text, Padding = new Thickness(14, 8) };
        button.Click += async (_, _) => await action();
        return button;
    }

    private static string ProviderId(string? value)
    {
        if ((value ?? string.Empty).Contains("Gemini", StringComparison.OrdinalIgnoreCase)) return "gemini";
        if ((value ?? string.Empty).Contains("OpenAI", StringComparison.OrdinalIgnoreCase) ||
            (value ?? string.Empty).Contains("ChatGPT", StringComparison.OrdinalIgnoreCase)) return "openai";
        return "generic";
    }

    private static string SafeFileName(string? value)
    {
        var name = string.IsNullOrWhiteSpace(value) ? "diez" : value.Trim();
        foreach (var invalid in Path.GetInvalidFileNameChars()) name = name.Replace(invalid, '-');
        return name.Length > 80 ? name[..80] : name;
    }

    private static void CopyText(string text)
    {
        var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
        package.SetText(text);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
        Windows.ApplicationModel.DataTransfer.Clipboard.Flush();
    }
}
