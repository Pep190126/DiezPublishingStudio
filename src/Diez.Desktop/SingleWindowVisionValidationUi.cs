using System.Reflection;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;

namespace DiezPublishingStudio;

/// <summary>
/// First-class semantic QA controls for the active single-window Review page.
/// The old direct approval button is hidden so every visible approval passes through
/// deterministic raster gates plus any imported or direct Vision hard-fail result.
/// </summary>
internal static class SingleWindowVisionValidationUi
{
    private const string PanelName = "DiezVisionValidationPanel";
    private const string SafeApproveName = "DiezValidatedApprove";

    public static void Attach(MainWindow window)
    {
        var host = SingleWindowEntryPointUi.GetHost(window);
        var pageHost = Field<ContentControl>(host, "_pageHost");
        if (pageHost is null) return;
        pageHost.PropertyChanged += (_, e) =>
        {
            if (e.Property == ContentControl.ContentProperty) EnsureCurrentPage(window);
        };
        EnsureCurrentPage(window);
    }

    internal static void EnsureCurrentPage(MainWindow window)
    {
        if (!TrySession(window, out var project, out var path)) return;
        var host = SingleWindowEntryPointUi.GetHost(window);
        var pageHost = Field<ContentControl>(host, "_pageHost");
        if (pageHost?.Content is not Control page) return;
        if (!Descendants(page).OfType<TextBlock>().Any(t =>
                (t.Text ?? string.Empty).Contains("Seleziona un'immagine", StringComparison.OrdinalIgnoreCase))) return;
        if (Descendants(page).Any(c => string.Equals(c.Name, PanelName, StringComparison.Ordinal))) return;

        var list = Descendants(page).OfType<ListBox>().FirstOrDefault();
        if (list is null) return;
        var oldApprove = Descendants(page).OfType<Button>().FirstOrDefault(b =>
            b.IsVisible && string.Equals(b.Content?.ToString(), "Approva", StringComparison.OrdinalIgnoreCase));

        var provider = PromptPreparationSettingsStore.Load(project).ProviderId;
        var status = new TextBlock
        {
            Name = "VisionValidationStatus",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            MaxWidth = 820
        };
        var details = new TextBox
        {
            Name = "VisionValidationDetails",
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            MinHeight = 92,
            MaxHeight = 170
        };
        var direct = Button("Esegui Vision via API", 205);
        direct.Name = "DiezDirectVisionValidation";
        var export = Button("Crea controllo Vision ZIP", 210);
        var import = Button("Importa esito Vision", 185);
        var safeApprove = Button("Approva", 115);
        safeApprove.Name = SafeApproveName;

        AiExchangeWorkUnit? SelectedUnit()
        {
            var selected = list.SelectedItem;
            if (selected is null) return null;
            if (selected is AiExchangeWorkUnit directUnit) return directUnit;
            return selected.GetType().GetProperty("Unit", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(selected) as AiExchangeWorkUnit;
        }

        AiExchangeVersion? LatestUsableVersion(AiExchangeState state, AiExchangeWorkUnit unit)
        {
            var version = state.Versions
                .Where(v => v.WorkUnitId == unit.WorkUnitId && v.Status != AiExchangeVersionStatuses.Rejected)
                .OrderByDescending(v => v.VersionNumber)
                .FirstOrDefault();
            var failure = AiExchangeResponseFailureStore.Latest(project, unit.WorkUnitId);
            return SingleWindowResponseReviewUi.FailureIsCurrent(version, failure) ? null : version;
        }

        AiExchangeVersion? SelectedVersion(AiExchangeState state)
        {
            var unit = SelectedUnit();
            return unit is null ? null : LatestUsableVersion(state, unit);
        }

        void Refresh()
        {
            var state = AiExchangeStateStore.Load(project);
            var unit = SelectedUnit();
            var version = SelectedVersion(state);
            if (unit is null)
            {
                status.Text = "Vision: seleziona una Candidate con immagine.";
                details.Text = string.Empty;
                direct.IsEnabled = false;
                safeApprove.IsEnabled = false;
                return;
            }
            if (version is null)
            {
                var failure = AiExchangeResponseFailureStore.Latest(project, unit.WorkUnitId);
                if (failure is not null)
                {
                    status.Text = $"Vision non eseguibile: {unit.Code} v{failure.CandidateVersion} è FAILED e non contiene un asset corrente.";
                    details.Text = string.IsNullOrWhiteSpace(failure.FailureReason)
                        ? "Il Response ha registrato FAILED senza asset da analizzare."
                        : "FAILED provider: " + failure.FailureReason;
                }
                else
                {
                    status.Text = "Vision: seleziona una Candidate con immagine.";
                    details.Text = string.Empty;
                }
                direct.IsEnabled = false;
                safeApprove.IsEnabled = false;
                return;
            }

            direct.IsEnabled = version.MaterialId.HasValue &&
                (string.Equals(provider, PromptEngineeringProviderIds.OpenAi, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(provider, PromptEngineeringProviderIds.Gemini, StringComparison.OrdinalIgnoreCase));

            var deterministic = VisualAssetValidationStore.Get(project, version.VersionId);
            var vision = VisionValidationStore.Get(project, version.VersionId);
            var lines = new List<string>
            {
                $"{unit.Code} · Candidate v{version.VersionNumber}",
                deterministic is null
                    ? "Controllo tecnico: non registrato"
                    : $"Controllo tecnico: {deterministic.Status} · {deterministic.Message}",
                VisionValidationStore.UserStatus(project, version.VersionId)
            };
            if (vision is not null)
            {
                if (!string.IsNullOrWhiteSpace(vision.ObservedDescription))
                    lines.Add("Vision vede: " + vision.ObservedDescription);
                if (!string.IsNullOrWhiteSpace(vision.Summary))
                    lines.Add("Sintesi: " + vision.Summary);
                foreach (var check in vision.Checks.Where(c => c.Status is VisionCheckStatuses.Fail or VisionCheckStatuses.Warn))
                    lines.Add($"{check.Key}: {check.Status}/{check.Severity} · {check.Evidence}");
            }
            details.Text = string.Join(Environment.NewLine, lines);

            var can = AiExchangeApprovalService.CanApprove(project, state, version.VersionId, out var gate);
            safeApprove.IsEnabled = can && version.Status != AiExchangeVersionStatuses.Approved;
            status.Text = gate;
        }

        direct.Click += async (_, _) =>
        {
            var state = AiExchangeStateStore.Load(project);
            var version = SelectedVersion(state);
            if (version is null) return;
            if (!VisionProviderAdapterFactory.TryCreate(provider, out var adapter, out var setupMessage) || adapter is null)
            {
                status.Text = setupMessage;
                SetMainStatus(window, setupMessage);
                return;
            }

            direct.IsEnabled = false;
            safeApprove.IsEnabled = false;
            status.Text = $"Controllo Vision via {ProviderLabel(provider)} in corso sulla Candidate reale…";
            SetMainStatus(window, status.Text);
            try
            {
                var record = await VisionValidationDirectService.ValidateAsync(
                    project, path, state, version.VersionId, adapter);
                var resultMessage = record.BlocksApproval
                    ? $"Vision API: FAIL · Candidate bloccata. {record.Summary}"
                    : record.OverallStatus == VisionValidationStatuses.Pass
                        ? $"Vision API: PASS · {record.Summary}"
                        : $"Vision API: REVIEW · decisione umana richiesta. {record.Summary}";
                status.Text = resultMessage;
                SetMainStatus(window, resultMessage);
            }
            catch (Exception ex)
            {
                var error = "Vision API non completata: " + ex.GetBaseException().Message + " Nessun nuovo PASS/FAIL è stato registrato.";
                status.Text = error;
                SetMainStatus(window, error);
            }
            finally
            {
                Refresh();
            }
        };

        export.Click += async (_, _) =>
        {
            var state = AiExchangeStateStore.Load(project);
            var activeLegacy = VisualPromptSessionService.ActiveLegacyJobIds(project);
            var activeUnits = state.WorkUnits
                .Where(u => string.Equals(u.ContentType, AiExchangeContentTypes.Image, StringComparison.OrdinalIgnoreCase) &&
                            (!u.LegacyAiJobId.HasValue || activeLegacy.Contains(u.LegacyAiJobId.Value)))
                .OrderBy(u => u.Position)
                .ToList();
            var latestVersions = activeUnits
                .Select(u => LatestUsableVersion(state, u))
                .Where(v => v?.MaterialId.HasValue == true)
                .Select(v => v!.VersionId)
                .ToList();
            if (latestVersions.Count == 0)
            {
                status.Text = "Non ci sono Candidate correnti con file reale da inviare al controllo Vision. I FAILED senza asset restano auditabili ma non sono immagini da analizzare.";
                return;
            }

            var file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Salva controllo Vision Diez",
                SuggestedFileName = "diez-vision-validation.zip",
                DefaultExtension = "zip",
                FileTypeChoices = [new FilePickerFileType("Controllo Vision Diez") { Patterns = ["*.zip"] }]
            });
            if (file is null) return;

            var result = await VisionValidationPromptPackHardStyleService.BuildAsync(
                project, path, state, latestVersions, EnsureZip(file.Path.LocalPath));
            status.Text = result.Message;
            SetMainStatus(window, result.Message);
        };

        import.Click += async (_, _) =>
        {
            var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Importa esito del controllo Vision",
                AllowMultiple = true,
                FileTypeFilter = [new FilePickerFileType("Esito Vision Diez") { Patterns = ["*.zip"] }]
            });
            if (files.Count == 0) return;

