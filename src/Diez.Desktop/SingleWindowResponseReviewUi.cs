using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace DiezPublishingStudio;

/// <summary>
/// Audited Response review page. Unlike the legacy page, provider-declared FAILED items are first-class
/// rows even when no Candidate/material exists, and the main review body always lives inside a ScrollViewer.
/// </summary>
internal static class SingleWindowResponseReviewUi
{
    internal static void Open(MainWindow window)
    {
        if (!TrySession(window, out var project, out var path)) return;
        var host = SingleWindowEntryPointUi.GetHost(window);
        var exchange = AiExchangeStateStore.Load(project);
        var rows = exchange.WorkUnits
            .Where(u => string.Equals(u.ContentType, AiExchangeContentTypes.Image, StringComparison.OrdinalIgnoreCase))
            .OrderBy(u => u.Position)
            .ThenBy(u => u.Code, StringComparer.OrdinalIgnoreCase)
            .Select(u => new ReviewRow(u, LatestVersion(exchange, u), AiExchangeResponseFailureStore.Latest(project, u.WorkUnitId)))
            .ToList();

        var list = new ListBox
        {
            Name = "DiezResponseReviewList",
            ItemsSource = rows,
            MinHeight = 150,
            MaxHeight = 260,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var info = new TextBlock
        {
            Name = "DiezResponseReviewInfo",
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 850
        };
        var audit = new TextBox
        {
            Name = "DiezResponseReviewAudit",
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 92,
            MaxHeight = 180,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var description = new TextBox
        {
            Name = "DiezResponseReviewDescription",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            IsUndoEnabled = true,
            MinHeight = 110,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var save = Button("Salva descrizione", 155);
        var approve = Button("Approva", 115);
        approve.Name = "DiezLegacyApproveForVisionReplacement";

        ReviewRow? Selected() => list.SelectedItem as ReviewRow;

        async Task RefreshSelectedAsync()
        {
            var row = Selected();
            if (row is null)
            {
                info.Text = "Seleziona una Work Unit.";
                audit.Text = string.Empty;
                description.Text = string.Empty;
                description.IsEnabled = false;
                save.IsEnabled = false;
                approve.IsEnabled = false;
                SetPreview(host, Placeholder("Seleziona un risultato per visualizzarne lo stato."));
                return;
            }

            // Reload live state because description/approval may change while this page remains open.
            var state = AiExchangeStateStore.Load(project);
            var version = LatestVersion(state, row.Unit);
            var failure = AiExchangeResponseFailureStore.Latest(project, row.Unit.WorkUnitId);
            if (FailureIsCurrent(version, failure))
            {
                var reason = string.IsNullOrWhiteSpace(failure!.FailureReason) ? "Motivo non specificato dal provider." : failure.FailureReason;
                info.Text = $"{row.Unit.Code} · v{failure.CandidateVersion} · FAILED · nessun asset accettato{Environment.NewLine}{reason}";
                var lines = new List<string>
                {
                    "Esito provider: FAILED",
                    "Motivo: " + reason
                };
                if (!string.IsNullOrWhiteSpace(failure.Description)) lines.Add("Descrizione provider: " + failure.Description);
                if (!string.IsNullOrWhiteSpace(failure.RenderRequestId)) lines.Add("render_request_id: " + failure.RenderRequestId);
                if (!string.IsNullOrWhiteSpace(failure.RenderPromptSha256)) lines.Add("render_prompt_sha256: " + failure.RenderPromptSha256);
                if (version is not null)
                    lines.Add($"Nota: esiste una Candidate precedente v{version.VersionNumber}, ma il tentativo corrente v{failure.CandidateVersion} è FAILED e resta lo stato mostrato.");
                audit.Text = string.Join(Environment.NewLine, lines);
                description.Text = failure.Description;
                description.IsEnabled = false;
                save.IsEnabled = false;
                approve.IsEnabled = false;
                SetPreview(host, Placeholder(
                    "FAILED — nessun asset incluso. Il renderer ha restituito un risultato non conforme e Diez lo ha correttamente scartato; non c'è un'immagine corrente da approvare o visualizzare." + Environment.NewLine + reason));
                return;
            }

            if (version is null)
            {
                info.Text = $"{row.Unit.Code} · nessun Response ancora registrato";
                audit.Text = "Non risultano né una Candidate né un FAILED provider auditato per questa Work Unit.";
                description.Text = string.Empty;
                description.IsEnabled = false;
                save.IsEnabled = false;
                approve.IsEnabled = false;
                SetPreview(host, Placeholder("Nessun risultato ancora registrato per questa Work Unit."));
                return;
            }

            info.Text = $"{row.Unit.Code} · v{version.VersionNumber} · {version.Status} · descrizione {version.DescriptionStatus}";
            description.Text = version.Description;
            description.IsEnabled = true;
            save.IsEnabled = true;
            approve.IsEnabled = version.MaterialId.HasValue && version.Status != AiExchangeVersionStatuses.Approved;
            var technical = VisualAssetValidationStore.Get(project, version.VersionId);
            var vision = VisionValidationStore.Get(project, version.VersionId);
            audit.Text = string.Join(Environment.NewLine, new[]
            {
                technical is null ? "Controllo tecnico: non registrato" : $"Controllo tecnico: {technical.Status} · {technical.Message}",
                vision is null ? "Vision: non ancora eseguita" : VisionValidationStore.UserStatus(project, version.VersionId)
            });

            if (version.MaterialId is Guid materialId)
                await ShowMaterialAsync(project, path, host, materialId, $"{row.Unit.Code} · v{version.VersionNumber}");
            else
                SetPreview(host, Placeholder("Candidate registrata senza file immagine disponibile."));
        }

        list.SelectionChanged += async (_, _) => await RefreshSelectedAsync();
        save.Click += async (_, _) =>
        {
            var row = Selected();
            if (row is null) return;
            var state = AiExchangeStateStore.Load(project);
            var version = LatestVersion(state, row.Unit);
            var failure = AiExchangeResponseFailureStore.Latest(project, row.Unit.WorkUnitId);
            if (version is null || FailureIsCurrent(version, failure)) return;
            version.Description = (description.Text ?? string.Empty).Trim();
            version.DescriptionStatus = string.IsNullOrWhiteSpace(version.Description)
                ? AiExchangeDescriptionStatuses.Missing
                : AiExchangeDescriptionStatuses.Valid;
            if (version.MaterialId.HasValue &&
                version.DescriptionStatus == AiExchangeDescriptionStatuses.Valid &&
                version.Status == AiExchangeVersionStatuses.Incomplete &&
                VisualAssetValidationStore.Get(project, version.VersionId)?.BlocksApproval != true &&
                VisionValidationStore.Get(project, version.VersionId)?.BlocksApproval != true)
                version.Status = AiExchangeVersionStatuses.Candidate;
            AiExchangeStateStore.Save(project, state);
            await ProjectFileStore.SaveAsync(path, project);
            await RefreshSelectedAsync();
        };
        approve.Click += async (_, _) =>
        {
            var row = Selected();
            if (row is null) return;
            var state = AiExchangeStateStore.Load(project);
            var version = LatestVersion(state, row.Unit);
            var failure = AiExchangeResponseFailureStore.Latest(project, row.Unit.WorkUnitId);
            if (version is null || FailureIsCurrent(version, failure)) return;
            if (!AiExchangeApprovalService.Approve(project, state, version.VersionId, out var message))
            {
                info.Text = message;
                return;
            }
            AiExchangeStateStore.Save(project, state);
            await ProjectFileStore.SaveAsync(path, project);
            info.Text = message;
            await RefreshSelectedAsync();
        };

        var body = new StackPanel
        {
            Name = "DiezResponseReviewBody",
            Spacing = 9,
            Margin = new Thickness(0, 0, 8, 8),
            Children =
            {
                new TextBlock
                {
                    Text = "Seleziona un'immagine / Work Unit: Candidate e FAILED vengono mostrati come esiti distinti.",
                    FontSize = 20,
                    TextWrapping = TextWrapping.Wrap
                },
                list,
                info,
                new TextBlock { Text = "Audit Response / controlli", FontSize = 15 },
                audit,
                new TextBlock { Text = "Descrizione associata", FontSize = 15 },
                description,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { save, approve } }
            }
        };

        var scroll = new ScrollViewer
        {
            Name = "DiezResponseReviewScroll",
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = body
        };
        var root = new Grid
        {
            Name = "DiezResponseReviewPage",
            RowDefinitions = new RowDefinitions("*"),
            Children = { scroll }
        };

        SingleWindowEntryPointUi.Invoke(
            host,
            "Push",
            "Coloring Book · 4/4 Revisione Response",
            root,
            Placeholder("Seleziona una Work Unit: una Candidate mostra l'immagine; un FAILED mostra perché nessun asset è stato accettato."),
            rows.Count == 0 ? "Nessuna Work Unit immagine disponibile." : "Response pronto per la revisione; anche i FAILED restano auditabili.");

        // Attach Vision after the new page is physically active. Its panel gets a separate Auto row while
        // the review body remains in the star-sized ScrollViewer row.
        SingleWindowVisionValidationUi.EnsureCurrentPage(window);
        if (rows.Count > 0) list.SelectedIndex = 0;
    }

    internal static bool FailureIsCurrent(AiExchangeVersion? version, AiExchangeResponseFailureStore.Record? failure) =>
        failure is not null && (version is null || failure.CandidateVersion >= version.VersionNumber);

    private static AiExchangeVersion? LatestVersion(AiExchangeState state, AiExchangeWorkUnit unit) =>
        state.Versions
            .Where(v => v.WorkUnitId == unit.WorkUnitId && v.Status != AiExchangeVersionStatuses.Rejected)
            .OrderByDescending(v => v.VersionNumber)
            .FirstOrDefault();

    private static async Task ShowMaterialAsync(
        PreviewProject project,
        string projectPath,
        object host,
        Guid materialId,
        string caption)
    {
        var material = project.Materials.FirstOrDefault(m => m.MaterialId == materialId);
        if (material is null)
        {
            SetPreview(host, Placeholder("Materiale immagine non trovato."));
            return;
        }
        var bytes = await ProjectFileStore.ReadEmbeddedMaterialAsync(projectPath, material);
        if (bytes is null || bytes.Length == 0)
        {
            SetPreview(host, Placeholder("File immagine non leggibile dal progetto."));
            return;
        }
        try
        {
            using var memory = new MemoryStream(bytes);
            var bitmap = new Bitmap(memory);
            var grid = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), RowSpacing = 6 };
            var image = new Image { Source = bitmap, Stretch = Stretch.Uniform };
            var label = new TextBlock { Text = caption, TextWrapping = TextWrapping.Wrap };
            Grid.SetRow(label, 1);
            grid.Children.Add(image);
            grid.Children.Add(label);
            SetPreview(host, grid);
        }
        catch
        {
            SetPreview(host, Placeholder("Il materiale esiste ma non può essere visualizzato come immagine."));
        }
    }

