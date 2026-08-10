using System.IO.Compression;
using System.Text;

namespace DiezPublishingStudio;

/// <summary>
/// Safe wrapper around the V2 enhancer. It removes instructions.md only after closing
/// the read stream, lets the V2 service enrich the package, then recomposes the original
/// master instructions with the V2 visual rules.
/// </summary>
internal static class AiExchangeImageRequestContextSafeEnhancer
{
    private const string InstructionsName = "instructions.md";

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
        var a = (original ?? string.Empty).TrimEnd();
        var b = (visual ?? string.Empty).Trim();
        if (a.Length == 0) return b;
        if (b.Length == 0) return a;
        if (a.Contains("Contesto visuale Diez V2", StringComparison.Ordinal)) return a;
        return a + Environment.NewLine + Environment.NewLine + b;
    }
}
