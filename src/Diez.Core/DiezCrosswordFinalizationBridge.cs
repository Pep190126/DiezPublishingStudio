using System.Text;
using System.Text.Json;

namespace DiezPublishingStudio;

public sealed record DiezCrosswordFinalCheck(string Code, bool Passed, string Message);

public sealed record DiezCrosswordFinalizationState(
    bool Ready,
    int WordCount,
    int WordsWithDefinitions,
    int ApprovedWords,
    int MissingDefinitions,
    int MissingApprovals,
    IReadOnlyList<DiezCrosswordFinalCheck> Checks);

public sealed record DiezCrosswordFinalExportResult(bool Exported, string Message, string? OutputPath);

/// <summary>
/// Core-only handoff gate for Crossword. Working dictionaries/templates remain editable,
/// while the final Qxw handoff is emitted only when every canonical word has at least one
/// definition and one explicitly approved clue.
/// </summary>
public static class DiezCrosswordFinalizationBridge
{
    public static DiezCrosswordFinalizationState Readiness(string projectJson) => Readiness(Parse(projectJson));

    public static async Task<DiezCrosswordFinalExportResult> ExportFinalQxwAsync(string projectJson, string outputPath)
    {
        var project = Parse(projectJson);
        var state = Readiness(project);
        if (!state.Ready)
        {
            var failures = state.Checks.Where(check => !check.Passed).Take(3).Select(check => check.Message);
            return new DiezCrosswordFinalExportResult(false,
                "Handoff Qxw finale bloccato: " + string.Join(" ", failures), null);
        }
        if (string.IsNullOrWhiteSpace(outputPath))
            return new DiezCrosswordFinalExportResult(false, "Percorso di esportazione non valido.", null);

        var fullPath = EnsureExtension(Path.GetFullPath(outputPath), ".txt");
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        await CrosswordService.ExportQxwTextAsync(project, fullPath);
        return new DiezCrosswordFinalExportResult(true,
            $"Handoff Qxw finale esportato: {Path.GetFileName(fullPath)} · {state.WordCount} parole.", fullPath);
    }

    internal static DiezCrosswordFinalizationState Readiness(PreviewProject project)
    {
        var words = CrosswordService.Words(project);
        var rows = CrosswordService.DefinitionRows(project);
        var wordKeys = words.Select(word => CrosswordService.NormalizeGridWord(word.Name)).ToList();
        var unique = wordKeys.Where(key => key.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Count() == words.Count;

        static bool HasDefinition(CrosswordDefinitionRow row) =>
            !string.IsNullOrWhiteSpace(row.Definition1) ||
            !string.IsNullOrWhiteSpace(row.Definition2) ||
            !string.IsNullOrWhiteSpace(row.Definition3) ||
            !string.IsNullOrWhiteSpace(row.Definition4);

        static bool ApprovedMatchesCandidate(CrosswordDefinitionRow row)
        {
            var approved = NormalizeText(row.Approved);
            if (approved.Length == 0) return false;
            return new[] { row.Definition1, row.Definition2, row.Definition3, row.Definition4 }
                .Select(NormalizeText)
                .Any(candidate => candidate.Length > 0 && string.Equals(candidate, approved, StringComparison.OrdinalIgnoreCase));
        }

        var withDefinitions = rows.Count(HasDefinition);
        var approved = rows.Count(ApprovedMatchesCandidate);
        var missingDefinitions = rows.Count - withDefinitions;
        var missingApprovals = rows.Count - approved;
        var theme = CrosswordService.GetSetting(project, "Theme", string.Empty);
        var language = CrosswordService.GetSetting(project, "PrimaryLanguage", string.Empty);

        var checks = new List<DiezCrosswordFinalCheck>
        {
            new("WORDS_PRESENT", words.Count > 0,
                words.Count > 0 ? $"Vocabolario: {words.Count} parole canoniche." : "Il vocabolario Cruciverba è vuoto."),
            new("WORDS_UNIQUE", unique,
                unique ? "Le parole del vocabolario sono uniche dopo normalizzazione da griglia." : "Il vocabolario contiene parole equivalenti duplicate."),
            new("DEFINITIONS_COMPLETE", missingDefinitions == 0,
                missingDefinitions == 0 ? "Ogni parola ha almeno una definizione candidata." : $"{missingDefinitions} parole non hanno ancora alcuna definizione."),
            new("APPROVED_CLUES", missingApprovals == 0 && rows.Count > 0,
                missingApprovals == 0 && rows.Count > 0 ? "Ogni parola ha una definizione approvata." : $"{missingApprovals} parole non hanno una definizione approvata valida."),
            new("LANGUAGE_SET", !string.IsNullOrWhiteSpace(language),
                !string.IsNullOrWhiteSpace(language) ? $"Lingua: {language}." : "Manca la lingua principale del cruciverba."),
            new("THEME_SET", !string.IsNullOrWhiteSpace(theme),
                !string.IsNullOrWhiteSpace(theme) ? $"Tema: {theme}." : "Manca il tema / criterio editoriale del cruciverba.")
        };

        return new DiezCrosswordFinalizationState(
            checks.All(check => check.Passed),
            words.Count,
            withDefinitions,
            approved,
            missingDefinitions,
            missingApprovals,
            checks);
    }

    public static string BuildApprovedCluesTsv(string projectJson)
    {
        var project = Parse(projectJson);
        var state = Readiness(project);
        if (!state.Ready) return string.Empty;
        var builder = new StringBuilder();
        builder.AppendLine("PAROLA\tDEFINIZIONE APPROVATA");
        foreach (var row in CrosswordService.DefinitionRows(project).OrderBy(row => row.Word, StringComparer.OrdinalIgnoreCase))
            builder.Append(row.Word).Append('\t').AppendLine(row.Approved.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' '));
        return builder.ToString();
    }

    private static string NormalizeText(string? value) =>
        string.Join(' ', (value ?? string.Empty).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static PreviewProject Parse(string json)
    {
        var project = JsonSerializer.Deserialize<PreviewProject>(json, JsonOptions)
            ?? throw new InvalidDataException("Il progetto Diez non può essere letto dal finalizzatore Cruciverba.");
        project.EditionMetadata ??= new EditionMetadata();
        project.AiProduction ??= new AiProductionSettings();
        project.AiProductionJobs ??= [];
        project.Materials ??= [];
        project.ContentNodes ??= [];
        project.IllustrationPlacements ??= [];
        project.Entities ??= [];
        project.Relations ??= [];
        project.BibleEntries ??= [];
        project.ConsistencyFacts ??= [];
        project.ConsistencyIssues ??= [];
        project.ConsistencyResolutions ??= [];
        project.RevisionCandidates ??= [];
        return project;
    }

    private static string EnsureExtension(string path, string extension) =>
        path.EndsWith(extension, StringComparison.OrdinalIgnoreCase) ? path : path + extension;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
}
