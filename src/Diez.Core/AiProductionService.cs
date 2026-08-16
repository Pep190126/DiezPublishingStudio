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
        StatusApplied => "Applicato al testo",
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

internal static class AiPromptPackExportService
{
    public static string SuggestedCsvFileName(PreviewProject project) => $"{SafeBaseName(project)}-prompt-ai.csv";
    public static string SuggestedXlsxFileName(PreviewProject project) => $"{SafeBaseName(project)}-prompt-ai.xlsx";

    public static async Task<AiProductionActionResult> ExportCsvAsync(PreviewProject project, string path)
    {
        if (project.AiProductionJobs.Count == 0) return new(false, "Non ci sono job AI da esportare.");
        var fullPath = EnsureExtension(path, ".csv");
        EnsureDirectory(fullPath);
        var b = new StringBuilder();
        AppendCsv(b, "Codice", "Tipo", "Titolo", "Richiesta", "Prompt", "Stato");
        foreach (var job in project.AiProductionJobs.OrderBy(j => j.Code, StringComparer.OrdinalIgnoreCase))
            AppendCsv(b, job.Code, AiProductionService.DisplayType(job.OutputType), job.Title, job.Request, job.Prompt, AiProductionService.DisplayStatus(job.Status));
        await File.WriteAllTextAsync(fullPath, b.ToString(), new UTF8Encoding(true));
        return new(true, $"Prompt pack CSV esportato: {Path.GetFileName(fullPath)}");
    }

    public static async Task<AiProductionActionResult> ExportXlsxAsync(PreviewProject project, string path)
    {
        if (project.AiProductionJobs.Count == 0) return new(false, "Non ci sono job AI da esportare.");
        var fullPath = EnsureExtension(path, ".xlsx");
        EnsureDirectory(fullPath);
        var temp = fullPath + ".tmp";
        if (File.Exists(temp)) File.Delete(temp);

        await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            await WriteEntry(archive, "[Content_Types].xml", ContentTypes());
            await WriteEntry(archive, "_rels/.rels", RootRels());
            await WriteEntry(archive, "xl/workbook.xml", Workbook());
            await WriteEntry(archive, "xl/_rels/workbook.xml.rels", WorkbookRels());
            await WriteEntry(archive, "xl/worksheets/sheet1.xml", Worksheet(project));
        }
        File.Move(temp, fullPath, true);
        return new(true, $"Prompt pack XLSX esportato: {Path.GetFileName(fullPath)}");
    }

    private static string Worksheet(PreviewProject project)
    {
        XNamespace x = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var data = new XElement(x + "sheetData");
        var rows = new List<string[]>
        {
            new[] { "Codice", "Tipo", "Titolo", "Richiesta", "Prompt", "Stato" }
        };
        rows.AddRange(project.AiProductionJobs.OrderBy(j => j.Code, StringComparer.OrdinalIgnoreCase)
            .Select(j => new[] { j.Code, AiProductionService.DisplayType(j.OutputType), j.Title, j.Request, j.Prompt, AiProductionService.DisplayStatus(j.Status) }));
        for (var r = 0; r < rows.Count; r++)
        {
            var row = new XElement(x + "row", new XAttribute("r", r + 1));
            for (var c = 0; c < rows[r].Length; c++)
            {
                var cell = new XElement(x + "c",
                    new XAttribute("r", CellRef(c, r + 1)),
                    new XAttribute("t", "inlineStr"),
                    new XElement(x + "is", new XElement(x + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), rows[r][c] ?? string.Empty)));
                row.Add(cell);
            }
            data.Add(row);
        }
        return Xml(new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), new XElement(x + "worksheet", data)));
    }

    private static string ContentTypes()
    {
        XNamespace x = "http://schemas.openxmlformats.org/package/2006/content-types";
        return Xml(new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(x + "Types",
                new XElement(x + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
                new XElement(x + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")),
                new XElement(x + "Override", new XAttribute("PartName", "/xl/workbook.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")),
                new XElement(x + "Override", new XAttribute("PartName", "/xl/worksheets/sheet1.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")))));
    }

    private static string RootRels()
    {
        XNamespace x = "http://schemas.openxmlformats.org/package/2006/relationships";
        return Xml(new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), new XElement(x + "Relationships",
            new XElement(x + "Relationship", new XAttribute("Id", "rId1"), new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"), new XAttribute("Target", "xl/workbook.xml")))));
    }

    private static string Workbook()
    {
        XNamespace x = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace r = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        return Xml(new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), new XElement(x + "workbook", new XAttribute(XNamespace.Xmlns + "r", r),
            new XElement(x + "sheets", new XElement(x + "sheet", new XAttribute("name", "Prompt AI"), new XAttribute("sheetId", "1"), new XAttribute(r + "id", "rId1"))))));
    }

    private static string WorkbookRels()
    {
        XNamespace x = "http://schemas.openxmlformats.org/package/2006/relationships";
        return Xml(new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), new XElement(x + "Relationships",
            new XElement(x + "Relationship", new XAttribute("Id", "rId1"), new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"), new XAttribute("Target", "worksheets/sheet1.xml")))));
    }

    private static async Task WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        await writer.WriteAsync(content);
    }

    private static void AppendCsv(StringBuilder b, params string[] values)
    {
        for (var i = 0; i < values.Length; i++)
        {
            if (i > 0) b.Append(';');
            b.Append('"').Append((values[i] ?? string.Empty).Replace("\"", "\"\"", StringComparison.Ordinal)).Append('"');
        }
        b.AppendLine();
    }

    private static string CellRef(int column, int row)
    {
        var n = column + 1;
        var s = string.Empty;
        while (n > 0)
        {
            n--;
            s = (char)('A' + n % 26) + s;
            n /= 26;
        }
        return s + row;
    }

    private static string SafeBaseName(PreviewProject project)
    {
        var name = string.IsNullOrWhiteSpace(project.EditionMetadata?.Title) ? project.Name : project.EditionMetadata.Title;
        var invalid = Path.GetInvalidFileNameChars();
        var safe = string.Concat((name ?? "progetto").Select(ch => invalid.Contains(ch) ? '_' : ch)).Trim();
        return string.IsNullOrWhiteSpace(safe) ? "progetto" : safe;
    }

    private static string EnsureExtension(string path, string extension) =>
        path.EndsWith(extension, StringComparison.OrdinalIgnoreCase) ? path : path + extension;

    private static void EnsureDirectory(string path)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
    }

    private static string Xml(XDocument document) => document.ToString(SaveOptions.DisableFormatting);
}

internal readonly record struct AiProductionActionResult(bool Success, string Message);
