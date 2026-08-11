using System.IO.Compression;
using System.Text;

namespace DiezPublishingStudio;

/// <summary>
/// ZIP-safety wrapper for the visual context layer. It preserves core instructions while the
/// context service adds real files/context and the explicit-preset layer normalizes important
/// visual values. Layout sanitization and final prompt compilation belong to AiVisualPromptPackService.
/// </summary>
internal static class AiExchangeImageRequestContextSafeEnhancer
{
    private const string InstructionsName = "instructions.md";
    private const string AuthoritativeImageRule =
        "REGOLA AUTORITATIVA IMMAGINI: le descrizioni utente e correnti accompagnano e guidano il lavoro, ma non sostituiscono il file immagine reale. Per una correzione/modifica, usa base_version.file come sorgente visiva autoritativa e applica preserve/change/add/remove sul file reale.";

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
                AiExchangeExplicitVisualPresetContext.Ensure(promptPackPath, project);
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
        RestoreInstructions(promptPackPath, MergeInstructions(originalInstructions, visualInstructions));
        return result;
    }

    private static string ReadAndDeleteInstructions(string zipPath)
    {
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Update);
        var entry = archive.GetEntry(InstructionsName);
        if (entry is null) return string.Empty;
        string text;
        using (var stream = entry.Open())
        using (var reader = new StreamReader(stream, Encoding.UTF8, true))
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
        if (b.Length > 0 &&
            !a.Contains("Contesto visuale Diez V2", StringComparison.Ordinal) &&
            !a.Contains("Contesto visuale Diez V3", StringComparison.Ordinal))
            parts.Add(b);
        var merged = string.Join(Environment.NewLine + Environment.NewLine, parts);
        if (!merged.Contains("non sostituiscono il file immagine reale", StringComparison.OrdinalIgnoreCase) &&
            !merged.Contains("non sostituisce mai l'immagine", StringComparison.OrdinalIgnoreCase))
            merged = merged.TrimEnd() + Environment.NewLine + Environment.NewLine + AuthoritativeImageRule;
        return merged.Trim();
    }
}
