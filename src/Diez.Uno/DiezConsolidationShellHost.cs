using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using DiezPublishingStudio;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace DiezPublishingStudio.UnoSpike;

/// <summary>
/// Publisher-facing physical-review shell. It groups the existing stable workspaces under
/// the six product sections without duplicating Core persistence or the visual production engine.
/// </summary>
internal sealed class DiezConsolidationShellHost : ContentControl
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly SolidColorBrush Napoli = Brush("#007FFF");
    private static readonly SolidColorBrush NapoliDark = Brush("#005EB8");
    private static readonly SolidColorBrush White = Brush("#FFFFFF");
    private static readonly SolidColorBrush BorderBlue = Brush("#9CCFFF");

    private readonly MainShellPage _shell;
    private readonly UIElement _polishedShell;
    private readonly TextBlock _projectMirror = new() { TextWrapping = TextWrapping.Wrap, Foreground = White };
    private readonly TextBlock _statusMirror = new() { TextWrapping = TextWrapping.Wrap, Foreground = White, FontSize = 12 };
    private readonly HashSet<TextBox> _wiredTextBoxes = [];
    private bool _closingDialogOpen;

    public DiezConsolidationShellHost(MainShellPage shell, UIElement polishedShell)
    {
        _shell = shell;
        _polishedShell = polishedShell;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        Background = Napoli;
        Content = BuildShell();
        HideLegacySidebar();
        Loaded += (_, _) => RefreshPresentation();
        LayoutUpdated += (_, _) => RefreshPresentation();
    }

    public async Task<bool> ConfirmCloseAsync()
    {
        if (_closingDialogOpen) return false;
        _closingDialogOpen = true;
        try
        {
            var dirty = await HasUnsavedCanonicalChangesAsync();
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = dirty ? "Salvare prima di uscire?" : "Uscire da Diez?",
                Content = dirty
                    ? "Il progetto contiene modifiche non ancora salvate. Vuoi salvarle prima di chiudere Diez?"
                    : "Il progetto risulta salvato. Sei sicuro di voler uscire?",
                PrimaryButtonText = dirty ? "Salva e chiudi" : "Esci",
                CloseButtonText = "Annulla",
                DefaultButton = ContentDialogButton.Primary
            };
            if (dirty) dialog.SecondaryButtonText = "Esci senza salvare";

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.None) return false;
            if (!dirty) return result == ContentDialogResult.Primary;
            if (result == ContentDialogResult.Secondary) return true;

            await InvokeAsync("SaveProjectAsync");
            return !await HasUnsavedCanonicalChangesAsync();
        }
        finally
        {
            _closingDialogOpen = false;
        }
    }

    private UIElement BuildShell()
    {
        var root = new Grid { Background = Napoli };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(270) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var navigation = new StackPanel
        {
            Margin = new Thickness(16, 18, 16, 16),
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Children =
            {
                new StackPanel
                {
                    Spacing = 0,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Children =
                    {
                        BrandText("Diez", 31, Microsoft.UI.Text.FontWeights.SemiBold),
                        BrandText("∞", 40, Microsoft.UI.Text.FontWeights.SemiBold),
                        BrandText("Publishing Studio", 14, Microsoft.UI.Text.FontWeights.Normal)
                    }
                },
                new Separator(),
                _projectMirror,
                new Separator()
            }
        };

        navigation.Children.Add(NavButton("Progetto", ShowProject));
        navigation.Children.Add(NavButton("Tipo libro", ShowBookType));
        navigation.Children.Add(NavButton("Produzione", ShowProduction));
        navigation.Children.Add(NavButton("Controlli e revisione", ShowReview));
        navigation.Children.Add(NavButton("Esportazione", ShowExport));
        navigation.Children.Add(NavButton("Libri finalizzati", ShowFinalized));
        navigation.Children.Add(new Separator());
        navigation.Children.Add(_statusMirror);

        var sidebar = new Border
        {
            Background = Napoli,
            Child = new ScrollViewer
            {
                Background = Napoli,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = navigation
            }
        };
        Grid.SetColumn(sidebar, 0);
        root.Children.Add(sidebar);

        var workspace = new Border
        {
            Background = Napoli,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Child = _polishedShell
        };
        Grid.SetColumn(workspace, 1);
        root.Children.Add(workspace);
        return root;
    }

    private void HideLegacySidebar()
    {
        if (_shell.Content is not Grid oldRoot || oldRoot.ColumnDefinitions.Count < 2) return;
        oldRoot.ColumnDefinitions[0].Width = new GridLength(0);
        oldRoot.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
        oldRoot.HorizontalAlignment = HorizontalAlignment.Stretch;
        foreach (var child in oldRoot.Children)
        {
            if (Grid.GetColumn(child) == 0) child.Visibility = Visibility.Collapsed;
        }
    }

    private void RefreshPresentation()
    {
        HideLegacySidebar();
        _projectMirror.Text = GetField<TextBlock>("_projectHeader")?.Text ?? "Nessun progetto aperto";
        _statusMirror.Text = GetField<TextBlock>("_status")?.Text ?? "Pronto.";
        WireTextFocus(_polishedShell);
    }

    private void WireTextFocus(DependencyObject node)
    {
        if (node is TextBox box)
        {
            box.SelectionHighlightColor = Napoli;
            if (_wiredTextBoxes.Add(box))
            {
                box.GotFocus += (_, _) =>
                {
                    box.BorderBrush = Napoli;
                    box.BorderThickness = new Thickness(3);
                    box.SelectionHighlightColor = Napoli;
                };
                box.LostFocus += (_, _) =>
                {
                    box.BorderBrush = BorderBlue;
                    box.BorderThickness = new Thickness(1);
                };
            }
            if (box.FocusState != FocusState.Unfocused)
            {
                box.BorderBrush = Napoli;
                box.BorderThickness = new Thickness(3);
            }
        }

        var count = VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < count; i++) WireTextFocus(VisualTreeHelper.GetChild(node, i));
    }

    private void ShowProject()
    {
        Invoke("ShowHome");
        var document = Document;
        if (document is null) return;
        if (ContentHost?.Content is not StackPanel root) return;

        RemoveTagged(root, "Diez.MaterialPreview");
        var preview = ProjectMaterialPreviewPanel.Build(document, Report);
        preview.Tag = "Diez.MaterialPreview";
        root.Children.Add(preview);
    }

    private void ShowBookType() => Invoke("ShowBookRoute");

    private void ShowProduction()
    {
        var document = Document;
        if (document is null)
        {
            ShowProject();
            Report("Prima crea o apri un progetto .diez.");
            return;
        }

        var type = BookTypeCatalog.Normalize(document.BookType);
        if (BookTypeCatalog.IsVisual(type))
        {
            ShowVisualProductionTab(Math.Clamp(document.GetUiInt("Visual.ActivePhase", 1), 1, 4));
            return;
        }

        Invoke("RouteCurrentBookType");
    }

    private void ShowVisualProductionTab(int selected)
    {
        var document = Document;
        if (document is null) return;
        selected = Math.Clamp(selected, 1, 5);

        if (selected == 5)
        {
            Invoke("ShowScenesAndSubjects");
        }
        else
        {
            document.SetUiInt("Visual.ActivePhase", selected);
            Invoke("ShowVisualWorkspace");
        }

        if (ContentHost?.Content is not UIElement currentContent) return;
        var names = new[]
        {
            "1 · Definizione",
            "2 · Prompt",
            "3 · Produzione",
            "4 · Revisione",
            "Scene e soggetti"
        };
        ContentHost.Content = BuildTabView(names, selected - 1, currentContent, async index =>
        {
            var target = index + 1;
            if (target <= 4)
            {
                document.SetUiInt("Visual.ActivePhase", target);
                await InvokeAsync("SaveIfPossibleAsync");
            }
            ShowVisualProductionTab(target);
        });
    }

    private void ShowReview() => ShowReviewTab(0);

    private void ShowReviewTab(int selected)
    {
        if (Document is null)
        {
            ShowProject();
            Report("Prima crea o apri un progetto .diez.");
            return;
        }

        var methods = new[] { "ShowEditableMaster", "ShowContentGraph", "ShowConsistency" };
        selected = Math.Clamp(selected, 0, methods.Length - 1);
        Invoke(methods[selected]);
        if (ContentHost?.Content is not UIElement currentContent) return;

        var names = new[]
        {
            "Testo principale",
            "Mappa contenuti + guida progetto",
            "Controllo coerenza"
        };
        ContentHost.Content = BuildTabView(names, selected, currentContent, index =>
        {
            ShowReviewTab(index);
            return Task.CompletedTask;
        });
    }

    private void ShowExport()
    {
        Invoke("ShowExportAndFinalization");
        var document = Document;
        if (document is null || ContentHost?.Content is not StackPanel root) return;

        RemoveTagged(root, "Diez.ConsolidatedExportTools");
        var panel = new StackPanel { Spacing = 9, HorizontalAlignment = HorizontalAlignment.Stretch };
        panel.Children.Add(new TextBlock { Text = "Materiali a corredo", FontSize = 19, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock
        {
            Text = "Esporta materiali utente e asset AI approvati separatamente dal file del libro. Le Candidate AI non approvate restano nel progetto ma non entrano nel pacchetto di consegna.",
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(AsyncButton("Materiali del libro · ZIP", async () =>
        {
            var path = await PickSavePathAsync(
                "Esporta materiali del libro",
                SafeName(document.EditionTitle) + "-materiali.zip",
                ".zip",
                "Archivio ZIP");
            if (path is null) return;
            Report(await UnoConsolidationExportService.ExportMaterialsZipAsync(document, path));
        }));

        if (string.Equals(BookTypeCatalog.Normalize(document.BookType), BookTypeCatalog.WordSearch, StringComparison.OrdinalIgnoreCase))
        {
            panel.Children.Add(new Separator());
            panel.Children.Add(new TextBlock { Text = "Word Search · database XLSX", FontSize = 19, TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(new TextBlock
            {
                Text = "Il database completo contiene tutto il lessico canonico disponibile; il database del libro contiene soltanto le parole effettivamente usate nei puzzle correnti.",
                TextWrapping = TextWrapping.Wrap
            });
            panel.Children.Add(Horizontal(
                AsyncButton("Database completo · XLSX", async () =>
                {
                    var path = await PickSavePathAsync(
                        "Esporta database completo Word Search",
                        SafeName(document.EditionTitle) + "-database-completo.xlsx",
                        ".xlsx",
                        "Foglio Excel XLSX");
                    if (path is null) return;
                    Report(await UnoConsolidationExportService.ExportWordSearchFullDatabaseAsync(document, path));
                }),
                AsyncButton("Database del libro · XLSX", async () =>
                {
                    var path = await PickSavePathAsync(
                        "Esporta database del libro Word Search",
                        SafeName(document.EditionTitle) + "-database-libro.xlsx",
                        ".xlsx",
                        "Foglio Excel XLSX");
                    if (path is null) return;
                    Report(await UnoConsolidationExportService.ExportWordSearchBookDatabaseAsync(document, path));
                })));
        }

        root.Children.Add(new Border
        {
            Tag = "Diez.ConsolidatedExportTools",
            Padding = new Thickness(16),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = panel
        });
    }

    private void ShowFinalized() => Invoke("ShowFinalizedLibrary");

    private static TabView BuildTabView(
        IReadOnlyList<string> names,
        int selected,
        UIElement currentContent,
        Func<int, Task> onChanged)
    {
        var tabs = new TabView
        {
            IsAddTabButtonVisible = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        for (var i = 0; i < names.Count; i++)
        {
            tabs.TabItems.Add(new TabViewItem
            {
                IsClosable = false,
                Header = new TextBlock
                {
                    Text = names[i],
                    Foreground = White,
                    TextWrapping = TextWrapping.NoWrap
                },
                Content = i == selected ? currentContent : null
            });
        }
        tabs.SelectedIndex = Math.Clamp(selected, 0, names.Count - 1);
        tabs.SelectionChanged += async (_, _) =>
        {
            if (tabs.SelectedIndex >= 0) await onChanged(tabs.SelectedIndex);
        };
        return tabs;
    }

    private async Task<string?> PickSavePathAsync(string title, string suggestedFileName, string extension, string typeName)
    {
        var picker = new Windows.Storage.Pickers.FileSavePicker
        {
            SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary,
            SuggestedFileName = Path.GetFileNameWithoutExtension(suggestedFileName)
        };
        picker.FileTypeChoices.Add(typeName, new List<string> { extension });
        var file = await picker.PickSaveFileAsync();
        return file?.Path;
    }

    private async Task<bool> HasUnsavedCanonicalChangesAsync()
    {
        var document = Document;
        if (document is null) return false;
        var path = ProjectPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return true;

        try
        {
            var memory = NormalizeForComparison(JsonNode.Parse(document.ExportProjectJson()));
            JsonNode? disk;
            try
            {
                using var archive = ZipFile.OpenRead(path);
                var entry = archive.GetEntry("project.json");
                if (entry is null) return true;
                using var reader = new StreamReader(entry.Open(), Encoding.UTF8, true);
                disk = JsonNode.Parse(await reader.ReadToEndAsync());
            }
            catch (InvalidDataException)
            {
                disk = JsonNode.Parse(await File.ReadAllTextAsync(path));
            }

            disk = NormalizeForComparison(disk);
            return !JsonNode.DeepEquals(memory, disk);
        }
        catch
        {
            return true;
        }
    }

    private static JsonNode? NormalizeForComparison(JsonNode? node)
    {
        var clone = node?.DeepClone();
        if (clone is JsonObject obj) obj.Remove("SavedAtLocal");
        return clone;
    }

    private DiezProjectDocument? Document => GetField<DiezProjectDocument>("_document");
    private string? ProjectPath => GetField<string>("_projectPath");
    private ContentControl? ContentHost => GetField<ContentControl>("_contentHost");

    private T? GetField<T>(string name) where T : class =>
        typeof(MainShellPage).GetField(name, PrivateInstance)?.GetValue(_shell) as T;

    private object? Invoke(string name) =>
        typeof(MainShellPage).GetMethod(name, PrivateInstance)?.Invoke(_shell, null);

    private async Task InvokeAsync(string name)
    {
        if (Invoke(name) is Task task) await task;
    }

    private void Report(string message)
    {
        var status = GetField<TextBlock>("_status");
        if (status is not null) status.Text = message;
        _statusMirror.Text = message;
    }

    private static void RemoveTagged(Panel panel, string tag)
    {
        for (var i = panel.Children.Count - 1; i >= 0; i--)
        {
            if (panel.Children[i] is FrameworkElement element &&
                string.Equals(element.Tag?.ToString(), tag, StringComparison.Ordinal))
                panel.Children.RemoveAt(i);
        }
    }

    private static Button NavButton(string text, Action action)
    {
        var button = new Button
        {
            Content = text,
            Background = NapoliDark,
            Foreground = White,
            BorderBrush = BorderBlue,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(14, 9)
        };
        button.Click += (_, _) => action();
        return button;
    }

    private static Button AsyncButton(string text, Func<Task> action)
    {
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(14, 8),
            Background = NapoliDark,
            Foreground = White,
            BorderBrush = BorderBlue
        };
        button.Click += async (_, _) => await action();
        return button;
    }

    private static StackPanel Horizontal(params UIElement[] items)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 9 };
        foreach (var item in items) panel.Children.Add(item);
        return panel;
    }

    private static TextBlock BrandText(string text, double size, Windows.UI.Text.FontWeight weight) => new()
    {
        Text = text,
        FontSize = size,
        FontWeight = weight,
        Foreground = White,
        TextAlignment = TextAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };

    private static string SafeName(string? value)
    {
        var raw = string.IsNullOrWhiteSpace(value) ? "libro" : value.Trim();
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(raw.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(safe) ? "libro" : safe;
    }

    private static SolidColorBrush Brush(string hex)
    {
        var value = hex.TrimStart('#');
        return new SolidColorBrush(Color.FromArgb(
            255,
            Convert.ToByte(value[0..2], 16),
            Convert.ToByte(value[2..4], 16),
            Convert.ToByte(value[4..6], 16)));
    }
}
