using System.Buffers.Binary;

namespace DiezPublishingStudio;

internal readonly record struct ImageCollectionCheck(string Code, string Message, bool NeedsAction);

internal static class ImageCollectionWorkspaceService
{
    private const string RulesEntityKind = "DiezImageConsistencyProfile";
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp"
    };

    public static List<AiProductionJob> Jobs(PreviewProject project) => project.AiProductionJobs
        .Where(j => string.Equals(j.OutputType, AiProductionService.TypeImage, StringComparison.OrdinalIgnoreCase))
        .OrderBy(j => Number(j.Code))
        .ThenBy(j => j.Code, StringComparer.OrdinalIgnoreCase)
        .ToList();

    public static string GetConsistencyRules(PreviewProject project) =>
        project.Entities.FirstOrDefault(e => string.Equals(e.Kind, RulesEntityKind, StringComparison.OrdinalIgnoreCase))?.Notes ?? string.Empty;

    public static void SetConsistencyRules(PreviewProject project, string? rules)
    {
        var entity = project.Entities.FirstOrDefault(e => string.Equals(e.Kind, RulesEntityKind, StringComparison.OrdinalIgnoreCase));
        if (entity is null)
        {
            entity = new GraphEntity
            {
                Kind = RulesEntityKind,
                Name = "Regole visive della raccolta",
                IsCandidate = false
            };
            project.Entities.Add(entity);
        }
        entity.Notes = (rules ?? string.Empty).Trim();
        entity.IsCandidate = false;
    }

    public static async Task<List<ImageCollectionCheck>> CheckAsync(PreviewProject project, string projectPath)
    {
        var jobs = Jobs(project);
        var checks = new List<ImageCollectionCheck>();
        if (jobs.Count == 0)
        {
            checks.Add(new("Raccolta", "Non ci sono ancora immagini nella raccolta.", true));
            return checks;
        }

        if (string.IsNullOrWhiteSpace(GetConsistencyRules(project)))
            checks.Add(new("Raccolta", "Regole di coerenza non ancora definite: scrivile qui per mantenere uniforme tutta la serie.", false));

        var materialByCode = new Dictionary<string, MaterialEntry>(StringComparer.OrdinalIgnoreCase);
        var dimensions = new Dictionary<string, (int Width, int Height)> (StringComparer.OrdinalIgnoreCase);
        foreach (var job in jobs)
        {
            if (!job.ResultMaterialId.HasValue)
            {
                checks.Add(new(job.Code, $"{job.Code} → immagine mancante.", true));
                continue;
            }

            var material = project.Materials.FirstOrDefault(m => m.MaterialId == job.ResultMaterialId.Value);
            if (material is null)
            {
                checks.Add(new(job.Code, $"{job.Code} → il file collegato non è più disponibile nel progetto.", true));
                continue;
            }
            materialByCode[job.Code] = material;

            var extension = Path.GetExtension(material.FileName);
            if (!ImageExtensions.Contains(extension))
                checks.Add(new(job.Code, $"{job.Code} → formato immagine non riconosciuto: {material.FileName}.", true));

            if (string.Equals(job.Status, AiProductionService.StatusNeedsRevision, StringComparison.Ordinal))
                checks.Add(new(job.Code, $"{job.Code} → già segnato da rifare.", true));
            else if (string.Equals(job.Status, AiProductionService.StatusRejected, StringComparison.Ordinal))
                checks.Add(new(job.Code, $"{job.Code} → immagine scartata: serve una sostituzione.", true));

            var bytes = await ProjectFileStore.ReadEmbeddedMaterialAsync(projectPath, material);
            if (bytes is null || bytes.Length == 0)
            {
                checks.Add(new(job.Code, $"{job.Code} → originale non leggibile dal progetto .diez.", true));
                continue;
            }
            var size = ReadDimensions(bytes, extension);
            if (size.Width > 0 && size.Height > 0) dimensions[job.Code] = size;
        }

        foreach (var duplicate in materialByCode
                     .Where(kv => !string.IsNullOrWhiteSpace(kv.Value.Sha256))
                     .GroupBy(kv => kv.Value.Sha256, StringComparer.OrdinalIgnoreCase)
                     .Where(g => g.Count() > 1))
        {
            var codes = duplicate.Select(kv => kv.Key).OrderBy(Number).ToList();
            var message = $"{string.Join(" e ", codes)} → usano esattamente lo stesso file immagine. Controlla che non sia un doppione involontario.";
            foreach (var code in codes) checks.Add(new(code, message, true));
        }

        var commonExtension = materialByCode.Values
            .Select(m => Path.GetExtension(m.FileName).ToLowerInvariant())
            .Where(ImageExtensions.Contains)
            .GroupBy(x => x)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key)
            .Select(g => g.Key)
            .FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(commonExtension))
        {
            foreach (var pair in materialByCode)
            {
                var extension = Path.GetExtension(pair.Value.FileName).ToLowerInvariant();
                if (ImageExtensions.Contains(extension) && !string.Equals(extension, commonExtension, StringComparison.OrdinalIgnoreCase))
                    checks.Add(new(pair.Key, $"{pair.Key} → formato {extension.TrimStart('.').ToUpperInvariant()} mentre la maggior parte della raccolta è {commonExtension.TrimStart('.').ToUpperInvariant()}. Da verificare.", false));
            }
        }

        var commonSize = dimensions.Values
            .GroupBy(s => s)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .FirstOrDefault();
        if (commonSize.Width > 0 && commonSize.Height > 0 && dimensions.Values.Count(s => s == commonSize) > 1)
        {
            foreach (var pair in dimensions)
            {
                if (pair.Value != commonSize)
                    checks.Add(new(pair.Key,
                        $"{pair.Key} → dimensioni {pair.Value.Width}×{pair.Value.Height}; la misura più comune è {commonSize.Width}×{commonSize.Height}. Da verificare prima dell'impaginazione.",
                        false));
            }
        }

        return checks
            .GroupBy(c => (c.Code, c.Message))
            .Select(g => g.First())
            .OrderBy(c => Number(c.Code))
            .ThenBy(c => c.Message, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string BuildCorrectionInstructions(PreviewProject project, AiProductionJob job)
    {
        var rules = GetConsistencyRules(project).Trim();
        var description = ImageCollectionDescriptionService.GetDescription(job).Trim();
        var lines = new List<string>
        {
            $"IMMAGINE DA CORREGGERE: {job.Code}",
            string.IsNullOrWhiteSpace(job.Title) ? string.Empty : $"Titolo: {job.Title}",
            string.IsNullOrWhiteSpace(job.Request) ? string.Empty : $"Richiesta originale: {job.Request}",
            string.IsNullOrWhiteSpace(description) ? string.Empty : $"Descrizione associata:\n{description}",
            string.IsNullOrWhiteSpace(rules) ? string.Empty : $"Regole di coerenza della raccolta:\n{rules}",
            "Correggi o rigenera soltanto questa immagine mantenendo il suo identificativo. Non rinumerare e non modificare le altre immagini della raccolta."
        };
        return string.Join("\n\n", lines.Where(l => !string.IsNullOrWhiteSpace(l)));
    }

    private static int Number(string? code)
    {
        var digits = new string((code ?? string.Empty).Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var value) ? value : int.MaxValue;
    }

    private static (int Width, int Height) ReadDimensions(byte[] bytes, string extension)
    {
        try
        {
            if (extension.Equals(".png", StringComparison.OrdinalIgnoreCase) && bytes.Length >= 24)
                return (BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4)), BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4)));
            if (extension.Equals(".gif", StringComparison.OrdinalIgnoreCase) && bytes.Length >= 10)
                return (BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(6, 2)), BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(8, 2)));
            if (extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase) && bytes.Length >= 26)
                return (Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(18, 4))), Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(22, 4))));
            if (extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
                return ReadJpegDimensions(bytes);
        }
        catch { }
        return (0, 0);
    }

    private static (int Width, int Height) ReadJpegDimensions(byte[] bytes)
    {
        var offset = 2;
        while (offset + 8 < bytes.Length)
        {
            if (bytes[offset] != 0xFF) { offset++; continue; }
            while (offset < bytes.Length && bytes[offset] == 0xFF) offset++;
            if (offset >= bytes.Length) break;
            var marker = bytes[offset++];
            if (marker is 0xD8 or 0xD9) continue;
            if (offset + 2 > bytes.Length) break;
            var length = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset, 2));
            if (length < 2 || offset + length > bytes.Length) break;
            if (marker is 0xC0 or 0xC1 or 0xC2 or 0xC3 or 0xC5 or 0xC6 or 0xC7 or 0xC9 or 0xCA or 0xCB or 0xCD or 0xCE or 0xCF)
            {
                if (offset + 7 <= bytes.Length)
                {
                    var height = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset + 3, 2));
                    var width = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset + 5, 2));
                    return (width, height);
                }
            }
            offset += length;
        }
        return (0, 0);
    }
}
