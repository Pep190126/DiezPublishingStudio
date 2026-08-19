using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;

namespace DiezPublishingStudio.UnoSpike;

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
    public string Label => $"{FileName} · {Kind} · {ProjectMaterialPreviewPanel.FormatSize(SizeBytes)}";
}

/// <summary>
/// Best-effort verification surface for project intake. Every material exposes either
/// a real preview or a structural/signature view so an import is never a black box.
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
            Text = items.Count == 0
                ? "Nessun materiale importato."
                : "Seleziona un materiale per verificarne contenuto, struttura e impronta.",
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

        var left = new StackPanel
        {
            Spacing = 8,
            Margin = new Thickness(0, 0, 14, 0),
            Children = { list, info }
        };
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
                        Text = "Ogni file importato deve poter essere verificato. ZIP: file interni; immagini: anteprima reale; documenti e tabelle: estratto strutturale leggibile.",
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

        if (string.IsNullOrWhiteSpace(document.SourcePath) ||
            string.IsNullOrWhiteSpace(item.EmbeddedPath) ||
            !File.Exists(document.SourcePath))
            return null;

        try
        {
            using var archive = ZipFile.OpenRead(document.SourcePath);
            var entry = archive.Entries.FirstOrDefault(x =>
                string.Equals(x.FullName, item.EmbeddedPath, StringComparison.OrdinalIgnoreCase));
            if (entry is null) return null;

            var cache = Path.Combine(Path.GetTempPath(), "DiezPublishingStudio", "MaterialPreview");
            Directory.CreateDirectory(cache);
            var path = Path.Combine(cache, item.MaterialId.ToString("N") + Path.GetExtension(item.FileName));
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
        if (path is null)
            return ReadOnlyText("Il record è presente nel progetto, ma i byte del materiale non sono disponibili.");

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
        var suffix = archive.Entries.Count > lines.Count
            ? $"\n… altri {archive.Entries.Count - lines.Count} elementi"
            : string.Empty;
        return $"Archivio: {Path.GetFileName(path)}\nFile interni: {archive.Entries.Count}\n\n{string.Join(Environment.NewLine, lines)}{suffix}";
    }

    private static string XlsxPreview(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        var workbookEntry = archive.GetEntry("xl/workbook.xml")
            ?? throw new InvalidDataException("XLSX: workbook.xml mancante.");
        var relsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels")
            ?? throw new InvalidDataException("XLSX: relazioni workbook mancanti.");

        XDocument workbook;
        XDocument rels;
        using (var stream = workbookEntry.Open()) workbook = XDocument.Load(stream);
        using (var stream = relsEntry.Open()) rels = XDocument.Load(stream);

        XNamespace main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace officeRel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRel = "http://schemas.openxmlformats.org/package/2006/relationships";

        var firstSheet = workbook.Descendants(main + "sheet").FirstOrDefault()
            ?? throw new InvalidDataException("XLSX senza fogli.");
        var sheetName = (string?)firstSheet.Attribute("name") ?? "Foglio 1";
        var relationId = (string?)firstSheet.Attribute(officeRel + "id") ?? string.Empty;
        var target = rels.Descendants(packageRel + "Relationship")
            .FirstOrDefault(x => string.Equals((string?)x.Attribute("Id"), relationId, StringComparison.Ordinal))
            ?.Attribute("Target")?.Value
            ?? throw new InvalidDataException("XLSX: foglio non trovato.");
        var normalized = target.Replace('\\', '/').TrimStart('/');
        var sheetEntry = archive.GetEntry(normalized.StartsWith("xl/", StringComparison.OrdinalIgnoreCase)
            ? normalized
            : "xl/" + normalized)
            ?? throw new InvalidDataException("XLSX: XML del foglio mancante.");

        var shared = ReadSharedStrings(archive, main);
        XDocument sheet;
        using (var stream = sheetEntry.Open()) sheet = XDocument.Load(stream);

        var rows = new List<string>();
        foreach (var row in sheet.Descendants(main + "row").Take(30))
        {
            rows.Add(string.Join(" | ", row.Elements(main + "c")
                .Select(cell => ReadCellValue(cell, main, shared))));
        }
        var total = sheet.Descendants(main + "row").Count();
        return $"XLSX · {sheetName} · {total} righe rilevate\n\n{string.Join(Environment.NewLine, rows)}";
    }

    private static string DocxPreview(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        var entry = archive.GetEntry("word/document.xml")
            ?? throw new InvalidDataException("DOCX: word/document.xml mancante.");
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
        var entry = archive.GetEntry("content.xml")
            ?? throw new InvalidDataException("ODT: content.xml mancante.");
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
        return $"PDF · {FormatSize(stream.Length)}\nPagine rilevate: {(pages > 0 ? pages.ToString() : "non determinate")}\nTitolo: {title}\n\nVerifica strutturale completata. La resa grafica completa della pagina PDF non è ancora incorporata in questa superficie Uno.";
    }

    private static string RtfPreview(string path)
    {
        var text = File.ReadAllText(path);
        text = Regex.Replace(text, @"\\par[d]?", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\\'[0-9a-fA-F]{2}", " ");
        text = Regex.Replace(text, @"\\[a-zA-Z]+-?\d* ?", string.Empty);
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

    private static UIElement ReadOnlyText(string text)
    {
        var box = new TextBox
        {
            Text = text ?? string.Empty,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        return new ScrollViewer
        {
            MinHeight = 360,
            MaxHeight = 620,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = box
        };
    }

    private static List<string> ReadSharedStrings(ZipArchive archive, XNamespace main)
    {
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null) return [];
        XDocument document;
        using (var stream = entry.Open()) document = XDocument.Load(stream);
        return document.Descendants(main + "si")
            .Select(x => string.Concat(x.Descendants(main + "t").Select(t => t.Value)))
            .ToList();
    }

    private static string ReadCellValue(XElement cell, XNamespace main, IReadOnlyList<string> shared)
    {
        var type = (string?)cell.Attribute("t");
        if (string.Equals(type, "inlineStr", StringComparison.Ordinal))
            return string.Concat(cell.Descendants(main + "t").Select(t => t.Value));
        var raw = cell.Element(main + "v")?.Value ?? string.Empty;
        if (string.Equals(type, "s", StringComparison.Ordinal) &&
            int.TryParse(raw, out var index) && index >= 0 && index < shared.Count)
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

    internal static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024d:0.#} KB";
        return $"{bytes / 1024d / 1024d:0.#} MB";
    }
}
