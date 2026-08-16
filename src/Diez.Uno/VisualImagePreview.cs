using System.IO.Compression;
using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;

namespace DiezPublishingStudio.UnoSpike;

internal sealed record DiezImagePreviewAsset(
    Guid MaterialId,
    string FileName,
    string Kind,
    string SourcePath,
    string EmbeddedPath,
    bool IsEmbedded,
    string Origin,
    string State)
{
    public string Label => string.IsNullOrWhiteSpace(State)
        ? $"{FileName} · {Origin}"
        : $"{FileName} · {Origin} · {State}";
}

/// <summary>
/// Read-only projection used by the Uno shell to preview visual materials without creating
/// a second persistence model. The material bytes remain owned by the .diez package adapter.
/// </summary>
internal static class DiezImagePreviewCatalog
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif"
    };

    public static IReadOnlyList<DiezImagePreviewAsset> Read(DiezProjectDocument document)
    {
        var root = JsonNode.Parse(document.ExportProjectJson()) as JsonObject;
        if (root?["Materials"] is not JsonArray materials) return [];

        var aiState = new Dictionary<Guid, (string Origin, string State, int Version)>( );
        foreach (var job in document.AiJobs()
                     .Where(j => string.Equals(j.OutputType, "Image", StringComparison.OrdinalIgnoreCase) && j.WorkUnitId.HasValue))
        {
            foreach (var version in document.AiVersions(job.WorkUnitId!.Value))
            {
                if (!version.MaterialId.HasValue) continue;
                var origin = string.Equals(job.Status, "Applied", StringComparison.OrdinalIgnoreCase) &&
                             string.Equals(version.Status, "APPROVED", StringComparison.OrdinalIgnoreCase)
                    ? "Nel libro"
                    : string.Equals(version.Status, "APPROVED", StringComparison.OrdinalIgnoreCase)
                        ? "Approvata"
                        : "Proposta AI";
                var state = $"v{version.VersionNumber} · {version.DisplayStatus}";
                if (!aiState.TryGetValue(version.MaterialId.Value, out var current) || version.VersionNumber >= current.Version)
                    aiState[version.MaterialId.Value] = (origin, state, version.VersionNumber);
            }
        }

        var result = new List<DiezImagePreviewAsset>();
        foreach (var material in materials.OfType<JsonObject>())
        {
            var materialId = ReadGuid(material, "MaterialId");
            if (!materialId.HasValue) continue;
            var fileName = ReadString(material, "FileName", "Immagine");
            var kind = ReadString(material, "Kind", "Materiale");
            if (!IsImage(kind, fileName)) continue;

            var ai = aiState.TryGetValue(materialId.Value, out var mapped) ? mapped : default;
            result.Add(new DiezImagePreviewAsset(
                materialId.Value,
                fileName,
                kind,
                ReadString(material, "SourcePath"),
                ReadString(material, "EmbeddedPath"),
                ReadBool(material, "IsEmbedded"),
                string.IsNullOrWhiteSpace(ai.Origin) ? "Materiale aggiunto" : ai.Origin,
                ai.State ?? string.Empty));
        }

        return result;
    }

    public static async Task<string?> ResolvePathAsync(DiezProjectDocument document, DiezImagePreviewAsset asset)
    {
        if (!string.IsNullOrWhiteSpace(asset.SourcePath) && File.Exists(asset.SourcePath))
            return Path.GetFullPath(asset.SourcePath);

        if (string.IsNullOrWhiteSpace(asset.EmbeddedPath) ||
            string.IsNullOrWhiteSpace(document.SourcePath) ||
            !File.Exists(document.SourcePath))
            return null;

        try
        {
            using var archive = ZipFile.OpenRead(document.SourcePath);
            var entry = archive.Entries.FirstOrDefault(x =>
                string.Equals(x.FullName, asset.EmbeddedPath, StringComparison.OrdinalIgnoreCase));
            if (entry is null) return null;

            var extension = Path.GetExtension(asset.FileName);
            if (!ImageExtensions.Contains(extension)) extension = ".img";
            var cacheDirectory = Path.Combine(Path.GetTempPath(), "DiezPublishingStudio", "PreviewCache");
            Directory.CreateDirectory(cacheDirectory);
            var cachePath = Path.Combine(cacheDirectory, asset.MaterialId.ToString("N") + extension.ToLowerInvariant());

            await using var source = entry.Open();
            await using var target = File.Create(cachePath);
            await source.CopyToAsync(target);
            return cachePath;
        }
        catch (InvalidDataException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static bool IsImage(string kind, string fileName) =>
        string.Equals(kind, "Image", StringComparison.OrdinalIgnoreCase) ||
        ImageExtensions.Contains(Path.GetExtension(fileName));

    private static Guid? ReadGuid(JsonObject obj, string name)
    {
        if (obj[name] is not JsonValue value) return null;
        if (value.TryGetValue<Guid>(out var guid)) return guid;
        return value.TryGetValue<string>(out var text) && Guid.TryParse(text, out guid) ? guid : null;
    }

    private static string ReadString(JsonObject obj, string name, string fallback = "")
    {
        if (obj[name] is JsonValue value && value.TryGetValue<string>(out var text))
            return text ?? fallback;
        return fallback;
    }

    private static bool ReadBool(JsonObject obj, string name)
    {
        return obj[name] is JsonValue value && value.TryGetValue<bool>(out var result) && result;
    }
}

/// <summary>
/// One preview surface for imported materials, AI candidates, approved versions and assets already
/// applied to the book. It never changes approval or placement state when the selection changes.
/// </summary>
internal sealed class VisualImagePreviewSurface
{
    private readonly Image _image;
    private readonly TextBlock _caption;
    private readonly TextBlock _message;

    public VisualImagePreviewSurface(double minHeight = 360)
    {
        _image = new Image
        {
            MinHeight = minHeight,
            MaxHeight = 620,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        _caption = new TextBlock { TextWrapping = TextWrapping.Wrap };
        _message = new TextBlock
        {
            Text = "Seleziona un'immagine per visualizzarne l'anteprima.",
            TextWrapping = TextWrapping.Wrap
        };

        View = new Border
        {
            Padding = new Thickness(14),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = new StackPanel
            {
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Children =
                {
                    _image,
                    _caption,
                    _message
                }
            }
        };
    }

    public FrameworkElement View { get; }

    public void Clear(string message = "Seleziona un'immagine per visualizzarne l'anteprima.")
    {
        _image.Source = null;
        _caption.Text = string.Empty;
        _message.Text = message;
    }

    public async Task ShowAssetAsync(DiezProjectDocument document, DiezImagePreviewAsset asset, string? caption = null)
    {
        var path = await DiezImagePreviewCatalog.ResolvePathAsync(document, asset);
        await ShowPathAsync(
            path,
            caption ?? asset.Label,
            path is null
                ? "Il record dell'immagine è presente, ma i byte non sono disponibili nel file originale né nel package .diez."
                : string.Empty);
    }

    public Task ShowFileAsync(string path, string caption) => ShowPathAsync(path, caption, string.Empty);

    private async Task ShowPathAsync(string? path, string caption, string missingMessage)
    {
        _image.Source = null;
        _caption.Text = caption;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            _message.Text = string.IsNullOrWhiteSpace(missingMessage)
                ? "Immagine non disponibile."
                : missingMessage;
            return;
        }

        try
        {
            var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(path));
            using var stream = await file.OpenReadAsync();
            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(stream);
            _image.Source = bitmap;
            _message.Text = string.Empty;
        }
        catch (Exception ex)
        {
            _image.Source = null;
            _message.Text = "Impossibile decodificare l'anteprima: " + ex.GetBaseException().Message;
        }
    }
}
