namespace DiezPublishingStudio.UnoSpike;

/// <summary>
/// Trace marker for the Round 5 physical-test candidate.
/// Runtime-neutral: identifies the source state that requires native image generation
/// and quality-first Coloring prompts instead of coded/vector fallback artwork.
/// </summary>
internal static class Round5CandidateMarker
{
    public const string Candidate = "ROUND5-NATIVE-IMAGE-QUALITY-2026-08-23";
}
