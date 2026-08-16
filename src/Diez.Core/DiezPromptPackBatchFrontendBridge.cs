using System.IO.Compression;
using System.Text;

namespace DiezPublishingStudio;

/// <summary>
/// Adds the human/AI entry point to the canonical manual Prompt Pack ZIP.
///
/// One ZIP represents one batch delivery to the AI. Diez still keeps one Work Unit per requested
/// image so versioning, Vision and editorial promotion remain independently auditable. Technical
/// identifiers stay in prompt-manifest.json and are not injected into the visual renderer prompts.
/// </summary>
public static class DiezPromptPackBatchFrontendBridge
{
    public const string PromptEntryName = "PROMPT.md";

    public static string BuildPackagePrompt(string projectJson, IEnumerable<Guid>? workUnitIds = null)
    {
        var items = DiezPromptPackFrontendBridge.Preview(projectJson, workUnitIds)
            .Where(x => string.Equals(x.ContentType, "Image", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (items.Count == 0)
            throw new InvalidOperationException("Non ci sono Prompt immagine pronti per il Prompt Pack.");

        var sb = new StringBuilder();
        sb.AppendLine("# DIEZ ∞ PUBLISHING STUDIO — PROMPT PACK IMMAGINI");
        sb.AppendLine();
        sb.AppendLine("Questo ZIP è il pacchetto completo da eseguire. Non chiedere all'utente di copiare i singoli prompt e non richiedere una nuova chat per ogni immagine.");
        sb.AppendLine($"Il lotto contiene ESATTAMENTE {items.Count} immagini da generare come asset separati, nell'ordine indicato qui sotto.");
        sb.AppendLine();
        sb.AppendLine("## Regole di esecuzione del lotto");
        sb.AppendLine("1. Tratta ogni blocco DIEZ VISUAL PROMPT come una richiesta di rendering indipendente, ma gestisci l'intero lotto all'interno di questa consegna ZIP.");
        sb.AppendLine("2. Genera una sola immagine finale per blocco. Non creare collage, griglie, contact sheet, tavole multiple o alternative nello stesso asset.");
        sb.AppendLine("3. Mantieni fra immagini soltanto le regole Consistent espresse nei prompt. Non trascinare automaticamente soggetti, pose, oggetti o Scene specifiche dal blocco precedente.");
        sb.AppendLine("4. `prompt-manifest.json` contiene gli identificatori tecnici necessari a Diez per ricomporre i risultati. Usali soltanto nel Response Pack e non inserirli nelle immagini né nei prompt del renderer.");
        sb.AppendLine("5. Eventuali reference/materiali sono sotto `inputs/` e vanno usati solo per i ruoli dichiarati nel manifest.");
        sb.AppendLine("6. Ogni risultato rientra come Candidate. Non approvare implicitamente: Vision/review e `Porta nel libro` restano fasi Diez separate.");
        sb.AppendLine("7. Al termine restituisci, quando il sistema lo consente, UN SOLO Response ZIP `diez-response` contenente un risultato distinto per ogni Work Unit del manifest.");
        sb.AppendLine();
        for (var i = 0; i < items.Count; i++)
        {
            sb.AppendLine($"## Immagine {i + 1:D3} di {items.Count:D3}");
            sb.AppendLine("<<< DIEZ VISUAL PROMPT START >>>");
            sb.AppendLine(items[i].Prompt.Trim());
            sb.AppendLine("<<< DIEZ VISUAL PROMPT END >>>");
            sb.AppendLine();
        }
        sb.AppendLine("## Controllo prima della consegna");
        sb.AppendLine($"Devono esistere {items.Count} risultati separati. Nessun ID, nome file tecnico, watermark o etichetta Diez deve comparire dentro le immagini.");
        sb.AppendLine("Se la piattaforma dimostra di contaminare un rendering con immagini precedenti, usa il fallback clean-room previsto dal protocollo storico; non è il percorso manuale predefinito.");
        return sb.ToString().Trim();
    }

    public static async Task<DiezPromptPackBuildResult> BuildManualPackageAsync(string projectJson, string? projectPackagePath, IEnumerable<Guid>? workUnitIds, string outputPath)
    {
        var ids = workUnitIds?.Where(x => x != Guid.Empty).Distinct().ToList();
        var packagePrompt = BuildPackagePrompt(projectJson, ids);
        var built = await DiezPromptPackFrontendBridge.BuildManualAsync(projectJson, projectPackagePath, ids, outputPath);
        if (!built.Success || string.IsNullOrWhiteSpace(built.OutputPath) || !File.Exists(built.OutputPath)) return built;
        try
        {
            using var archive = ZipFile.Open(built.OutputPath, ZipArchiveMode.Update);
            archive.GetEntry(PromptEntryName)?.Delete();
            var entry = archive.CreateEntry(PromptEntryName, CompressionLevel.Optimal);
            await using var stream = entry.Open();
            await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            await writer.WriteAsync(packagePrompt);
        }
        catch (Exception ex)
        {
            return built with
            {
                Success = false,
                Status = "PROMPT_ENTRY_FAILED",
                Message = "Lo ZIP canonico è stato creato ma manca PROMPT.md; il Prompt Pack non viene considerato consegnabile: " + ex.GetBaseException().Message
            };
        }
        return built with
        {
            Message = $"Prompt Pack ZIP pronto: {built.WorkUnitCount} immagini in un'unica consegna · {Path.GetFileName(built.OutputPath)} · ingresso AI: {PromptEntryName}."
        };
    }
}
