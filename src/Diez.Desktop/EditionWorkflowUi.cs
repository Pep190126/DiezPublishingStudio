using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace DiezPublishingStudio;

internal static class EditionWorkflowUi
{
    private const string ProjectFieldName = "_project";
    private const string ProjectPathFieldName = "_currentProjectPath";
    private const string StatusFieldName = "_status";

    public static void Attach(MainWindow window)
    {
        window.Title = "Diez Publishing Studio — 0.8.2 Preview";

        if (window.Content is not Border border || border.Child is not StackPanel root)
            return;

        var subtitle = root.Children
            .OfType<TextBlock>()
            .FirstOrDefault(t => t.Text?.StartsWith("Preview 0.8", StringComparison.Ordinal) == true);
        if (subtitle is not null)
            subtitle.Text = "Preview 0.8.2 — Editable Master + Edition Freeze + preflight editoriale";

        var projectButtons = root.Children
            .OfType<StackPanel>()
            .FirstOrDefault(panel => panel.Orientation == Orientation.Horizontal &&
                                     panel.Children.OfType<Button>().Any(button =>
                                         string.Equals(button.Content?.ToString(), "Nuovo progetto", StringComparison.Ordinal)));
        if (projectButtons is null) return;
        if (projectButtons.Children.OfType<Button>().Any(button =>
                string.Equals(button.Content?.ToString(), "Edizione / Preflight", StringComparison.Ordinal)))
            return;

        var editionButton = new Button
        {
            Content = "Edizione / Preflight",
            Width = 160,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        editionButton.Click += async (_, _) =>
        {
            if (!TryGetSession(window, out var project, out var projectPath))
            {
                SetMainStatus(window, "Prima crea o apri un progetto .diez per usare Edition Freeze e preflight.");
                return;
            }

            var dialog = new EditionPreflightWindow(project, projectPath);
            await dialog.ShowDialog(window);

            var freezeCount = EditionFreezeService.FreezeCount(project);
            var current = freezeCount > 0 && EditionFreezeService.IsLatestFreezeCurrent(project);
            SetMainStatus(window, freezeCount == 0
                ? "Nessun Edition Freeze creato. Il progetto resta modificabile normalmente."
                : $"Edition Freeze: {freezeCount} snapshot · ultimo {(current ? "corrente" : "superato da modifiche successive")}.");
        };

        projectButtons.Children.Add(editionButton);
    }

    private static bool TryGetSession(MainWindow window, out PreviewProject project, out string projectPath)
    {
        project = null!;
        projectPath = string.Empty;

        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var projectField = typeof(MainWindow).GetField(ProjectFieldName, flags);
        var pathField = typeof(MainWindow).GetField(ProjectPathFieldName, flags);
        if (projectField?.GetValue(window) is not PreviewProject currentProject) return false;
        if (pathField?.GetValue(window) is not string currentPath || string.IsNullOrWhiteSpace(currentPath)) return false;

        project = currentProject;
        projectPath = currentPath;
        return true;
    }

    private static void SetMainStatus(MainWindow window, string message)
    {
        var statusField = typeof(MainWindow).GetField(StatusFieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        if (statusField?.GetValue(window) is TextBlock status) status.Text = message;
    }
}

internal sealed class EditionPreflightWindow : Window
{
    private readonly PreviewProject _project;
    private readonly string _projectPath;
    private readonly TextBlock _freezeState;
    private readonly TextBlock _summary;
    private readonly ListBox _checks;

    public EditionPreflightWindow(PreviewProject project, string projectPath)
    {
        _project = project;
        _projectPath = projectPath;

        Title = "Edition Freeze / Preflight";
        Width = 900;
        Height = 650;
        MinWidth = 720;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var heading = new TextBlock
        {
            Text = "Controllo finale dell'edizione",
            FontSize = 24,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        };
        var explanation = new TextBlock
        {
            Text = "Edition Freeze crea uno snapshot immutabile del Master e della Bible. Il preflight verifica che quello snapshot sia ancora corrente e che non restino blocchi editoriali prima della fase di pubblicazione.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        };

        _freezeState = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        _summary = new TextBlock
        {
            FontSize = 17,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        };
        _checks = new ListBox
        {
            Height = 320,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var freezeButton = new Button { Content = "Crea Edition Freeze", Width = 180 };
        freezeButton.Click += async (_, _) => await CreateFreezeAsync();
        var preflightButton = new Button { Content = "Esegui preflight", Width = 170 };
        preflightButton.Click += (_, _) => RefreshPreflight();
        var closeButton = new Button { Content = "Chiudi", Width = 120 };
        closeButton.Click += (_, _) => Close();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { freezeButton, preflightButton, closeButton }
        };

        Content = new Border
        {
            Padding = new Thickness(20),
            Child = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,*,Auto"),
                RowSpacing = 12,
                Children =
                {
                    heading,
                    explanation.WithGridRow(1),
                    _freezeState.WithGridRow(2),
                    _summary.WithGridRow(3),
                    _checks.WithGridRow(4),
                    buttons.WithGridRow(5)
                }
            }
        };

        Opened += (_, _) => RefreshPreflight();
    }

    private async Task CreateFreezeAsync()
    {
        var before = EditionFreezeService.FreezeCount(_project);
        var result = EditionFreezeService.CreateFreeze(_project);
        var after = EditionFreezeService.FreezeCount(_project);

        if (result.Freeze is null)
        {
            _summary.Text = result.Message;
            UpdateFreezeState();
            return;
        }

        try
        {
            if (after > before)
                await ProjectFileStore.SaveAsync(_projectPath, _project);
            _summary.Text = result.Message;
            RefreshPreflight();
        }
        catch (Exception ex)
        {
            _summary.Text = $"Edition Freeze creato in memoria, ma il salvataggio del .diez è fallito: {ex.Message}. Riapri il progetto prima di continuare.";
            UpdateFreezeState();
        }
    }

    private void RefreshPreflight()
    {
        UpdateFreezeState();
        var result = EditionFreezeService.RunPreflight(_project);
        _summary.Text = result.Summary;
        _checks.ItemsSource = result.Checks
            .Select(check =>
            {
                var symbol = check.Passed ? "✓" : check.Severity == "Warning" ? "!" : "✕";
                var level = check.Severity == "Warning" ? "ATTENZIONE" : "BLOCCANTE";
                return $"{symbol}  [{level}]  {check.Code} — {check.Message}";
            })
            .ToList();
    }

    private void UpdateFreezeState()
    {
        var count = EditionFreezeService.FreezeCount(_project);
        var latest = EditionFreezeService.GetLatestFreeze(_project);
        if (latest is null)
        {
            _freezeState.Text = "Edition Freeze: nessuno snapshot creato.";
            return;
        }

        var sequence = string.IsNullOrWhiteSpace(latest.ProposedValue) ? count.ToString() : latest.ProposedValue;
        var current = EditionFreezeService.IsLatestFreezeCurrent(_project);
        _freezeState.Text = $"Edition Freeze #{sequence} · {latest.CreatedAtLocal} · totale {count} · stato: {(current ? "CORRENTE" : "SUPERATO")}.";
    }
}
