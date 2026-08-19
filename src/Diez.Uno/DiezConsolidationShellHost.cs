using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using DiezPublishingStudio;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.UI;

namespace DiezPublishingStudio.UnoSpike;

/// <summary>
/// Physical-review shell introduced after the 2026-08-19 UX review.
/// It groups the existing stable workspaces under six publisher-facing sections
/// without duplicating their persistence or Core logic.
/// </summary>
internal sealed class DiezConsolidationShellHost : ContentControl
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly SolidColorBrush Napoli = Brush("#007FFF");
    private static readonly SolidColorBrush NapoliDark = Brush("#005EB8");
    private static readonly SolidColorBrush NapoliDeep = Brush("#004A91");
    private static readonly SolidColorBrush White = Brush("#FFFFFF");
    private static readonly SolidColorBrush BorderBlue = Brush("#9CCFFF");
    private static readonly SolidColorBrush Ink = Brush("#12304A");

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

        var brand = new StackPanel
        {
            Spacing = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Children =
            {
                BrandText("Diez", 31, Microsoft.UI.Text.FontWeights.SemiBold),
                BrandText("∞", 40, Microsoft.UI.Text.FontWeights.SemiBold),
                BrandText("Publishing Studio", 14, Microsoft.UI.Text.FontWeights.Normal)
            }
        };

        var navigation = new StackPanel
        {
            Margin = new Thickness(16, 18, 16, 16),
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Children =
            {
                brand,
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
            if (box.FocusState != FocusState.Unfocused)
            {
                box.BorderBrush = Napoli;
                box.BorderThickness = new Thickness(3);
                box.Foreground = Ink;
            }
            else if (!_wiredTextBoxes.Contains(box))
            {
                box.BorderBrush = BorderBlue;
                box.BorderThickness = new Thickness(1);
            }

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
        }

        var count = VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < count; i++) WireTextFocus(VisualTreeHelper.GetChild(node, i));
    }

    private void ShowProject()
    {
        Invoke("ShowHome");
        var document = Document;
        if (document is null) return;
        if (ContentHost?.Content is StackPanel root)
        {
            RemoveTagged(root, "Diez.MaterialPreview");
            var preview = ProjectMaterialPreviewPanel.Build(document, Report);
            preview.Tag = "Diez.MaterialPreview";
            root.Children.Add(preview);
        }
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
            var phase = Math.Clamp(document.GetUiInt("Visual.ActivePhase", 1), 1, 4);
            ShowVisualProductionTab(phase);
            return;
        }

        Invoke("RouteCurrentBookType");
    }

    private void ShowVisualProductionTab(int selected)
    {
        var document = Document;
        if (document is null) return;

        if (selected == 4)
        {
            document.SetUiInt("Visual.ActivePhase", 4);
            Invoke("ShowVisualWorkspace");
        }
        else if (selected == 5)
        {
            Invoke("ShowScenesAndSubjects");
        }
        else
        {
            document.SetUiInt("Visual.ActivePhase", Math.Clamp(selected, 1, 4));
            Invoke("ShowVisualWorkspace");
        }

        var current = ContentHost?.Content;
        if (current is null) return;
        var names = new[] { "1 · Definizione", "2 · Prompt", "3 · Produzione", "4 · Revisione", "Scene e soggetti" };
        ContentHost!.Content = BuildTabView(names, selected - 1, current, async index =>
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
        var current = ContentHost?.Content;
        if (current is null) return;
        var names = new[] { "Testo principale", "Mappa contenuti + guida progetto", "Controllo coerenza" };
        ContentHost!.Content = BuildTabView(names, selected, current, index =>
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
        panel.Children.Add(new TextBlock
        {
            Text = "Materiali a corredo",
            FontSize = 19,
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Esporta materiali utente e asset AI approvati separatamente dal file del libro. Le Candidate AI non approvate restano nel progetto ma non entrano nel pacchetto di consegna.",
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(AsyncButton("Materiali del libro · ZIP", async () =>
        {
            var path = await PickSavePathAsync("Esporta materiali del libro", SafeName(document.EditionTitle) + "-materiali.zip", ".zip", "Archivio ZIP");
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
                    var path = await PickSavePathAsync("Esporta database completo Word Search", SafeName(document.EditionTitle) + "-database-completo.xlsx", ".xlsx", "Foglio Excel XLSX");
                    if (path is null) return;
                    Report(await UnoConsolidationExportService.ExportWordSearchFullDatabaseAsync(document, path));
                }),
                AsyncButton("Database del libro · XLSX", async () =>
                {
                    var path = await PickSavePathAsync("Esporta database del libro Word Search", SafeName(document.EditionTitle) + "-database-libro.xlsx", ".xlsx", "Foglio Excel XLSX");
                    if (path is null) return;
                    Report(await UnoConsolidationExportService.ExportWordSearchBookDatabaseAsync(document, path));
                })));
        }

        var card = new Border
        {
            Tag = "Diez.ConsolidatedExportTools",
            Padding = new Thickness(16),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = panel
        };
        root.Children.Add(card);
    }

    private void ShowFinalized() => Invoke("ShowFinalizedLibrary");

    private TabView BuildTabView(IReadOnlyList<string> names, int selected, UIElement currentContent, Func<int, Task> onChanged)
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
            if (tabs.SelectedIndex < 0) return;
            await onChanged(tabs.SelectedIndex);
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
            if (panel.Children[i] is FrameworkElement element && string.Equals(element.Tag?.ToString(), tag, StringComparison.Ordinal))
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
        return new SolidColorBrush(Color.FromArgb(255,
            Convert.ToByte(value[0..2], 16),
            Convert.ToByte(value[2..4], 16),
            Convert.ToByte(value[4..6], 16)));
    }
}

