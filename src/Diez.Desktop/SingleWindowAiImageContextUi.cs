using System.Reflection;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;

namespace DiezPublishingStudio;

/// <summary>
/// Real-image intake and local correction UI for the single-window visual workflow.
/// ZIP construction is deliberately NOT implemented here: generation and correction both delegate
/// to AiVisualPromptPackService so there is one source-level prompt/asset pipeline.
/// </summary>
internal static class SingleWindowAiImageContextUi
{
    private const string PromptMarker = "DiezAiImageContextPromptPanel";
    private const string ReviewMarker = "DiezAiImageContextCorrectionPanel";

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

        if (Descendants(page).OfType<Button>().Any(b =>
                string.Equals(b.Content?.ToString(), "Crea Prompt Pack ZIP", StringComparison.Ordinal)))
            EnsureIntakePanel(window, page, project, path);

        if (Descendants(page).OfType<TextBlock>().Any(t =>
                (t.Text ?? string.Empty).Contains("Seleziona un'immagine", StringComparison.OrdinalIgnoreCase)))
            EnsureCorrectionPanel(window, page, project, path);
    }

    private static void EnsureIntakePanel(MainWindow window, Control page, PreviewProject project, string path)
    {
        if (Descendants(page).Any(c => string.Equals(c.Name, PromptMarker, StringComparison.Ordinal))) return;
        var export = Descendants(page).OfType<Button>().FirstOrDefault(b =>
            string.Equals(b.Content?.ToString(), "Crea Prompt Pack ZIP", StringComparison.Ordinal));
        if (export?.Parent is not StackPanel actionRow) return;

        var role = new ComboBox
        {
            Name = "AiImageIntakeRole",
            ItemsSource = AiExchangeImageRequestContextService.IntakeRoles,
            SelectedItem = "REFERENCE",
            Width = 280,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        var description = new TextBox
        {
            Name = "AiImageIntakeDescription",
            MinHeight = 86,
            AcceptsReturn = true,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Watermark = "Descrivi cosa rappresenta la foto e come deve essere usata. La descrizione accompagna il file reale e non lo sostituisce.",
            IsUndoEnabled = true
        };
        var intakeList = new ListBox
        {
            Name = "AiImageIntakeList",
            MinHeight = 92,
            MaxHeight = 180
        };
        var add = Button("Aggiungi foto intake", 180);
        var remove = Button("Rimuovi intake", 150);

        void Refresh()
        {
            var state = AiExchangeImageRequestContextService.Load(project);
            intakeList.ItemsSource = state.Images.Select(x => new IntakeRow(x)).ToList();
        }

        add.Click += async (_, _) =>
        {
            var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Aggiungi foto all'intake Diez",
                AllowMultiple = true,
                FileTypeFilter =
                [
                    new FilePickerFileType("Immagini")
                    {
                        Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp", "*.gif"]
                    }
                ]
            });
            if (files.Count == 0) return;

            var exchange = AiExchangeStateStore.Load(project);
            var activeLegacy = VisualPromptSessionService.ActiveLegacyJobIds(project);
            var targetIds = exchange.WorkUnits
                .Where(x => string.Equals(x.ContentType, AiExchangeContentTypes.Image, StringComparison.OrdinalIgnoreCase) &&
                            x.LegacyAiJobId.HasValue && activeLegacy.Contains(x.LegacyAiJobId.Value))
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
            Refresh();
            SetStatus(window, $"Aggiunte {added} foto intake alla sessione visuale attiva. File reali + descrizioni entreranno nel Prompt Pack.");
        };

        remove.Click += async (_, _) =>
        {
            if (intakeList.SelectedItem is not IntakeRow row) return;
            if (!AiExchangeImageRequestContextService.Remove(project, row.Item.IntakeId)) return;
            await ProjectFileStore.SaveAsync(path, project);
            Refresh();
            SetStatus(window, "Foto rimossa dall'intake AI. Il materiale originale del progetto non viene cancellato.");
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
                    Text = "Le immagini intake appartengono alla sessione visuale attiva. Diez esporta sempre il file reale insieme a ruolo e descrizione; una descrizione non può sostituire l'immagine.",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                },
                Labeled("Ruolo", role),
                Labeled("Descrizione / istruzioni d'uso", description),
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { add, remove } },
                intakeList,
                new TextBlock
                {
                    Text = "Generazione e correzione usano la stessa pipeline: profilo del Tipo libro attivo, prompt provider-specific, intake/paradigmi reali, eventuale immagine base, Consistent e specifiche tecniche correnti.",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                }
            }
        };

        var root = FindOwningStack(page, actionRow);
        if (root is not null)
        {
            var index = root.Children.IndexOf(actionRow);
            root.Children.Insert(Math.Max(0, index), panel);
        }
        Refresh();
    }

    private static void EnsureCorrectionPanel(MainWindow window, Control page, PreviewProject project, string path)
    {
        if (Descendants(page).Any(c => string.Equals(c.Name, ReviewMarker, StringComparison.Ordinal))) return;
        var list = Descendants(page).OfType<ListBox>().FirstOrDefault();
        if (list is null) return;

        var instruction = new TextBox
        {
            Name = "AiImageCorrectionInstruction",
            MinHeight = 96,
            AcceptsReturn = true,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Watermark = "Es. Cambia soltanto il cappello in un berretto rosso; mantieni invariati personaggio, posa, sfondo, composizione e stile.",
            IsUndoEnabled = true
        };
        var preserveRest = new CheckBox
        {
            Name = "AiImageCorrectionPreserveRest",
            Content = "Mantieni invariato tutto ciò che non chiedo di cambiare",
            IsChecked = true
        };
        var create = Button("Crea Prompt Pack correzione", 230);
        var info = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };

        create.Click += async (_, _) =>
        {
            var selected = SelectedWorkUnit(list);
            if (selected is null)
            {
                info.Text = "Seleziona prima un'immagine da correggere.";
                return;
            }
            var change = (instruction.Text ?? string.Empty).Trim();
            if (change.Length == 0)
            {
                info.Text = "Descrivi la correzione da applicare.";
                instruction.Focus();
                return;
            }

            var state = AiExchangeStateStore.Load(project);
            var unit = state.WorkUnits.FirstOrDefault(x => x.WorkUnitId == selected.WorkUnitId);
            if (unit is null)
            {
                info.Text = "Work Unit non più disponibile nello stato AI corrente.";
                return;
            }
            if (unit.LegacyAiJobId is Guid legacyId && !VisualPromptSessionService.IsActiveJob(project, legacyId))
            {
                info.Text = "Questa immagine appartiene a una sessione/Tipo libro archiviato. Riattiva o ricrea il lavoro nel Tipo libro corrente prima di correggerla.";
                return;
            }

            var baseVersion = ResolveBaseVersion(state, unit);
            if (baseVersion?.MaterialId is not Guid)
            {
                info.Text = "La correzione richiede una versione base con un'immagine reale associata.";
                return;
            }

            unit.Mode = AiExchangeModes.AiWithInputAsReference;
            unit.Instruction = change;
            unit.Change = [change];
            unit.Preserve = preserveRest.IsChecked == true ? ["all unspecified visual elements"] : [];
            unit.Add = [];
            unit.Remove = [];
            AiExchangeStateStore.Save(project, state);
            await ProjectFileStore.SaveAsync(path, project);

            var file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Salva Prompt Pack di correzione",
                SuggestedFileName = $"diez-correzione-{SafeCode(unit.Code)}.zip",
                DefaultExtension = "zip",
                FileTypeChoices = [new FilePickerFileType("Prompt Pack Diez") { Patterns = ["*.zip"] }]
            });
            if (file is null) return;

            var result = await AiVisualPromptPackService.BuildAsync(
                project,
                path,
                state,
                [unit.WorkUnitId],
                EnsureZip(file.Path.LocalPath));
            info.Text = result.Success
                ? "Correzione pronta con la pipeline unica: immagine base reale + descrizione corrente + intake e descrizioni + paradigmi + preserve/change + prompt provider-specific + specifiche correnti."
                : result.Message;
            SetStatus(window, info.Text);
        };

        var panel = new StackPanel
        {
            Name = ReviewMarker,
            Spacing = 8,
            Children =
            {
                new Separator(),
                new TextBlock { Text = "Correggi l'immagine selezionata con AI", FontSize = 18 },
                new TextBlock
                {
                    Text = "La correzione usa la stessa pipeline della generazione iniziale. La base reale è autoritativa e tutto ciò che non viene chiesto di cambiare può essere preservato esplicitamente.",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                },
                Labeled("Cosa vuoi cambiare?", instruction),
                preserveRest,
                create,
                info
            }
        };

        if (list.Parent is StackPanel stack)
        {
            var index = stack.Children.IndexOf(list);
            stack.Children.Insert(Math.Min(stack.Children.Count, index + 1), panel);
            return;
        }

        var grid = Descendants(page).OfType<Grid>().FirstOrDefault(g => Descendants(g).Contains(list));
        if (grid is null) return;
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        Grid.SetRow(panel, grid.RowDefinitions.Count - 1);
        Grid.SetColumnSpan(panel, Math.Max(1, grid.ColumnDefinitions.Count));
        grid.Children.Add(panel);
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
        Control currentChild = child;
        var current = child.Parent;
        while (current is not null && !ReferenceEquals(current, page))
        {
            if (current is StackPanel panel && panel.Children.Contains(currentChild)) return panel;
            if (current is Control control) currentChild = control;
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
        var main = Field<TextBlock>(window, "_status");
        if (main is not null) main.Text = message;
        try
        {
            var host = SingleWindowEntryPointUi.GetHost(window);
            var status = Field<TextBlock>(host, "_status");
            if (status is not null) status.Text = message;
        }
        catch { }
    }

    private static StackPanel Labeled(string label, Control control) => new()
    {
        Spacing = 3,
        Children = { new TextBlock { Text = label, FontSize = 13 }, control }
    };

    private static Button Button(string text, double width) => new()
    {
        Content = text,
        Width = width,
        HorizontalContentAlignment = HorizontalAlignment.Center
    };

    private static string EnsureZip(string path) =>
        path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? path : path + ".zip";

    private static string SafeCode(string? value)
    {
        var text = string.IsNullOrWhiteSpace(value) ? "immagine" : value.Trim();
        return string.Concat(text.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
    }

    private static T? Field<T>(object owner, string name) where T : class =>
        owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(owner) as T;

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

    private sealed record IntakeRow(AiExchangeImageRequestContextService.IntakeImage Item)
    {
        public override string ToString()
        {
            var description = string.IsNullOrWhiteSpace(Item.Description) ? "senza descrizione" : Item.Description.Trim();
            if (description.Length > 90) description = description[..87] + "…";
            return $"{Item.Role} · {Item.Scope} · {description}";
        }
    }
}
