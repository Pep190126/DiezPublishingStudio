namespace DiezPublishingStudio;

internal sealed record AiProviderDescriptor(
    string Id,
    string DisplayName,
    bool SupportsText,
    bool SupportsImage,
    bool SupportsData,
    bool SupportsDirectApi,
    string AdvancedImageInstruction,
    string BalancedImageInstruction);

internal static class AiProviderCatalog
{
    public const string OpenAiId = "openai";
    public const string GeminiId = "gemini";
    public const string OtherId = "other";

    private static readonly List<AiProviderDescriptor> Providers =
    [
        new(
            OpenAiId,
            "ChatGPT / OpenAI",
            SupportsText: true,
            SupportsImage: true,
            SupportsData: true,
            SupportsDirectApi: false,
            AdvancedImageInstruction: "Usa il modello di generazione immagini OpenAI più avanzato disponibile in questa piattaforma.",
            BalancedImageInstruction: "Usa il modello immagini OpenAI consigliato per un buon equilibrio tra qualità, velocità e disponibilità."),
        new(
            GeminiId,
            "Gemini",
            SupportsText: true,
            SupportsImage: true,
            SupportsData: true,
            SupportsDirectApi: false,
            AdvancedImageInstruction: "Usa il modello di generazione immagini Gemini più avanzato disponibile in questa piattaforma.",
            BalancedImageInstruction: "Usa il modello immagini Gemini consigliato per un buon equilibrio tra qualità, velocità e disponibilità."),
        new(
            OtherId,
            "Altra / nuova AI",
            SupportsText: true,
            SupportsImage: true,
            SupportsData: true,
            SupportsDirectApi: false,
            AdvancedImageInstruction: "Usa il modello di generazione immagini più avanzato disponibile nella piattaforma scelta.",
            BalancedImageInstruction: "Usa il modello immagini consigliato dalla piattaforma scelta per questo lavoro.")
    ];

    public static IReadOnlyList<AiProviderDescriptor> All => Providers;

    public static IReadOnlyList<AiProviderDescriptor> ForOutputType(string? outputType)
    {
        var type = (outputType ?? string.Empty).Trim();
        return Providers.Where(provider => type switch
        {
            AiProductionService.TypeImage => provider.SupportsImage,
            AiProductionService.TypeData => provider.SupportsData,
            _ => provider.SupportsText
        }).ToList();
    }

    public static AiProviderDescriptor FindById(string? id) =>
        Providers.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase))
        ?? Providers.First(p => p.Id == OtherId);

    public static AiProviderDescriptor FindByDisplayName(string? displayName) =>
        Providers.FirstOrDefault(p => string.Equals(p.DisplayName, displayName, StringComparison.OrdinalIgnoreCase))
        ?? Providers.First(p => p.Id == OtherId);

    public static void Register(AiProviderDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (string.IsNullOrWhiteSpace(descriptor.Id) || string.IsNullOrWhiteSpace(descriptor.DisplayName))
            throw new ArgumentException("Il provider AI deve avere ID e nome leggibile.", nameof(descriptor));

        var existing = Providers.FindIndex(p => string.Equals(p.Id, descriptor.Id, StringComparison.OrdinalIgnoreCase));
        if (existing >= 0) Providers[existing] = descriptor;
        else Providers.Insert(Math.Max(0, Providers.Count - 1), descriptor);
    }

    public static IReadOnlyList<string> DisplayNamesFor(string? outputType) =>
        ForOutputType(outputType).Select(p => p.DisplayName).ToList();

    public static string ImageModelInstruction(string? providerDisplayName, bool preferMostAdvancedModel)
    {
        var provider = FindByDisplayName(providerDisplayName);
        return preferMostAdvancedModel ? provider.AdvancedImageInstruction : provider.BalancedImageInstruction;
    }
}
