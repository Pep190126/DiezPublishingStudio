using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace DiezPublishingStudio;

internal static class AiImageBatchService
{
    public const string ProviderOpenAi = "ChatGPT / OpenAI";
    public const string ProviderGemini = "Gemini";
    public const string ProviderOther = "Altra AI";

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp"
    };

    private static readonly Regex ImageCodeRegex = new(@"IMG-\d+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static IReadOnlyList<AiProductionJob> CreateImageSeries(PreviewProject project, int count, string theme, string titlePrefix)
    {
        count = Math.Clamp(count, 1, 500);
        theme = (theme ?? string.Empty).Trim();
        titlePrefix = string.IsNullOrWhiteSpace(titlePrefix) ? "Immagine" : titlePrefix.Trim();
        var created = new List<AiProductionJob>(count);
        for (var i = 1; i <= count; i++)
        {
            var title = $"{titlePrefix} {i:D3}";
            var request = new StringBuilder()
                .AppendLine(theme)
                .AppendLine()
                .AppendLine($"Questa è l'immagine {i} di {count} della serie.")
                .AppendLine("Crea un risultato distinto dagli altri elementi della serie, senza cambiare il tema comune.")
                .AppendLine("Non aggiungere numeri, ID o nomi file dentro l'immagine, salvo richiesta esplicita del tema.")
                .ToString().Trim();
            created.Add(AiProductionService.CreateJob(project, AiProductionService.TypeImage, title, request));
        }
        return created;
    }

    public static IReadOnlyList<AiProductionJob> SelectImageJobs(PreviewProject project, bool onlyMissingOrToRedo)
    {
        var jobs = project.AiProductionJobs
            .Where(j => string.Equals(j.OutputType, AiProductionService.TypeImage, StringComparison.OrdinalIgnoreCase))
            .Where(j => !string.Equals(j.Status, AiProductionService.StatusRejected, StringComparison.Ordinal))
            .OrderBy(j => CodeNumber(j.Code))
            .ThenBy(j => j.Code, StringComparer.OrdinalIgnoreCase);

        if (onlyMissingOrToRedo)
            jobs = jobs.Where(j => !j.ResultMaterialId.HasValue || string.Equals(j.Status, AiProductionService.StatusNeedsRevision, StringComparison.Ordinal))
                .OrderBy(j => CodeNumber(j.Code))
                .ThenBy(j => j.Code, StringComparer.OrdinalIgnoreCase);

        return jobs.ToList();
    }

    public static string SuggestedPackName(PreviewProject project, bool correction) =>
        $"{SafeBaseName(project)}-{(correction ? "immagini-mancanti" : "pacchetto-immagini-ai")}.xlsx";

    public static string SuggestedApprovedZipName(PreviewProject project) =>
        $"{SafeBaseName(project)}-immagini-approvate.zip";

    public static string ChatInstruction =>
        "Leggi il file XLSX allegato e segui il foglio ISTRUZIONI. Genera le immagini indicate nel foglio IMMAGINI mantenendo esattamente gli ID e i nomi file richiesti. Se non riesci a completarle tutte, restituisci quelle completate senza rinumerarle.";

    public static async Task<AiProductionActionResult> ExportPackXlsxAsync(
        PreviewProject project,
        string path,
        string provider,
        bool preferMostAdvancedModel,
        bool onlyMissingOrToRedo)
    {
        var jobs = SelectImageJobs(project, onlyMissingOrToRedo);
        if (jobs.Count == 0)
            return new(false, onlyMissingOrToRedo
                ? "Non ci sono immagini mancanti o da rifare da mettere nel pacchetto."
                : "Non ci sono immagini preparate da mettere nel pacchetto.");

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
            await WriteEntry(archive, "xl/worksheets/sheet1.xml", InstructionsSheet(provider, preferMostAdvancedModel, jobs.Count));
            await WriteEntry(archive, "xl/worksheets/sheet2.xml", ImagesSheet(project, jobs));
        }
        File.Move(temp, fullPath, true);
        return new(true, $"Pacchetto per l'AI creato con {jobs.Count} immagini: {Path.GetFileName(fullPath)}");
    }

    public static async Task<AiImageZipImportResult> ImportResultZipAsync(PreviewProject project, string projectPath, string zipPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
            return new(false, 0, 0, 0, 0, 0, "Salva prima il progetto .diez.");
        if (string.IsNullOrWhiteSpace(zipPath) || !File.Exists(zipPath))
            return new(false, 0, 0, 0, 0, 0, "Lo ZIP selezionato non esiste.");

        var imageJobs = project.AiProductionJobs
            .Where(j => string.Equals(j.OutputType, AiProductionService.TypeImage, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(j => j.Code, StringComparer.OrdinalIgnoreCase);
        if (imageJobs.Count == 0)
            return new(false, 0, 0, 0, 0, 0, "Il progetto non contiene ancora immagini con ID IMG-### da collegare.");

        var tempRoot = Path.Combine(Path.GetTempPath(), "DiezImageZip-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var linked = 0;
        var unknown = 0;
        var duplicate = 0;
        var ignored = 0;
        try
        {
            using var archive = ZipFile.OpenRead(zipPath);
            var candidates = archive.Entries
                .Where(e => !string.IsNullOrWhiteSpace(e.Name) && ImageExtensions.Contains(Path.GetExtension(e.Name)))
                .Select(e => (Entry: e, Code: ExtractCode(e.Name)))
                .ToList();

            ignored += candidates.Count(x => string.IsNullOrWhiteSpace(x.Code));
            var withCode = candidates.Where(x => !string.IsNullOrWhiteSpace(x.Code)).ToList();
            var groups = withCode.GroupBy(x => x.Code!, StringComparer.OrdinalIgnoreCase).ToList();

            foreach (var group in groups)
            {
                if (group.Count() != 1)
                {
                    duplicate++;
                    continue;
                }
                if (!imageJobs.TryGetValue(group.Key, out var job))
                {
                    unknown++;
                    continue;
                }

                var entry = group.Single().Entry;
                var extension = Path.GetExtension(entry.Name).ToLowerInvariant();
                var localPath = Path.Combine(tempRoot, group.Key.ToUpperInvariant() + extension);
                await using (var source = entry.Open())
                await using (var destination = File.Create(localPath))
                    await source.CopyToAsync(destination);

                var material = await MaterialImporter.ImportAsync(localPath);
                var existing = project.Materials.FirstOrDefault(m => string.Equals(m.Sha256, material.Sha256, StringComparison.OrdinalIgnoreCase));
                if (existing is null)
                {
                    material.FileName = group.Key.ToUpperInvariant() + extension;
                    material.Summary = $"Risultato AI {group.Key.ToUpperInvariant()} · {material.Summary}";
                    material.Preview = $"Risultato importato automaticamente dallo ZIP e associato a {group.Key.ToUpperInvariant()}.\n\n{material.Preview}";
                    project.Materials.Add(material);
                    existing = material;
                }

                job.ResultMaterialId = existing.MaterialId;
                job.Status = AiProductionService.StatusToReview;
                job.UpdatedAtLocal = DateTimeOffset.Now.ToString("O");
                linked++;
            }

            if (linked > 0)
                await ProjectFileStore.SaveAsync(projectPath, project);

            var missing = imageJobs.Values.Count(j => !j.ResultMaterialId.HasValue || string.Equals(j.Status, AiProductionService.StatusNeedsRevision, StringComparison.Ordinal));
            var message = $"ZIP letto: {linked} immagini collegate automaticamente · {missing} ancora mancanti/da rifare";
            if (duplicate > 0) message += $" · {duplicate} ID duplicati nello ZIP non importati";
            if (unknown > 0) message += $" · {unknown} ID non presenti nel progetto";
            if (ignored > 0) message += $" · {ignored} file immagine senza ID IMG-### ignorati";
            return new(true, linked, missing, duplicate, unknown, ignored, message + ".");
        }
        catch (InvalidDataException ex)
        {
            return new(false, linked, 0, duplicate, unknown, ignored, "ZIP non valido: " + ex.Message);
        }
        finally
        {
            try { Directory.Delete(tempRoot, true); } catch { }
        }
    }

    public static async Task<AiProductionActionResult> ExportApprovedImagesZipAsync(PreviewProject project, string projectPath, string path)
    {
        var jobs = project.AiProductionJobs
            .Where(j => string.Equals(j.OutputType, AiProductionService.TypeImage, StringComparison.OrdinalIgnoreCase))
            .Where(j => string.Equals(j.Status, AiProductionService.StatusApproved, StringComparison.Ordinal))
            .Where(j => j.ResultMaterialId.HasValue)
            .OrderBy(j => CodeNumber(j.Code))
            .ThenBy(j => j.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (jobs.Count == 0) return new(false, "Non ci sono immagini approvate da esportare.");

        var fullPath = EnsureExtension(path, ".zip");
        EnsureDirectory(fullPath);
        var temp = fullPath + ".tmp";
        if (File.Exists(temp)) File.Delete(temp);

        await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            foreach (var job in jobs)
            {
                var material = project.Materials.FirstOrDefault(m => m.MaterialId == job.ResultMaterialId!.Value);
                if (material is null) continue;
                var bytes = await ProjectFileStore.ReadEmbeddedMaterialAsync(projectPath, material);
                if (bytes is null || bytes.Length == 0) continue;
                var extension = Path.GetExtension(material.FileName).ToLowerInvariant();
                if (!ImageExtensions.Contains(extension)) extension = ".png";
                var entry = archive.CreateEntry(job.Code.ToUpperInvariant() + extension, CompressionLevel.Optimal);
                await using var target = entry.Open();
                await target.WriteAsync(bytes);
            }
        }
        File.Move(temp, fullPath, true);
        return new(true, $"ZIP immagini approvate creato con sequenza stabile IMG-###: {Path.GetFileName(fullPath)}");
    }

    private static string InstructionsSheet(string provider, bool preferMostAdvancedModel, int count)
    {
        var model = ModelInstruction(provider, preferMostAdvancedModel);
        var rows = new List<string[]>
        {
            new[] { "PACCHETTO PER L'AI" },
            new[] { "Servizio scelto", string.IsNullOrWhiteSpace(provider) ? ProviderOther : provider },
            new[] { "Preferenza modello", preferMostAdvancedModel ? "Migliore disponibile" : "Consigliato / bilanciato" },
            new[] { "Modello / istruzione corrente", model },
            new[] { "Numero immagini in questo pacchetto", count.ToString() },
            new[] { "Cosa fare", "Leggi tutte le righe del foglio IMMAGINI e genera una sola immagine per ogni riga, seguendo il prompt della riga." },
            new[] { "ID", "Mantieni esattamente gli ID IMG-###. Non rinumerare mai le immagini, anche se ne salti alcune." },
            new[] { "Nome file", "Salva ogni risultato con il Nome file atteso indicato nella riga corrispondente." },
            new[] { "Se non puoi completare tutto", "Genera quante immagini puoi. Non sostituire, non comprimere la numerazione e non inventare file per le righe non completate." },
            new[] { "Consegna", "Se la piattaforma lo consente, restituisci un unico ZIP contenente le immagini completate. Lo ZIP può essere parziale: Diez ricomporrà il progetto usando gli ID." },
            new[] { "Frase pronta da usare nella chat", ChatInstruction }
        };
        return Worksheet(rows);
    }

    private static string ImagesSheet(PreviewProject project, IReadOnlyList<AiProductionJob> jobs)
    {
        var rows = new List<string[]>
        {
            new[] { "Ordine", "ID", "Titolo", "Prompt completo", "Stato in Diez", "Nome file atteso" }
        };
        for (var i = 0; i < jobs.Count; i++)
        {
            var job = jobs[i];
            rows.Add(new[]
            {
                (i + 1).ToString(),
                job.Code.ToUpperInvariant(),
                job.Title,
                job.Prompt,
                AiProductionService.DisplayStatus(job.Status),
                job.Code.ToUpperInvariant() + ".png"
            });
        }
        return Worksheet(rows);
    }

    private static string ModelInstruction(string provider, bool preferMostAdvancedModel)
    {
        if (string.Equals(provider, ProviderOpenAi, StringComparison.OrdinalIgnoreCase))
            return preferMostAdvancedModel
                ? "Usa GPT Image 2, se disponibile in questa esperienza; altrimenti usa il modello immagini OpenAI più avanzato disponibile."
                : "Usa il modello immagini OpenAI consigliato per un buon equilibrio tra qualità, velocità e disponibilità.";
        if (string.Equals(provider, ProviderGemini, StringComparison.OrdinalIgnoreCase))
            return preferMostAdvancedModel
                ? "Usa Nano Banana Pro (Gemini 3 Pro Image), se disponibile; altrimenti usa il modello immagini Gemini più avanzato disponibile."
                : "Usa Nano Banana 2 (Gemini 3.1 Flash Image), se disponibile; altrimenti usa il modello immagini Gemini consigliato.";
        return preferMostAdvancedModel
            ? "Usa il modello di generazione immagini più avanzato disponibile in questa piattaforma."
            : "Usa il modello immagini consigliato dalla piattaforma per questo lavoro.";
    }

    private static string? ExtractCode(string name)
    {
        var match = ImageCodeRegex.Match(Path.GetFileName(name));
        return match.Success ? match.Value.ToUpperInvariant() : null;
    }

    private static int CodeNumber(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return int.MaxValue;
        var dash = code.LastIndexOf('-');
        return dash >= 0 && int.TryParse(code[(dash + 1)..], out var number) ? number : int.MaxValue;
    }

    private static string Worksheet(IReadOnlyList<string[]> rows)
    {
        XNamespace x = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var data = new XElement(x + "sheetData");
        for (var r = 0; r < rows.Count; r++)
        {
            var row = new XElement(x + "row", new XAttribute("r", r + 1));
            for (var c = 0; c < rows[r].Length; c++)
                row.Add(new XElement(x + "c",
                    new XAttribute("r", CellRef(c, r + 1)),
                    new XAttribute("t", "inlineStr"),
                    new XElement(x + "is", new XElement(x + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), rows[r][c] ?? string.Empty))));
            data.Add(row);
        }
        return Xml(new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), new XElement(x + "worksheet", data)));
    }

    private static string ContentTypes()
    {
        XNamespace x = "http://schemas.openxmlformats.org/package/2006/content-types";
        return Xml(new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), new XElement(x + "Types",
            new XElement(x + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
            new XElement(x + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")),
            new XElement(x + "Override", new XAttribute("PartName", "/xl/workbook.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")),
            new XElement(x + "Override", new XAttribute("PartName", "/xl/worksheets/sheet1.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")),
            new XElement(x + "Override", new XAttribute("PartName", "/xl/worksheets/sheet2.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")))));
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
            new XElement(x + "sheets",
                new XElement(x + "sheet", new XAttribute("name", "ISTRUZIONI"), new XAttribute("sheetId", "1"), new XAttribute(r + "id", "rId1")),
                new XElement(x + "sheet", new XAttribute("name", "IMMAGINI"), new XAttribute("sheetId", "2"), new XAttribute(r + "id", "rId2"))))));
    }

    private static string WorkbookRels()
    {
        XNamespace x = "http://schemas.openxmlformats.org/package/2006/relationships";
        return Xml(new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), new XElement(x + "Relationships",
            new XElement(x + "Relationship", new XAttribute("Id", "rId1"), new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"), new XAttribute("Target", "worksheets/sheet1.xml")),
            new XElement(x + "Relationship", new XAttribute("Id", "rId2"), new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"), new XAttribute("Target", "worksheets/sheet2.xml")))));
    }

    private static async Task WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        await writer.WriteAsync(content);
    }

    private static string CellRef(int column, int row)
    {
        var n = column + 1;
        var value = string.Empty;
        while (n > 0)
        {
            n--;
            value = (char)('A' + n % 26) + value;
            n /= 26;
        }
        return value + row;
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

internal readonly record struct AiImageZipImportResult(
    bool Success,
    int Linked,
    int Missing,
    int DuplicateIds,
    int UnknownIds,
    int IgnoredWithoutId,
    string Message);