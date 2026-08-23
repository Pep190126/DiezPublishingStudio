using System.Text.RegularExpressions;

namespace DiezPublishingStudio;

/// <summary>
/// Final semantic boundary before visual prompt compilation.
/// Diez may only hand the renderer a concrete Work Unit. Aggregate series planning belongs to
/// canonical editorial state (subjects/scenes) and must never be delegated to the image renderer.
/// </summary>
internal static class VisualSemanticResolutionGuard
{
    private static readonly Regex AggregateSubject = new(
        @"^\s*(?:\d+|one|two|three|four|five|six|seven|eight|nine|ten|uno|una|due|tre|quattro|cinque|sei|sette|otto|nove|dieci)\s+(?:soggett[oi]|subjects?|personagg(?:io|i)|characters?)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex LegacyImageAggregateSubject = new(
        @"^\s*(?:\d+|one|two|three|four|five|six|seven|eight|nine|ten|uno|una|due|tre|quattro|cinque|sei|sette|otto|nove|dieci)\s+(?:images?|immagin[ei])\b.*\b(?:soggett[oi]|subjects?|personagg(?:io|i)|characters?)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex AggregateScene = new(
        @"\b(?:\d+|one|two|three|four|five|six|seven|eight|nine|ten|uno|una|due|tre|quattro|cinque|sei|sette|otto|nove|dieci)\s+(?:scen(?:a|e|ari|ario)|scenarios?|settings?|backgrounds?)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex PlaceholderSubject = new(
        @"^(?:(?:soggetto|subject|personaggio|character)\s*\d+|one concrete, specific, immediately recognizable subject.*|the requested focal subject)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static void EnsureResolvedForPosition(
        PreviewProject project,
        PromptEngineeringRequest request,
        int position,
        string? itemSubject,
        MultiSubjectDefinition? focal,
        IReadOnlyList<MultiSubjectDefinition> participants)
    {
        var focalName = (focal?.Name ?? string.Empty).Trim();
        var itemName = (itemSubject ?? string.Empty).Trim();
        var seriesSubject = (request.Subject ?? string.Empty).Trim();

        if (IsConcrete(focalName) || IsConcrete(itemName))
        {
            EnsureScenePlanResolved(project, request);
            return;
        }

        // A scene participant may become the focal subject only when it is concrete.
        if (participants.Count > 0 && participants.All(p => IsConcrete(p.Name)))
        {
            EnsureScenePlanResolved(project, request);
            return;
        }

        if (LooksAggregateSubject(seriesSubject))
        {
            throw new InvalidOperationException(
                $"Piano soggetti non risolto in Diez per la posizione {Math.Max(1, position)}. " +
                $"'{seriesSubject}' descrive una serie, non un soggetto atomico. " +
                "Prima del Prompt Pack Diez deve avere un soggetto concreto per ogni immagine. " +
                "Usa Scene + soggetti strutturati oppure una futura proposta del planner AI interno; il renderer immagini non può scegliere i soggetti al posto di Diez.");
        }

        EnsureScenePlanResolved(project, request);
    }

    public static bool LooksAggregateSubject(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0) return false;
        return AggregateSubject.IsMatch(text) || LegacyImageAggregateSubject.IsMatch(text);
    }

    public static bool IsConcrete(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        return text.Length > 0 && !PlaceholderSubject.IsMatch(text) && !LooksAggregateSubject(text);
    }

    private static void EnsureScenePlanResolved(PreviewProject project, PromptEngineeringRequest request)
    {
        var combined = string.Join("\n", new[] { request.MustDo, request.MustNotDo }
            .Where(x => !string.IsNullOrWhiteSpace(x)));
        if (!AggregateScene.IsMatch(combined)) return;

        var scenes = StructuredSceneProfileService.Load(project);
        var active = StructuredSceneProfileService.ActiveScenes(scenes);
        if (scenes.Enabled && active.Count > 0 && active.All(IsConcreteScene)) return;

        throw new InvalidOperationException(
            "Piano Scene non risolto in Diez. Un vincolo dell'utente richiede più scene/scenari distinti, " +
            "ma non esiste ancora una scena concreta per ogni posizione. Definisci le Scene prima del Prompt Pack: " +
            "la scelta dello scenario non deve essere delegata al renderer immagini.");
    }

    private static bool IsConcreteScene(StructuredSceneDefinition scene)
    {
        var defaultIt = $"Scena {scene.Number}";
        var defaultEn = $"Scene {scene.Number}";
        var name = (scene.Name ?? string.Empty).Trim();
        var description = (scene.Description ?? string.Empty).Trim();
        if (description.Length > 0) return true;
        return name.Length > 0 &&
               !string.Equals(name, defaultIt, StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(name, defaultEn, StringComparison.OrdinalIgnoreCase);
    }
}
