using System.IO.Compression;
using System.Text;

namespace DiezPublishingStudio;

/// <summary>
/// Safe wrapper around the V2 enhancer. It preserves core instructions, enriches the
/// package with real visual assets/context, adds normalized effective presets, strips
/// layout-only fields, then recomposes instructions without deleting an open ZIP entry.
/// </summary>
internal static class AiExchangeImageRequestContextSafeEnhancer
{
    private const string InstructionsName = "instructions.md";
    private const string AuthoritativeImageRule =
        "REGOLA AUTORITATIVA IMMAGINI: le descrizioni utente e le descrizioni correnti accompagnano e guidano il lavoro, ma non sostituiscono il file immagine reale. Per una correzione/modifica, usa sempre base_version.file come sorgente visiva autoritativa e applica su quella immagine preserve/change/add/remove e tutti i preset presenti in request-context.json.";

    public static async Task<AiExchangeImageRequestContextService.EnhanceResult> EnhancePromptPackAsync(
        PreviewProject project,
        string projectPath,
        AiExchangeState exchangeState,
        IEnumerable<Guid> workUnitIds,
        string promptPackPath)
    {
        if (!File.Exists(promptPackPath))
            return new AiExchangeImageRequestContextService.EnhanceResult(false, "Prompt Pack non trovato.", 0, 0);

        var originalInstructions = ReadAndDeleteInstructions(promptPackPath);
        AiExchangeImageRequestContextService.EnhanceResult result;
        try
        {
            result = await AiExchangeImageRequestContextService.EnhancePromptPackAsync(
                project, projectPath, exchangeState, workUnitIds, promptPackPath);
            if (result.Success)
            {
                AiExchangeExplicitVisualPresetContext.Ensure(promptPackPath, project);
                AiExchangeVisualLayoutSanitizer.Sanitize(promptPackPath);
            }
        }
        catch
        {
            RestoreInstructions(promptPackPath, originalInstructions);
            throw;
        }

        if (!result.Success)
        {
            RestoreInstructions(promptPackPath, originalInstructions);
            return result;
        }

        var visualInstructions = ReadAndDeleteInstructions(promptPackPath);
        var merged = MergeInstructions(originalInstructions, visualInstructions);
        RestoreInstructions(promptPackPath, merged);
        return result;
    }

    private static string ReadAndDeleteInstructions(string zipPath)
    {
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Update);
        var entry = archive.GetEntry(InstructionsName);
        if (entry is null) return string.Empty;
        string text;
        using (var stream = entry.Open())
        using (var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
            text = reader.ReadToEnd();
        entry.Delete();
        return text;
    }

    private static void RestoreInstructions(string zipPath, string text)
    {
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Update);
        archive.GetEntry(InstructionsName)?.Delete();
        var entry = archive.CreateEntry(InstructionsName, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(text ?? string.Empty);
    }

    private static string MergeInstructions(string original, string visual)
    {
        var parts = new List<string>();
        var a = (original ?? string.Empty).Trim();
        var b = (visual ?? string.Empty).Trim();
        if (a.Length > 0) parts.Add(a);
        if (b.Length > 0 && !a.Contains("Contesto visuale Diez V2", StringComparison.Ordinal)) parts.Add(b);
        var merged = string.Join(Environment.NewLine + Environment.NewLine, parts);
        if (!merged.Contains("non sostituiscono il file immagine reale", StringComparison.OrdinalIgnoreCase))
            merged = merged.TrimEnd() + Environment.NewLine + Environment.NewLine + AuthoritativeImageRule;
        return merged.Trim();
    }
}
