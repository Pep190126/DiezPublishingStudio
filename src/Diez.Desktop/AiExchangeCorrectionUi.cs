using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;

namespace DiezPublishingStudio;

internal static class AiExchangeCorrectionUi
{
    public static void Attach(MainWindow window)
    {
        if (window.Content is not Border border || border.Child is not StackPanel root) return;
        var projectButtons = root.Children.OfType<StackPanel>()
            .FirstOrDefault(panel => panel.Orientation == Orientation.Horizontal &&
                                     panel.Children.OfType<Button>().Any(b => string.Equals(b.Content?.ToString(), "Nuovo progetto", StringComparison.Ordinal)));
        if (projectButtons is null || projectButtons.Children.OfType<Button>().Any(b => string.Equals(b.Content?.ToString(), "Correggi con AI", StringComparison.Ordinal))) return;

        var button = new Button
        {
            Content = "Correggi con AI",
            Width = 145,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        ToolTip.SetTip(button, "Correggi un singolo risultato mantenendo invariato ciò che non chiedi di cambiare. Consistent resta attivo quando previsto.");
        button.Click += async (_, _) =>
        {
            if (!TrySession(window, out var project, out var path))
            {
                SetStatus(window, "Prima crea o apri un progetto .diez.");
                return;
            }
            await new AiExchangeCorrectionWindow(project, path, message => SetStatus(window, message)).ShowDialog(window);
        };
        projectButtons.Children.Add(button);
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
        var status = typeof(MainWindow).GetField("_status", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(window) as TextBlock;
        if (status is not null) status.Text = message;
    }
}

internal sealed class AiExchangeCorrectionWindow : Window
{
    private readonly PreviewProject _project;
    private readonly string _projectPath;
    private readonly Action<string> _mainStatus;
    private readonly AiExchangeState _state;
    private readonly ListBox _items;
    private readonly TextBox _instruction;
    private readonly CheckBox _preserveRest;
    private readonly TextBlock _baseInfo;
    private readonly TextBlock _status;

    public AiExchangeCorrectionWindow(PreviewProject project, string projectPath, Action<string> mainStatus)
    {
        _project = project;
        _projectPath = projectPath;
        _mainStatus = mainStatus;
        _state = AiExchangeStateStore.Load(project);

        Title = "Correggi un risultato con AI";
        Width = 900;
        Height = 640;
        MinWidth = 760;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _items = new ListBox { Height = 210 };
        _items.SelectionChanged += (_, _) => RefreshBaseInfo();
        _instruction = new TextBox
        {
            AcceptsReturn = true,
            Height = 120,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Watermark = "Es. Cambia soltanto il cappello con un berretto rosso."
        };
        _preserveRest = new CheckBox
        {
            Content = "Mantieni invariato tutto ciò che non ho chiesto di cambiare",
            IsChecked = true
        };
        _baseInfo = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        _status = new TextBlock
        {
            Text = "La correzione mantiene la stessa Work Unit: il risultato tornerà come nuova versione dello stesso elemento, non come nuovo elemento.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        };

        var pack = new Button
        {
            Content = "Crea Prompt Pack correzione",
            Width = 225,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        pack.Click += async (_, _) => await CreateCorrectionPackAsync();

        Content = new Border
        {
            Padding = new Thickness(18),
            Child = new StackPanel
            {
                Spacing = 11,
                Children =
                {
                    new TextBlock { Text = "Modifica solo ciò che serve", FontSize = 24 },
                    new TextBlock
                    {
                        Text = "Seleziona il contenuto da correggere. Per Coloring Book e Raccolta immagini Diez conserva automaticamente il contesto Consistent e i paradigmi già associati.",
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap
                    },
                    _items,
                    _baseInfo,
                    new TextBlock { Text = "Cosa vuoi cambiare?", FontSize = 17 },
                    _instruction,
                    _preserveRest,
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { pack } },
                    _status
                }
            }
        };

        RefreshItems();
    }

    private CorrectionRow? Selected => _items.SelectedItem as CorrectionRow;

    private void RefreshItems()
    {
        var rows = _state.WorkUnits
            .Select(unit => new
            {
                Unit = unit,
                Version = ResolveBaseVersion(unit)
            })
            .Where(x => x.Version is not null)
            .OrderBy(x => x.Unit.Position)
            .ThenBy(x => x.Unit.Code)
            .Select(x => new CorrectionRow(x.Unit, x.Version!))
            .ToList();
        _items.ItemsSource = rows;
        if (rows.Count > 0) _items.SelectedIndex = 0;
        else _baseInfo.Text = "Non ci sono ancora risultati da correggere.";
    }

    private AiExchangeVersion? ResolveBaseVersion(AiExchangeWorkUnit unit)
    {
        if (unit.ApprovedVersionId is Guid approvedId)
        {
            var approved = _state.Versions.FirstOrDefault(v => v.VersionId == approvedId);
            if (approved is not null) return approved;
        }
        return _state.Versions
            .Where(v => v.WorkUnitId == unit.WorkUnitId && v.Status != AiExchangeVersionStatuses.Rejected)
            .OrderByDescending(v => v.VersionNumber)
            .FirstOrDefault();
    }

    private void RefreshBaseInfo()
    {
        if (Selected is not { } selected) return;
        var consistent = selected.Unit.SharedContextIds
            .Select(id => _state.SharedContexts.FirstOrDefault(c => c.SharedContextId == id))
            .Any(c => c?.ConsistentEnabled == true);
        _baseInfo.Text = $"Base: {selected.Unit.Code} versione {selected.Version.VersionNumber}" +
                         (selected.Version.Status == AiExchangeVersionStatuses.Approved ? " approvata" : " corrente") +
                         (consistent ? " · Consistent attivo" : string.Empty) +
                         $" · prossima versione prevista: {AiExchangeStateStore.NextVersionNumber(_state, selected.Unit.WorkUnitId)}";
    }

    private async Task CreateCorrectionPackAsync()
    {
        if (Selected is not { } selected)
        {
            Report("Seleziona prima un risultato da correggere.");
            return;
        }
        var instruction = (_instruction.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(instruction))
        {
            Report("Scrivi cosa vuoi cambiare.");
            return;
        }

        selected.Unit.Mode = AiExchangeModes.AiWithInputAsReference;
        selected.Unit.Change = [instruction];
        selected.Unit.Preserve = _preserveRest.IsChecked == true
            ? ["all unspecified elements"]
            : [];
        selected.Unit.Add = [];
        selected.Unit.Remove = [];
        selected.Unit.Instruction = instruction;

        AiExchangeStateStore.Save(_project, _state);
        await ProjectFileStore.SaveAsync(_projectPath, _project);

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Salva Prompt Pack di correzione",
            SuggestedFileName = $"diez-correzione-{selected.Unit.Code.ToLowerInvariant()}.zip",
            DefaultExtension = "zip",
            FileTypeChoices = [new FilePickerFileType("Prompt Pack Diez") { Patterns = ["*.zip"] }]
        });
        if (file is null) return;

        var result = await AiExchangePromptPackBuilder.BuildAsync(
            _project,
            _projectPath,
            _state,
            [selected.Unit.WorkUnitId],
            file.Path.LocalPath);
        Report(result.Success
            ? result.Message + " La nuova risposta verrà ricondotta allo stesso elemento con una nuova versione e descrizione aggiornata."
            : result.Message);
    }

    private void Report(string message)
    {
        _status.Text = message;
        _mainStatus(message);
    }

    private sealed record CorrectionRow(AiExchangeWorkUnit Unit, AiExchangeVersion Version)
    {
        public override string ToString() => $"{Unit.Code} · v{Version.VersionNumber} · {Version.Status}";
    }
}