internal sealed record ProjectMaterialPreviewItem(
    Guid MaterialId,
    string FileName,
    string Kind,
    long SizeBytes,
    string Sha256,
    string Summary,
    string Preview,
    string SourcePath,
    string EmbeddedPath,
    bool IsEmbedded)
{
    public string Label => $"{FileName} · {Kind} · {FormatSize(SizeBytes)}";

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024d:0.#} KB";
        return $"{bytes / 1024d / 1024d:0.#} MB";
    }
}

/// <summary>
/// Universal best-effort preview: every imported material gets a verifiable surface.
/// Images are rendered; structured/document formats expose a readable structural preview;
/// ZIP-like packages list their internal entries; unknown binaries expose metadata/signature.
/// </summary>
internal static class ProjectMaterialPreviewPanel
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp"
    };

    public static FrameworkElement Build(DiezProjectDocument document, Action<string> report)
    {
        var items = ReadItems(document);
        var list = new ListView
        {
            MinHeight = 220,
            MaxHeight = 420,
            ItemsSource = items.Select(x => x.Label).ToList(),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var info = new TextBlock
        {
            Text = items.Count == 0 ? "Nessun materiale importato." : "Seleziona un materiale per verificarne contenuto, struttura e impronta.",
            TextWrapping = TextWrapping.Wrap
        };
        var previewHost = new ContentControl
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Content = ReadOnlyText("Nessuna anteprima selezionata.")
        };

        list.SelectionChanged += async (_, _) =>
        {
            if (list.SelectedIndex < 0 || list.SelectedIndex >= items.Count) return;
            var item = items[list.SelectedIndex];
            info.Text = $"{item.FileName} · {item.Kind} · {FormatSize(item.SizeBytes)}\nSHA-256: {item.Sha256}\n{item.Summary}";
            try
            {
                previewHost.Content = await BuildPreviewAsync(document, item);
                report($"Anteprima materiale: {item.FileName}");
            }
            catch (Exception ex)
            {
                previewHost.Content = ReadOnlyText("Anteprima non disponibile: " + ex.GetBaseException().Message);
                report($"Anteprima {item.FileName}: {ex.GetBaseException().Message}");
            }
        };

        var grid = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        var left = new StackPanel { Spacing = 8, Margin = new Thickness(0, 0, 14, 0), Children = { list, info } };
        Grid.SetColumn(left, 0);
        grid.Children.Add(left);
        Grid.SetColumn(previewHost, 1);
        grid.Children.Add(previewHost);

        return new Border
        {
            Padding = new Thickness(16),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = new StackPanel
            {
                Spacing = 9,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Children =
                {
                    new TextBlock { Text = "Anteprima e verifica materiali", FontSize = 19, TextWrapping = TextWrapping.Wrap },
                    new TextBlock
                    {
                        Text = "Ogni file importato deve poter essere verificato. Per gli archivi ZIP viene mostrato l'elenco dei file interni; per immagini l'anteprima grafica; per documenti e tabelle un estratto strutturale leggibile.",
                        TextWrapping = TextWrapping.Wrap
                    },
                    grid
                }
            }
        };
    }

    internal static IReadOnlyList<ProjectMaterialPreviewItem> ReadItems(DiezProjectDocument document)
    {
        var root = JsonNode.Parse(document.ExportProjectJson()) as JsonObject;
        if (root?["Materials"] is not JsonArray materials) return [];
        var result = new List<ProjectMaterialPreviewItem>();
        foreach (var material in materials.OfType<JsonObject>())
        {
            var id = ReadGuid(material, "MaterialId") ?? Guid.NewGuid();
            result.Add(new ProjectMaterialPreviewItem(
                id,
                ReadString(material, "FileName", "(senza nome)"),
                ReadString(material, "Kind", "Materiale"),
                ReadLong(material, "SizeBytes"),
                ReadString(material, "Sha256"),
                ReadString(material, "Summary"),
                ReadString(material, "Preview"),
                ReadString(material, "SourcePath"),
                ReadString(material, "EmbeddedPath"),
                ReadBool(material, "IsEmbedded")));
        }
        return result;
    }

    internal static async Task<string?> ResolveMaterialPathAsync(DiezProjectDocument document, ProjectMaterialPreviewItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.SourcePath) && File.Exists(item.SourcePath))
            return Path.GetFullPath(item.SourcePath);

        if (string.IsNullOrWhiteSpace(document.SourcePath) || string.IsNullOrWhiteSpace(item.EmbeddedPath) || !File.Exists(document.SourcePath))
            return null;

        try
        {
            using var archive = ZipFile.OpenRead(document.SourcePath);
            var entry = archive.Entries.FirstOrDefault(x => string.Equals(x.FullName, item.EmbeddedPath, StringComparison.OrdinalIgnoreCase));
            if (entry is null) return null;
            var cache = Path.Combine(Path.GetTempPath(), "DiezPublishingStudio", "MaterialPreview");
            Directory.CreateDirectory(cache);
            var extension = Path.GetExtension(item.FileName);
            var path = Path.Combine(cache, item.MaterialId.ToString("N") + extension);
            await using var input = entry.Open();
            await using var output = File.Create(path);
            await input.CopyToAsync(output);
            return path;
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    private static async Task<UIElement> BuildPreviewAsync(DiezProjectDocument document, ProjectMaterialPreviewItem item)
    {
        var path = await ResolveMaterialPathAsync(document, item);
        if (path is null) return ReadOnlyText("Il record è presente nel progetto, ma i byte del materiale non sono disponibili.");
        var extension = Path.GetExtension(item.FileName).ToLowerInvariant();

        if (ImageExtensions.Contains(extension)) return await ImagePreviewAsync(path, item.FileName);
        if (extension is ".zip" or ".diez") return ReadOnlyText(ZipPreview(path));
        if (extension == ".xlsx") return ReadOnlyText(XlsxPreview(path));
        if (extension == ".docx") return ReadOnlyText(DocxPreview(path));
        if (extension == ".odt") return ReadOnlyText(OdtPreview(path));
        if (extension == ".pdf") return ReadOnlyText(PdfPreview(path));
        if (extension == ".rtf") return ReadOnlyText(RtfPreview(path));
        if (extension is ".txt" or ".md" or ".csv" or ".tsv" or ".json" or ".xml")
            return ReadOnlyText(await ReadTextPreviewAsync(path));
        if (!string.IsNullOrWhiteSpace(item.Preview)) return ReadOnlyText(item.Preview);
        return ReadOnlyText(BinaryPreview(path));
    }

    private static async Task<UIElement> ImagePreviewAsync(string path, string caption)
    {
        var image = new Image
        {
            MinHeight = 360,
            MaxHeight = 620,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(path));
            using var stream = await file.OpenReadAsync();
            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(stream);
            image.Source = bitmap;
            return new StackPanel
            {
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Children = { image, new TextBlock { Text = caption, TextWrapping = TextWrapping.Wrap } }
            };
        }
        catch (Exception ex)
        {
            return ReadOnlyText("Impossibile decodificare l'immagine: " + ex.GetBaseException().Message);
        }
    }

    private static string ZipPreview(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        var lines = archive.Entries
            .Where(x => !string.IsNullOrWhiteSpace(x.FullName))
            .Take(500)
            .Select(x => $"{x.FullName} · {FormatSize(x.Length)}")
            .ToList();
        var suffix = archive.Entries.Count > lines.Count ? $"\n… altri {archive.Entries.Count - lines.Count} elementi" : string.Empty;
        return $"Archivio: {Path.GetFileName(path)}\nFile interni: {archive.Entries.Count}\n\n{string.Join(Environment.NewLine, lines)}{suffix}";
    }

    private static string XlsxPreview(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        var workbookEntry = archive.GetEntry("xl/workbook.xml") ?? throw new InvalidDataException("XLSX: workbook.xml mancante.");
        var relsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels") ?? throw new InvalidDataException("XLSX: relazioni workbook mancanti.");
        XDocument workbook;
        XDocument rels;
        using (var stream = workbookEntry.Open()) workbook = XDocument.Load(stream);
        using (var stream = relsEntry.Open()) rels = XDocument.Load(stream);
        XNamespace main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace officeRel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRel = "http://schemas.openxmlformats.org/package/2006/relationships";
        var firstSheet = workbook.Descendants(main + "sheet").FirstOrDefault() ?? throw new InvalidDataException("XLSX senza fogli.");
        var sheetName = (string?)firstSheet.Attribute("name") ?? "Foglio 1";
        var relation = (string?)firstSheet.Attribute(officeRel + "id") ?? string.Empty;
        var target = rels.Descendants(packageRel + "Relationship").FirstOrDefault(x => string.Equals((string?)x.Attribute("Id"), relation, StringComparison.Ordinal))?.Attribute("Target")?.Value
                     ?? throw new InvalidDataException("XLSX: foglio non trovato.");
        var normalized = target.Replace('\\', '/').TrimStart('/');
        var sheetEntry = archive.GetEntry(normalized.StartsWith("xl/", StringComparison.OrdinalIgnoreCase) ? normalized : "xl/" + normalized)
                         ?? throw new InvalidDataException("XLSX: XML del foglio mancante.");
        var shared = ReadSharedStrings(archive, main);
        XDocument sheet;
        using (var stream = sheetEntry.Open()) sheet = XDocument.Load(stream);
        var rows = new List<string>();
        foreach (var row in sheet.Descendants(main + "row").Take(30))
        {
            var cells = row.Elements(main + "c").Select(cell => ReadCellValue(cell, main, shared));
            rows.Add(string.Join(" | ", cells));
        }
        var total = sheet.Descendants(main + "row").Count();
        return $"XLSX · {sheetName} · {total} righe rilevate\n\n{string.Join(Environment.NewLine, rows)}";
    }

    private static string DocxPreview(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        var entry = archive.GetEntry("word/document.xml") ?? throw new InvalidDataException("DOCX: word/document.xml mancante.");
        XDocument document;
        using (var stream = entry.Open()) document = XDocument.Load(stream);
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var paragraphs = document.Descendants(w + "p")
            .Select(p => string.Concat(p.Descendants(w + "t").Select(t => t.Value)).Trim())
            .Where(x => x.Length > 0)
            .ToList();
        return $"DOCX · {paragraphs.Count} paragrafi\n\n{string.Join(Environment.NewLine, paragraphs.Take(60))}";
    }

    private static string OdtPreview(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        var entry = archive.GetEntry("content.xml") ?? throw new InvalidDataException("ODT: content.xml mancante.");
        XDocument document;
        using (var stream = entry.Open()) document = XDocument.Load(stream);
        XNamespace text = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";
        var paragraphs = document.Descendants()
            .Where(x => x.Name == text + "p" || x.Name == text + "h")
            .Select(x => string.Concat(x.DescendantNodes().OfType<XText>().Select(t => t.Value)).Trim())
            .Where(x => x.Length > 0)
            .ToList();
        return $"ODT · {paragraphs.Count} paragrafi\n\n{string.Join(Environment.NewLine, paragraphs.Take(60))}";
    }

    private static string PdfPreview(string path)
    {
        const int max = 16 * 1024 * 1024;
        using var stream = File.OpenRead(path);
        var count = (int)Math.Min(stream.Length, max);
        var buffer = new byte[count];
        var read = stream.Read(buffer, 0, count);
        var latin = Encoding.Latin1.GetString(buffer, 0, read);
        var pages = Regex.Matches(latin, @"/Type\s*/Page\b", RegexOptions.CultureInvariant).Count;
        var titleMatch = Regex.Match(latin, @"/Title\s*\((?<title>(?:\\.|[^)])*)\)", RegexOptions.CultureInvariant);
        var title = titleMatch.Success ? titleMatch.Groups["title"].Value : "(titolo non rilevato)";
        return $"PDF · {FormatSize(stream.Length)}\nPagine rilevate: {(pages > 0 ? pages.ToString() : "non determinate")}\nTitolo: {title}\n\nAnteprima strutturale: il PDF è stato letto ed è disponibile nel progetto. La resa grafica completa del PDF non è ancora incorporata in questa superficie Uno.";
    }

    private static string RtfPreview(string path)
    {
        var text = File.ReadAllText(path);
        text = Regex.Replace(text, @"\\par[d]?", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\\'[0-9a-fA-F]{2}", " ");
        text = Regex.Replace(text, @"\\[a-zA-Z]+-?\d* ?", "");
        text = text.Replace("{", string.Empty).Replace("}", string.Empty);
        return text.Length > 12000 ? text[..12000] + "\n…" : text;
    }

    private static async Task<string> ReadTextPreviewAsync(string path)
    {
        using var reader = new StreamReader(path, detectEncodingFromByteOrderMarks: true);
        var builder = new StringBuilder();
        for (var i = 0; i < 120 && await reader.ReadLineAsync() is { } line; i++)
        {
            builder.AppendLine(line);
            if (builder.Length > 16000) break;
        }
        return builder.ToString().TrimEnd();
    }

    private static string BinaryPreview(string path)
    {
        using var stream = File.OpenRead(path);
        var bytes = new byte[(int)Math.Min(256, stream.Length)];
        var read = stream.Read(bytes, 0, bytes.Length);
        return $"File binario · {FormatSize(stream.Length)}\nFirma iniziale (hex):\n{Convert.ToHexString(bytes, 0, read)}";
    }

    private static TextBox ReadOnlyText(string text) => new()
    {
        Text = text ?? string.Empty,
        IsReadOnly = true,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        MinHeight = 360,
        MaxHeight = 620,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto
    };

    private static List<string> ReadSharedStrings(ZipArchive archive, XNamespace main)
    {
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null) return [];
        XDocument document;
        using (var stream = entry.Open()) document = XDocument.Load(stream);
        return document.Descendants(main + "si").Select(x => string.Concat(x.Descendants(main + "t").Select(t => t.Value))).ToList();
    }

    private static string ReadCellValue(XElement cell, XNamespace main, IReadOnlyList<string> shared)
    {
        var type = (string?)cell.Attribute("t");
        if (string.Equals(type, "inlineStr", StringComparison.Ordinal))
            return string.Concat(cell.Descendants(main + "t").Select(t => t.Value));
        var raw = cell.Element(main + "v")?.Value ?? string.Empty;
        if (string.Equals(type, "s", StringComparison.Ordinal) && int.TryParse(raw, out var index) && index >= 0 && index < shared.Count)
            return shared[index];
        if (string.Equals(type, "b", StringComparison.Ordinal)) return raw == "1" ? "TRUE" : "FALSE";
        return raw;
    }

    private static Guid? ReadGuid(JsonObject obj, string name)
    {
        if (obj[name] is not JsonValue value) return null;
        if (value.TryGetValue<Guid>(out var id)) return id;
        return value.TryGetValue<string>(out var text) && Guid.TryParse(text, out id) ? id : null;
    }

    private static string ReadString(JsonObject obj, string name, string fallback = "") =>
        obj[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text ?? fallback : fallback;

    private static long ReadLong(JsonObject obj, string name) =>
        obj[name] is JsonValue value && value.TryGetValue<long>(out var result) ? result : 0;

    private static bool ReadBool(JsonObject obj, string name) =>
        obj[name] is JsonValue value && value.TryGetValue<bool>(out var result) && result;

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024d:0.#} KB";
        return $"{bytes / 1024d / 1024d:0.#} MB";
    }
}
