namespace DiezPublishingStudio.UnoSpike;

/// <summary>
/// Stable feature metadata for the local visual theme library introduced in Round 5.6.
/// Kept as product metadata rather than a CI marker so installed builds can identify the UX contract.
/// </summary>
internal static class VisualThemeFeatureInfo
{
    public const string ContractVersion = "5.6.1";
    public const string SubjectProposalMode = "LOCAL_THEME_LIBRARY";
    public const bool ManualPlannerVisible = false;
    public const bool PromptReadinessGateImplemented = true;
    public const bool CustomThemeApiExpansionImplemented = false;
}