            var state = AiExchangeStateStore.Load(project);
            var report = await VisionValidationPromptPackService.ImportAsync(
                project, path, state, files.Select(f => f.Path.LocalPath));
            status.Text = report.Message;
            SetMainStatus(window, report.Message);
            Refresh();
        };

        safeApprove.Click += async (_, _) =>
        {
            var state = AiExchangeStateStore.Load(project);
            var version = SelectedVersion(state);
            if (version is null) return;
            if (!AiExchangeApprovalService.Approve(project, state, version.VersionId, out var message))
            {
                status.Text = message;
                SetMainStatus(window, message);
                Refresh();
                return;
            }
            AiExchangeStateStore.Save(project, state);
            await ProjectFileStore.SaveAsync(path, project);
            status.Text = message;
            SetMainStatus(window, message);
            Refresh();
        };

        list.SelectionChanged += (_, _) => Refresh();

        var panel = new StackPanel
        {
            Name = PanelName,
            Spacing = 7,
            Children =
            {
                new Separator(),
                new TextBlock { Text = "Controllo qualità Vision", FontSize = 18 },
                new TextBlock
                {
                    Text = $"Secondo passaggio semantico indipendente · destinatario: {ProviderLabel(provider)}. Il modello deve guardare i file reali e confrontarli con la stessa specifica canonica usata per generarli. Soggetto atomico, composizione singola e stile esplicitamente selezionato sono vincoli HARD; un FAIL HARD blocca l'approvazione. REVIEW resta alla decisione umana per dubbi o qualità dentro lo stile corretto. OpenAI/Gemini possono essere eseguiti direttamente con chiavi lette dall'ambiente; nessuna chiave viene salvata nel progetto. Il flusso ZIP resta sempre disponibile.",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    MaxWidth = 820
                },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { direct, export, import } },
                status,
                details
            }
        };

        if (page is Grid grid)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            Grid.SetRow(panel, grid.RowDefinitions.Count - 1);
            Grid.SetColumnSpan(panel, Math.Max(1, grid.ColumnDefinitions.Count));
            grid.Children.Add(panel);
        }
        else if (page is StackPanel stack)
        {
            stack.Children.Add(panel);
        }
        else return;

        if (oldApprove?.Parent is StackPanel approvalRow)
        {
            oldApprove.IsVisible = false;
            var index = approvalRow.Children.IndexOf(oldApprove);
            approvalRow.Children.Insert(index < 0 ? approvalRow.Children.Count : index + 1, safeApprove);
        }
        else
        {
            panel.Children.Add(safeApprove);
        }

        Refresh();
    }

    private static string ProviderLabel(string provider) => provider switch
    {
        PromptEngineeringProviderIds.OpenAi => "ChatGPT / OpenAI con visione",
        PromptEngineeringProviderIds.Gemini => "Gemini multimodale",
        PromptEngineeringProviderIds.Other => "AI multimodale scelta dall'utente",
        _ => "AI multimodale generica"
    };

    private static string EnsureZip(string path) =>
        path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? path : path + ".zip";

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

    private static T? Field<T>(object owner, string name) where T : class =>
        owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(owner) as T;

    private static void SetMainStatus(MainWindow window, string message)
    {
        var status = typeof(MainWindow).GetField("_status", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(window) as TextBlock;
        if (status is not null) status.Text = message;
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
}
