namespace DiezPublishingStudio.UnoSpike;

/// <summary>
/// Build marker for the Round 3 physical-test candidate.
/// No runtime behavior: the source commit exists only to make the reviewed Round 3 UI state
/// independently traceable through the Windows installer pipeline.
/// </summary>
internal static class Round3CandidateMarker
{
    public const string Candidate = "ROUND3-VISUAL-PREPROMPT-2026-08-20";
}
