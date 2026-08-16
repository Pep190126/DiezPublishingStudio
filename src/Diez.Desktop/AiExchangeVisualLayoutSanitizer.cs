using System.IO.Compression;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DiezPublishingStudio;

/// <summary>
/// Removes layout/print-stage settings from the AI request even when they survive in an older
/// .diez project. Orientation is derived from aspect ratio; bleed and safety margins belong to
/// the layout engine and must not be presented to an image-generation provider as creative presets.
/// </summary>
internal static class AiExchangeVisualLayoutSanitizer
{
    private const string ContextName = "request-context.json";
    private const string InstructionsName = "instructions.md";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    private static readonly string[] RemovedKeys =
    [
        "orientation", "Orientation",
        "safe_margin", "SafeMargin",
        "bleed", "Bleed",
        "bleed_amount", "BleedAmount"
    ];

    public static void Sanitize(string promptPackPath)
    {
        using var archive = ZipFile.Open(promptPackPath, ZipArchiveMode.Update);
        SanitizeContext(archive);
        SanitizeInstructions(archive);
    }

    private static void SanitizeContext(ZipArchive archive)
    {
        var entry = archive.GetEntry(ContextName);
        if (entry is null) return;

        string text;
        using (var stream = entry.Open())
        using (var reader = new StreamReader(stream, Encoding.UTF8, true))
            text = reader.ReadToEnd();

        var root = JsonNode.Parse(text);
        if (root is null) return;
        RemoveRecursively(root);

        entry.Delete();
        var replacement = archive.CreateEntry(ContextName, CompressionLevel.Optimal);
        using var target = replacement.Open();
        using var writer = new StreamWriter(target, new UTF8Encoding(false));
        writer.Write(root.ToJsonString(JsonOptions));
    }

    private static void RemoveRecursively(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            foreach (var key in RemovedKeys) obj.Remove(key);
            foreach (var child in obj.Select(x => x.Value).Where(x => x is not null).ToList())
                RemoveRecursively(child!);
        }
        else if (node is JsonArray array)
        {
            foreach (var child in array.Where(x => x is not null).ToList())
                RemoveRecursively(child!);
        }
    }

    private static void SanitizeInstructions(ZipArchive archive)
    {
        var entry = archive.GetEntry(InstructionsName);
        if (entry is null) return;

        string text;
        using (var stream = entry.Open())
        using (var reader = new StreamReader(stream, Encoding.UTF8, true))
            text = reader.ReadToEnd();

        text = text
            .Replace(", margini, bleed e Consistent", ", formato e Consistent", StringComparison.OrdinalIgnoreCase)
            .Replace(", margini e bleed", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("margini, bleed", string.Empty, StringComparison.OrdinalIgnoreCase);

        entry.Delete();
        var replacement = archive.CreateEntry(InstructionsName, CompressionLevel.Optimal);
        using var target = replacement.Open();
        using var writer = new StreamWriter(target, new UTF8Encoding(false));
        writer.Write(text);
    }
}
