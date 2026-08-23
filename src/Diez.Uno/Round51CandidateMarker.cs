namespace DiezPublishingStudio.UnoSpike;

/// <summary>
/// Build marker for the Round 5.1 physical candidate.
/// The functional fix lives in Diez.Core; this marker gives the Windows installer pipeline
/// an unambiguous committed Source SHA after the migration regression gate passed.
/// </summary>
internal static class Round51CandidateMarker
{
    public const string Candidate = "ROUND-5.1-LEGACY-PROMPT-MIGRATION";
}
