using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;

namespace DiezPublishingStudio;

/// <summary>
/// V2 visual AI exchange inside the single physical MainWindow.
/// Replaces the plain Prompt Pack export with one enriched by real image intake,
/// base-image metadata and every effective visual preset. Also exposes local correction
/// without reopening the legacy separate correction window.
/// </summary>
internal static class SingleWindowAiImageContextUi
{
    private const string PromptMarker = "DiezAiImageContextPromptPanel";
    private const string ReviewMarker = "DiezAiImageContextCorrectionPanel";

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
        if (!TrySession(window, out var project, out var path)) return;
        var host = SingleWindowEntryPointUi.GetHost(window);
        var pageHost = host.GetType().GetField("_pageHost", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host) as ContentControl;
        if (pageHost?.Content is not Control page) return;

        if (Descendants(page).OfType<Button>().Any(b => string.Equals(b.Content?.ToString(), "Crea Prompt Pack ZIP", StringComparison.Ordinal)))
            EnsurePromptPackPage(window, page, project, path);

        if (Descendants(page).OfType<TextBlock>().Any(t =>
                (t.Text ?? string.Empty).Contains("Seleziona un'immagine", StringComparison.OrdinalIgnoreCase)))
            EnsureReviewCorrection(window, page, project, path);
    }

    private static void EnsurePromptPackPage(MainWindow window, Control page, PreviewProject project, string path)
    {
        if (Descendants(page).Any(c => string.Equals(c.Name, PromptMarker, StringComparison.Ordinal))) return;
        var oldExport = Descendants(page).OfType<Button>().FirstOrDefault(b =>
            string.Equals(b.Content?.ToString(), "Crea Prompt Pack ZIP", StringComparison.Ordinal));
        if (oldExport?.Parent is not StackPanel actionRow) return;
        oldExport.IsVisible = false;

        var role = new ComboBox
        {
            Name = "AiImageIntakeRole",
            ItemsSource = AiExchangeImageRequestContextService.IntakeRoles,
            SelectedItem = "REFERENCE",
            Width = 260,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        var description = new TextBox
        {
            Name = "AiImageIntakeDescription",
            Height = 82,
            AcceptsReturn = true,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Watermark = "Descrivi cosa rappresenta la foto e come deve essere usata dall'AI. La descrizione accompagna sempre il file reale.",
            IsUndoEnabled = true
        };
        var intakeList = new ListBox { Name = "AiImageIntakeList", MinHeight = 92, MaxHeight = 170 };
        var addIntake = MakeButton("Aggiungi foto intake", 180);
        var removeIntake = MakeButton("Rimuovi intake", 150);
        var exportV2 = MakeButton("Crea Prompt Pack ZIP", 190);

        void RefreshIntake()
        {
            var state = AiExchangeImageRequestContextService.Load(project);
            intakeList.ItemsSource = state.Images.Select(x => new IntakeRow(x)).ToList();
        }

        addIntake.Click += async (_, _) =>
        {
            var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Aggiungi foto all'intake Diez",
                AllowMultiple = true,
                FileTypeFilter = [new FilePickerFileType("Immagini") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp", "*.gif"] }]
            });
            if (files.Count == 0) return;

            var exchange = AiExchangeStateStore.Load(project);
            var targetIds = exchange.WorkUnits
                .Where(x => string.Equals(x.ContentType, AiExchangeContentTypes.Image, StringComparison.OrdinalIgnoreCase))
                .Select(x => x.WorkUnitId)
                .ToList();
            var added = 0;
            foreach (var file in files)
            {
                var imported = await MaterialImporter.ImportAsync(file.Path.LocalPath);
                var material = project.Materials.FirstOrDefault(m =>
                    string.Equals(m.Sha256, imported.Sha256, StringComparison.OrdinalIgnoreCase));
                if (material is null)
                {
                    imported.Summary = "Foto intake AI · " + imported.Summary;
                    project.Materials.Add(imported);
                    material = imported;
                }
                AiExchangeImageRequestContextService.Add(
                    project,
                    material.MaterialId,
                    role.SelectedItem?.ToString() ?? "REFERENCE",
                    description.Text ?? string.Empty,
                    targetIds.Count == 0 ? null : targetIds);
                added++;
            }
            await ProjectFileStore.SaveAsync(path, project);
            RefreshIntake();
            SetStatus(window, $"Aggiunte {added} foto intake. Il file reale e la descrizione entreranno nel JSON operativo.");
        };

        removeIntake.Click += async (_, _) =>
        {
            if (intakeList.SelectedItem is not IntakeRow row) return;
            if (AiExchangeImageRequestContextService.Remove(project, row.Item.IntakeId))
            {
                await ProjectFileStore.SaveAsync(path, project);
                RefreshIntake();
                SetStatus(window, "Foto rimossa dall'intake AI. Il materiale del progetto non viene cancellato.");
            }
        };

        exportV2.Click += async (_, _) =>
        {
            var exchange = AiExchangeStateStore.Load(project);
            var units = exchange.WorkUnits
                .Where(x => string.Equals(x.ContentType, AiExchangeContentTypes.Image, StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.Position)
                .ThenBy(x => x.Code)
                .ToList();
            if (units.Count == 0)
            {
                SetStatus(window, "Non ci sono Work Unit immagine da esportare.");
                return;
            }

            var file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Salva Prompt Pack Diez con contesto immagini completo",
                SuggestedFileName = "diez-prompt-pack.zip",
                DefaultExtension = "zip",
                FileTypeChoices = [new FilePickerFileType("Prompt Pack Diez") { Patterns = ["*.zip"] }]
            });
            if (file is null) return;

            var targetPath = EnsureZip(file.Path.LocalPath);
            var built = await AiExchangePromptPackBuilder.BuildAsync(
                project,
                path,
                exchange,
                units.Select(x => x.WorkUnitId),
                targetPath);
            if (!built.Success)
            {
                SetStatus(window, built.Message);
                return;
            }

            var enhanced = await AiExchangeImageRequestContextService.EnhancePromptPackAsync(
                project,
                path,
                exchange,
                units.Select(x => x.WorkUnitId),
                targetPath);
            SetStatus(window, enhanced.Success
                ? built.Message + " · " + enhanced.Message
                : built.Message + " · ATTENZIONE: " + enhanced.Message);
        };

        var panel = new StackPanel
        {
            Name = PromptMarker,
            Spacing = 8,
            Children =
            {
                new Separator(),
                new TextBlock { Text = "Foto intake per l'AI", FontSize = 19 },
                new TextBlock
                {
                    Text = "Le foto intake vengono sempre esportate come file reali più record JSON. La descrizione utente spiega ruolo e contenuto, ma non sostituisce mai l'immagine.",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                },
                Labeled("Ruolo", role),
                Labeled("Descrizione della foto / istruzioni d'uso", description),
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { addIntake, removeIntake } },
                intakeList,
                new TextBlock
                {
                    Text = "L'export include inoltre immagine base e descrizione corrente, paradigmi, preserve/change/add/remove, Consistent e tutti i preset: stile/resa, colore, dettaglio, spessori, HD–8K, pixel, aspect ratio, DPI, formato, margini e bleed.",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                }
            }
        };

        var root = FindOwningStack(page, actionRow);
        if (root is not null)
        {
            var idx = root.Children.IndexOf(actionRow);
            root.Children.Insert(Math.Max(0, idx), panel);
        }
        actionRow.Children.Insert(Math.Max(0, actionRow.Children.IndexOf(oldExport)), exportV2);
        RefreshIntake();
    }

    private static void EnsureReviewCorrection(MainWindow window, Control page, PreviewProject project, string path)
    {
        if (Descendants(page).Any(c => string.Equals(c.Name, ReviewMarker, StringComparison.Ordinal))) return;
        var list = Descendants(page).OfType<ListBox>().FirstOrDefault();
        if (list is null) return;

        var instruction = new TextBox
        {
            Name = "AiImageCorrectionInstruction",
            Height = 92,
            AcceptsReturn = true,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Watermark = "Es. Sostituisci soltanto il cappello con un berretto rosso; lascia invariato tutto il resto.",
            IsUndoEnabled = true
        };
        var preserveRest = new CheckBox
        {
            Name = "AiImageCorrectionPreserveRest",
            Content = "Mantieni invariato tutto ciò che non chiedo di cambiare",
            IsChecked = true
        };
        var create = MakeButton("Crea Prompt Pack correzione", 225);
        var info = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };

        create.Click += async (_, _) =>
        {
            var unit = SelectedWorkUnit(list);
            if (unit is null)
            {
                info.Text = "Seleziona prima un'immagine da correggere.";
                return;
            }
            var change = (instruction.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(change))
            {
                info.Text = "Descrivi la correzione da applicare.";
                instruction.Focus();
                return;
            }

            var exchange = AiExchangeStateStore.Load(project);
            var liveUnit = exchange.WorkUnits.FirstOrDefault(x => x.WorkUnitId == unit.WorkUnitId);
            if (liveUnit is null)
            {
                info.Text = "Work Unit non più disponibile.";
                return;
            }
            var baseVersion = ResolveBaseVersion(exchange, liveUnit);
            if (baseVersion?.MaterialId is not Guid)
            {
                info.Text = "La correzione immagine richiede una versione base con immagine reale.";
                return;
            }

            liveUnit.Mode = AiExchangeModes.AiWithInputAsReference;
            liveUnit.Instruction = change;
            liveUnit.Change = [change];
            liveUnit.Preserve = preserveRest.IsChecked == true ? ["all unspecified elements"] : [];
            liveUnit.Add = [];
            liveUnit.Remove = [];
            AiExchangeStateStore.Save(project, exchange);
            await ProjectFileStore.SaveAsync(path, project);

            var file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Salva Prompt Pack di correzione",
                SuggestedFileName = $"diez-correzione-{SafeCode(liveUnit.Code)}.zip",
                DefaultExtension = "zip",
                FileTypeChoices = [new FilePickerFileType("Prompt Pack Diez") { Patterns = ["*.zip"] }]
            });
            if (file is null) return;

            var targetPath = EnsureZip(file.Path.LocalPath);
            var built = await AiExchangePromptPackBuilder.BuildAsync(project, path, exchange, [liveUnit.WorkUnitId], targetPath);
            if (!built.Success)
            {
                info.Text = built.Message;
                SetStatus(window, built.Message);
                return;
            }

            var enhanced = await AiExchangeImageRequestContextService.EnhancePromptPackAsync(
                project, path, exchange, [liveUnit.WorkUnitId], targetPath);
            info.Text = enhanced.Success
                ? "Correzione pronta: immagine base reale + descrizione corrente + intake e descrizioni + paradigmi + istruzione/preserve/change + preset completi."
                : "Prompt Pack creato, ma il contesto immagini V2 non è completo: " + enhanced.Message;
            SetStatus(window, info.Text);
        };

        var panel = new StackPanel
        {
            Name = ReviewMarker,
            Spacing = 7,
            Children =
            {
                new Separator(),
                new TextBlock { Text = "Correggi l'immagine selezionata con AI", FontSize = 18 },
                instruction,
                preserveRest,
                create,
                info
            }
        };

        var root = Descendants(page).OfType<Grid>().FirstOrDefault(g => g.Children.Contains(list));
        if (root is not null)
        {
            root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            Grid.SetRow(panel, Math.Max(0, root.RowDefinitions.Count - 1));
            root.Children.Add(panel);
        }
        else if (list.Parent is StackPanel stack)
        {
            var idx = stack.Children.IndexOf(list);
            stack.Children.Insert(Math.Min(stack.Children.Count, idx + 1), panel);
        }
    }

    private static AiExchangeWorkUnit? SelectedWorkUnit(ListBox list)
    {
        var selected = list.SelectedItem;
        if (selected is null) return null;
        if (selected is AiExchangeWorkUnit direct) return direct;
        return selected.GetType().GetProperty("Unit", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(selected) as AiExchangeWorkUnit;
    }

    private static AiExchangeVersion? ResolveBaseVersion(AiExchangeState state, AiExchangeWorkUnit unit)
    {
        if (unit.ApprovedVersionId is Guid approvedId)
        {
            var approved = state.Versions.FirstOrDefault(v => v.VersionId == approvedId);
            if (approved is not null) return approved;
        }
        return state.Versions
            .Where(v => v.WorkUnitId == unit.WorkUnitId &&
                        !string.Equals(v.Status, AiExchangeVersionStatuses.Rejected, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(v => v.VersionNumber)
            .FirstOrDefault();
    }

    private static StackPanel? FindOwningStack(Control page, Control child)
    {
        var current = child.Parent;
        while (current is not null && current != page)
        {
            if (current is StackPanel panel && panel.Children.Contains(child)) return panel;
            child = current as Control ?? child;
            current = current.Parent;
        }
        return Descendants(page).OfType<StackPanel>().FirstOrDefault(p => p.Children.Contains(child));
    }

    private static bool TrySession(MainWindow window, out PreviewProject project, out string path)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        project = typeof(MainWindow).GetField("_project", flags)?.GetValue(window) as PreviewProject ?? null!;
        path = typeof(MainWindow).GetField("_currentProjectPath", flags)?.GetValue(window) as string ?? string.Empty;
        return project is not null && !string.IsNullOrWhiteSpace(path);
    }

    private static void SetStatus(MainWindow window, string message)
    {
        var main = typeof(MainWindow).GetField("_status", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(window) as TextBlock;
        if (main is not null) main.Text = message;
        try
        {
            var host = SingleWindowEntryPointUi.GetHost(window);
            var status = host.GetType().GetField("_status", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host) as TextBlock;
            if (status is not null) status.Text = message;
        }
        catch { }
    }

    private static StackPanel Labeled(string label, Control control) => new()
    {
        Spacing = 3,
        Children = { new TextBlock { Text = label, FontSize = 13 }, control }
    };

    private static Button MakeButton(string text, double width) => new()
    {
        Content = text,
        Width = width,
        HorizontalContentAlignment = HorizontalAlignment.Center
    };

    private static string EnsureZip(string path) =>
        path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? path : path + ".zip";

    private static string SafeCode(string? code)
    {
        var value = string.IsNullOrWhiteSpace(code) ? "immagine" : code;
        return string.Concat(value.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch)).ToLowerInvariant();
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

    private sealed record IntakeRow(AiExchangeImageRequestContextService.IntakeImage Item)
    {
        public override string ToString()
        {
            var description = string.IsNullOrWhiteSpace(Item.Description) ? "senza descrizione" : Item.Description;
            return $"{Item.Role} · {description}";
        }
    }
}
