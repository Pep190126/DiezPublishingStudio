namespace DiezPublishingStudio;

public sealed record DiezAiProviderCapability(
    string Id,
    string DisplayName,
    bool SupportsText,
    bool SupportsImage,
    bool SupportsData,
    bool SupportsDirectApi);

/// <summary>
/// Public UI-neutral view of provider capabilities. Frontends must not present a direct API action
/// as operational unless the Core provider catalog explicitly declares it supported.
/// </summary>
public static class DiezAiTransportFrontendBridge
{
    public static IReadOnlyList<DiezAiProviderCapability> Providers() =>
        AiProviderCatalog.All.Select(ToDto).ToList();

    public static DiezAiProviderCapability Provider(string? displayName) =>
        ToDto(AiProviderCatalog.FindByDisplayName(displayName));

    private static DiezAiProviderCapability ToDto(AiProviderDescriptor provider) => new(
        provider.Id,
        provider.DisplayName,
        provider.SupportsText,
        provider.SupportsImage,
        provider.SupportsData,
        provider.SupportsDirectApi);
}
