using DiezPublishingStudio;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace DiezPublishingStudio.UnoSpike;

internal static class VisionWorkspace
{
    public static UIElement Build(
        DiezProjectDocument document,
        Func<Task> save,
        Action<string> report,
        Action showVision,
        Action showAiCenter)
    {
        var root = new StackPanel
        {
            Spacing = 16,
            Margin = new Thickness(28),
            MaxWidth = 1050,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        root.Children.Add(new TextBlock
        {
            Text = "Immagini 4/4 · Vision",
            FontSize = 28,
            TextWrapping = TextWrapping.Wrap
        });
        root.Children.Add(new TextBlock
        {
            Text = "Importa l'immagine candidata con la sua descrizione, poi verifica tutti i controlli richiesti da Diez. Vision approva la versione; l'applicazione al libro resta una seconda azione esplicita.",
            TextWrapping = TextWrapping.Wrap
        });
        root.Children.Add(new Separator());

        var imageJobs = document.AiJobs()
            .Where(x => string.Equals(x.OutputType, "Image", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var jobs = new ListView
        {
            Height = 190,
            ItemsSource = imageJobs
                .Select(x => $"{x.Code} · {x.DisplayStatus} · {x.Title}")
                .ToList()
        };
        var selectedJob = new TextBlock { TextWrapping = TextWrapping.Wrap };

        string selectedImagePath = string.Empty;
        var selectedImage = new TextBlock
        {
            Text = "Nessuna immagine scelta.",
            TextWrapping = TextWrapping.Wrap
        };
        var description = Editor(
            document.GetUiString("Vision.ImageDescriptionDraft"),
            "Descrivi ciò che è realmente visibile nell'immagine: soggetto, scena, composizione e dettagli utili al controllo.",
            135);
        var versions = new ListView { Height = 170 };
        List<DiezAiFrontendVersion> versionModels = [];

        var requirementsHost = new StackPanel { Spacing = 10 };
        var checkByKey = new Dictionary<string, CheckBox>(StringComparer.OrdinalIgnoreCase);
        var reviewNotes = Editor(
            document.GetUiString("Vision.ReviewNotes"),
            "Note sul controllo Vision, correzioni richieste o motivo del rifiuto.",
            115);

        void RefreshRequirements(DiezAiFrontendJob? job)
        {
            requirementsHost.Children.Clear();
            checkByKey.Clear();
            if (job?.WorkUnitId is not Guid workUnitId)
            {
                requirementsHost.Children.Add(new TextBlock
                {
                    Text = "Seleziona un'attività Immagine per vedere i controlli richiesti.",
                    TextWrapping = TextWrapping.Wrap
                });
                return;
            }

            var requirements = document.VisionRequirements(workUnitId);
            if (requirements.Count == 0)
            {
                requirementsHost.Children.Add(new TextBlock
                {
                    Text = "Non risultano controlli Vision disponibili per questa attività.",
                    TextWrapping = TextWrapping.Wrap
                });
                return;
            }

            foreach (var requirement in requirements)
            {
                var check = new CheckBox
                {
                    Content = string.IsNullOrWhiteSpace(requirement.Expected)
                        ? requirement.Label
                        : $"{requirement.Label} — atteso: {requirement.Expected}",
                    IsChecked = false,
                    Tag = requirement.Key
                };
                checkByKey[requirement.Key] = check;
                requirementsHost.Children.Add(check);
            }

            requirementsHost.Children.Add(new TextBlock
            {
                Text = "Spunta un controllo solo dopo averlo verificato. Un controllo obbligatorio non spuntato viene trattato come FAIL e blocca l'approvazione.",
                TextWrapping = TextWrapping.Wrap
            });
        }

        void RefreshSelection()
        {
            selectedImagePath = string.Empty;
            selectedImage.Text = "Nessuna immagine scelta.";
            versions.SelectedIndex = -1;

            if (jobs.SelectedIndex < 0 || jobs.SelectedIndex >= imageJobs.Count)
            {
                selectedJob.Text = imageJobs.Count == 0
                    ? "Non ci sono ancora attività Immagine. Creane una dal Prompt Pack o dalla Produzione con AI."
                    : "Seleziona un'attività Immagine.";
                versionModels = [];
                versions.ItemsSource = Array.Empty<string>();
                RefreshRequirements(null);
                return;
            }

            var job = imageJobs[jobs.SelectedIndex];
            selectedJob.Text = $"{job.Code} · {job.DisplayStatus}\n{job.Title}";
            versionModels = job.WorkUnitId.HasValue
                ? document.AiVersions(job.WorkUnitId.Value).ToList()
                : [];
            versions.ItemsSource = versionModels.Select(VersionLabel).ToList();
            versions.SelectedIndex = versionModels.Count > 0 ? 0 : -1;
            RefreshRequirements(job);
        }

        jobs.SelectionChanged += (_, _) => RefreshSelection();
        versions.SelectionChanged += (_, _) =>
        {
            if (versions.SelectedIndex < 0 || versions.SelectedIndex >= versionModels.Count) return;
            var version = versionModels[versions.SelectedIndex];
            if (!string.IsNullOrWhiteSpace(version.Description))
                description.Text = version.Description;
        };
        jobs.SelectedIndex = imageJobs.Count > 0 ? 0 : -1;
        RefreshSelection();

        root.Children.Add(Card("Attività Immagine", Vertical(jobs, selectedJob)));
        root.Children.Add(Card("Immagine candidata", Vertical(
            Horizontal(
                AsyncButton("Scegli immagine…", async () =>
                {
                    try
                    {
                        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
                        foreach (var extension in new[] { ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif" })
                            picker.FileTypeFilter.Add(extension);
                        var file = await picker.PickSingleFileAsync();
                        if (file is null) return;
                        selectedImagePath = file.Path;
                        selectedImage.Text = $"Immagine scelta: {file.Name}";
                    }
                    catch (Exception ex)
                    {
                        report("Errore scelta immagine: " + ex.GetBaseException().Message);
                    }
                }),
                selectedImage),
            Labeled("Descrizione dell'immagine candidata", description),
            AsyncButton("Importa immagine candidata", async () =>
            {
                if (jobs.SelectedIndex < 0 || jobs.SelectedIndex >= imageJobs.Count)
                {
                    report("Seleziona prima un'attività Immagine.");
                    return;
                }
                var job = imageJobs[jobs.SelectedIndex];
                if (!job.WorkUnitId.HasValue)
                {
                    report("Questa attività non ha ancora una Work Unit AI Exchange valida.");
                    return;
                }
                if (string.IsNullOrWhiteSpace(selectedImagePath))
                {
                    report("Scegli prima il file immagine da importare.");
                    return;
                }

                document.SetUiString("Vision.ImageDescriptionDraft", description.Text);
                var result = await document.IngestAiImageResultAsync(
                    job.WorkUnitId.Value,
                    selectedImagePath,
                    description.Text);
                if (result.Status is "IMPORTED" or "UPDATED" or "DUPLICATE")
                    document.SetUiString("Vision.ImageDescriptionDraft", "");
                await save();
                showVision();
                report(result.Message);
            }))));

        root.Children.Add(Card("Versioni", Vertical(
            versions,
            new TextBlock
            {
                Text = "Ogni nuova versione candidata resta separata dalle precedenti. Un file diverso non sovrascrive una versione già esistente.",
                TextWrapping = TextWrapping.Wrap
            })));

        root.Children.Add(Card("Controlli Vision richiesti", requirementsHost));
        root.Children.Add(Card("Esito Vision", Vertical(
            Labeled("Note", reviewNotes),
            new TextBlock
            {
                Text = "Verifica e approva non inserisce automaticamente l'immagine nel libro. Dopo il PASS usa “Porta nel libro”.",
                TextWrapping = TextWrapping.Wrap
            },
            Horizontal(
                AsyncButton("Verifica e approva", async () =>
                {
                    if (versions.SelectedIndex < 0 || versions.SelectedIndex >= versionModels.Count)
                    {
                        report("Seleziona una versione immagine da verificare.");
                        return;
                    }
                    if (checkByKey.Count == 0)
                    {
                        report("Non ci sono controlli Vision disponibili per la versione selezionata.");
                        return;
                    }

                    var checks = checkByKey.Select(pair => new DiezVisionCheckInput(
                        pair.Key,
                        pair.Value.IsChecked == true ? "PASS" : "FAIL",
                        pair.Value.IsChecked == true
                            ? "Verificato manualmente in Diez Uno."
                            : "Controllo non confermato dall'utente."))
                        .ToList();
                    document.SetUiString("Vision.ReviewNotes", reviewNotes.Text);
                    var result = document.ApproveAiImageVersionWithVision(
                        versionModels[versions.SelectedIndex].VersionId,
                        checks,
                        reviewNotes.Text);
                    await save();
                    showVision();
                    report(result.Message);
                }),
                AsyncButton("Porta nel libro", async () =>
                {
                    if (versions.SelectedIndex < 0 || versions.SelectedIndex >= versionModels.Count)
                    {
                        report("Seleziona una versione immagine da portare nel libro.");
                        return;
                    }
                    var result = document.PromoteAiVersion(versionModels[versions.SelectedIndex].VersionId);
                    await save();
                    showVision();
                    report(result.Message);
                }),
                ActionButton("Torna alla Produzione con AI", showAiCenter)))));

        return root;
    }

    private static string VersionLabel(DiezAiFrontendVersion version)
    {
        var asset = version.MaterialId.HasValue ? "immagine presente" : "immagine mancante";
        var description = version.DescriptionStatus switch
        {
            "VALID" => "descrizione valida",
            "MISSING" => "descrizione mancante",
            "NEEDS_VERIFICATION" => "descrizione da verificare",
            _ => string.IsNullOrWhiteSpace(version.Description) ? "descrizione mancante" : "descrizione presente"
        };
        return $"v{version.VersionNumber} · {version.DisplayStatus} · {asset} · {description}";
    }

    private static Border Card(string title, UIElement content) => new()
    {
        Padding = new Thickness(16),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(6),
        Child = Vertical(
            new TextBlock { Text = title, FontSize = 19, TextWrapping = TextWrapping.Wrap },
            content)
    };

    private static StackPanel Vertical(params UIElement[] items)
    {
        var panel = new StackPanel { Spacing = 9 };
        foreach (var item in items) panel.Children.Add(item);
        return panel;
    }

    private static StackPanel Horizontal(params UIElement[] items)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 9 };
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
}
