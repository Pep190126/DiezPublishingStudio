using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace DiezPublishingStudio;

internal static class AiProductionService
{
    public const string TypeImage = "Image";
    public const string TypeText = "Text";
    public const string TypeData = "Data";

    public const string StatusReady = "Ready";
    public const string StatusToReview = "ToReview";
    public const string StatusApproved = "Approved";
    public const string StatusNeedsRevision = "NeedsRevision";
    public const string StatusRejected = "Rejected";
    public const string StatusApplied = "Applied";

    private static readonly HashSet<string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        TypeImage, TypeText, TypeData
    };

    public static AiProductionJob CreateJob(
        PreviewProject project,
        string outputType,
        string title,
        string request,
        Guid? targetContentId = null)
    {
        project.AiProduction ??= new AiProductionSettings();
        project.AiProductionJobs ??= [];

        outputType = NormalizeType(outputType);
        var now = DateTimeOffset.Now.ToString("O");
        var job = new AiProductionJob
        {
            JobId = Guid.NewGuid(),
            Code = NextCode(project, outputType),
            OutputType = outputType,
            Title = (title ?? string.Empty).Trim(),
            Request = (request ?? string.Empty).Trim(),
            TargetContentId = targetContentId,
            Status = StatusReady,
            CreatedAtLocal = now,
            UpdatedAtLocal = now
        };
        job.Prompt = BuildPrompt(project, job);
        project.AiProductionJobs.Add(job);
        return job;
    }

    public static void SetProjectBrief(PreviewProject project, string? brief)
    {
        project.AiProduction ??= new AiProductionSettings();
        project.AiProduction.ProjectBrief = (brief ?? string.Empty).Trim();
    }

    public static void RebuildPrompt(PreviewProject project, AiProductionJob job)
    {
        job.Prompt = BuildPrompt(project, job);
        Touch(job);
    }

    public static string BuildPrompt(PreviewProject project, AiProductionJob job)
    {
        var brief = (project.AiProduction?.ProjectBrief ?? string.Empty).Trim();
        var request = (job.Request ?? string.Empty).Trim();
        var title = (job.Title ?? string.Empty).Trim();
        var outputInstruction = NormalizeType(job.OutputType) switch
        {
            TypeImage => "OUTPUT RICHIESTO: una singola immagine coerente con il brief. Non aggiungere testo, cornici o elementi non richiesti.",
            TypeData => "OUTPUT RICHIESTO: dati strutturati facili da riportare in CSV/XLSX. Mantieni una struttura regolare e non aggiungere commenti estranei ai dati.",
            _ => "OUTPUT RICHIESTO: solo il testo proposto, pronto per essere revisionato. Non presentarlo come già approvato o già applicato al progetto."
        };

        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(brief))
        {
            builder.AppendLine("BRIEF GENERALE DEL PROGETTO:");
            builder.AppendLine(brief);
            builder.AppendLine();
        }
        builder.AppendLine($"JOB DIEZ: {job.Code}");
        if (!string.IsNullOrWhiteSpace(title)) builder.AppendLine($"TITOLO / SOGGETTO: {title}");
        builder.AppendLine("RICHIESTA SPECIFICA:");
        builder.AppendLine(string.IsNullOrWhiteSpace(request) ? "Segui il brief generale per questo elemento." : request);
        builder.AppendLine();
        builder.AppendLine(outputInstruction);
        return builder.ToString().Trim();
    }

    public static void SetTextResult(AiProductionJob job, string? resultText)
    {
        job.ResultText = resultText ?? string.Empty;
        job.Status = string.IsNullOrWhiteSpace(job.ResultText) ? StatusReady : StatusToReview;
        Touch(job);
    }

    public static async Task<AiProductionActionResult> AttachResultFileAsync(
        PreviewProject project,
        string projectPath,
        AiProductionJob job,
        string resultPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
            return new(false, "Salva prima il progetto .diez.");
        if (string.IsNullOrWhiteSpace(resultPath) || !File.Exists(resultPath))
            return new(false, "Il file risultato non esiste.");

        var material = await MaterialImporter.ImportAsync(resultPath);
        if (string.Equals(NormalizeType(job.OutputType), TypeImage, StringComparison.Ordinal) &&
            !IllustrationPlanService.IsImage(material))
            return new(false, "Questo job richiede un'immagine: scegli un file PNG, JPEG, GIF, BMP o WebP.");

        var existing = project.Materials.FirstOrDefault(m =>
            string.Equals(m.Sha256, material.Sha256, StringComparison.OrdinalIgnoreCase));
        var added = false;
        if (existing is null)
        {
            material.Summary = $"Risultato AI {job.Code} · {material.Summary}";
            material.Preview = $"Risultato associato al job {job.Code}.\n\n{material.Preview}";
            project.Materials.Add(material);
            existing = material;
            added = true;
        }

        var previousMaterialId = job.ResultMaterialId;
        var previousStatus = job.Status;
        job.ResultMaterialId = existing.MaterialId;
        job.Status = StatusToReview;
        Touch(job);

        try
        {
            await ProjectFileStore.SaveAsync(projectPath, project);
            return new(true, added
                ? $"Risultato {existing.FileName} incorporato nel .diez e collegato a {job.Code}. Ora controllalo e approvalo oppure chiedi una revisione."
                : $"Il file era già nel progetto: {job.Code} è stato collegato all'originale esistente {existing.FileName}.");
        }
        catch
        {
            job.ResultMaterialId = previousMaterialId;
            job.Status = previousStatus;
            if (added) project.Materials.Remove(existing);
            throw;
        }
    }

    public static AiProductionActionResult Approve(PreviewProject project, AiProductionJob job)
    {
        if (!HasResult(project, job))
            return new(false, "Prima collega o incolla un risultato da controllare.");
        job.Status = StatusApproved;
        Touch(job);
        return new(true, $"{job.Code} approvato. Il risultato resta collegato al job e al progetto.");
    }

    public static AiProductionActionResult NeedsRevision(AiProductionJob job)
    {
        job.Status = StatusNeedsRevision;
        Touch(job);
        return new(true, $"{job.Code} segnato da rifare. Puoi modificare la richiesta, ricostruire il prompt e generare una nuova versione.");
    }

    public static AiProductionActionResult Reject(AiProductionJob job)
    {
        job.Status = StatusRejected;
        Touch(job);
        return new(true, $"{job.Code} scartato. Il job rimane nella cronologia del progetto.");
    }

    public static AiProductionActionResult ApplyApprovedText(PreviewProject project, AiProductionJob job)
    {
        if (!string.Equals(NormalizeType(job.OutputType), TypeText, StringComparison.Ordinal))
            return new(false, "Solo un job di testo può essere applicato al Testo di lavoro.");
        if (!string.Equals(job.Status, StatusApproved, StringComparison.Ordinal))
            return new(false, "Approva prima il risultato AI.");
        if (job.TargetContentId is not Guid contentId || contentId == Guid.Empty)
            return new(false, "Questo job non è collegato a un capitolo o una sezione del Testo di lavoro.");
        if (string.IsNullOrWhiteSpace(job.ResultText))
            return new(false, "Il job non contiene un testo risultato da applicare.");

        var result = EditableMasterService.ApplyManualEdit(
            project,
            contentId,
            job.ResultText,
            $"Testo applicato esplicitamente dal job AI {job.Code}; risultato revisionato e approvato dall'utente.");
        if (!result.Changed) return new(false, result.Message);

        job.Status = StatusApplied;
        Touch(job);
        return new(true, $"{job.Code} applicato al Testo di lavoro. L'originale importato resta invariato.");
    }

    public static bool HasResult(PreviewProject project, AiProductionJob job) =>
        !string.IsNullOrWhiteSpace(job.ResultText) ||
        (job.ResultMaterialId.HasValue && project.Materials.Any(m => m.MaterialId == job.ResultMaterialId.Value));

    public static string DisplayType(string type) => NormalizeType(type) switch
    {
        TypeImage => "Immagine",
        TypeData => "Dati / tabella",
        _ => "Testo"
    };

    public static string DisplayStatus(string status) => status switch
    {
        StatusReady => "Pronto da generare",
        StatusToReview => "Da controllare",
        StatusApproved => "Approvato",
        StatusNeedsRevision => "Da rifare",
        StatusRejected => "Scartato",
        StatusApplied => "Applicato al libro",
        _ => status
    };

    private static string NormalizeType(string? type) =>
        AllowedTypes.FirstOrDefault(t => string.Equals(t, type, StringComparison.OrdinalIgnoreCase)) ?? TypeText;

    private static string NextCode(PreviewProject project, string outputType)
    {
        var prefix = outputType switch
        {
            TypeImage => "IMG",
            TypeData => "DAT",
            _ => "TXT"
        };
        var used = project.AiProductionJobs
            .Select(j => j.Code ?? string.Empty)
            .Where(c => c.StartsWith(prefix + "-", StringComparison.OrdinalIgnoreCase))
            .Select(c => int.TryParse(c[(prefix.Length + 1)..], out var n) ? n : 0)
            .DefaultIfEmpty(0)
            .Max();
        return $"{prefix}-{used + 1:D3}";
    }

    private static void Touch(AiProductionJob job) => job.UpdatedAtLocal = DateTimeOffset.Now.ToString("O");
}