    private static void SetPreview(object host, Control control)
    {
        var previewHost = host.GetType().GetField("_previewHost", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host) as ContentControl;
        if (previewHost is not null) previewHost.Content = control;
    }

    private static Control Placeholder(string text) => new Border
    {
        Padding = new Thickness(18),
        Child = new TextBlock { Text = text, FontSize = 16, TextWrapping = TextWrapping.Wrap }
    };

    private static Button Button(string text, double width) => new()
    {
        Content = text,
        Width = width,
        HorizontalContentAlignment = HorizontalAlignment.Center
    };

    private static bool TrySession(MainWindow window, out PreviewProject project, out string path)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        project = typeof(MainWindow).GetField("_project", flags)?.GetValue(window) as PreviewProject ?? null!;
        path = typeof(MainWindow).GetField("_currentProjectPath", flags)?.GetValue(window) as string ?? string.Empty;
        return project is not null && !string.IsNullOrWhiteSpace(path);
    }

    internal sealed class ReviewRow
    {
        public ReviewRow(AiExchangeWorkUnit unit, AiExchangeVersion? version, AiExchangeResponseFailureStore.Record? failure)
        {
            Unit = unit;
            Version = version;
            Failure = failure;
        }

        public AiExchangeWorkUnit Unit { get; }
        public AiExchangeVersion? Version { get; }
        public AiExchangeResponseFailureStore.Record? Failure { get; }

        public override string ToString()
        {
            if (FailureIsCurrent(Version, Failure)) return $"{Unit.Code} · v{Failure!.CandidateVersion} · FAILED";
            if (Version is not null) return $"{Unit.Code} · v{Version.VersionNumber} · {Version.Status}";
            return $"{Unit.Code} · nessun Response";
        }
    }
}
