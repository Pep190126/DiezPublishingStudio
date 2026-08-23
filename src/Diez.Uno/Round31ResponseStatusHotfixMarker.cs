namespace DiezPublishingStudio.UnoSpike;

/// <summary>
/// Build marker for the Round 3.1 physical-test candidate.
/// The candidate includes the Response transport compatibility fix that normalizes provider
/// status CANDIDATE to canonical COMPLETE while preserving strict rejection of unknown statuses.
/// </summary>
internal static class Round31ResponseStatusHotfixMarker
{
    public const string Candidate = "ROUND3.1-RESPONSE-CANDIDATE-ALIAS-2026-08-23";
}
