using DiezPublishingStudio;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

var selectedStyle = "Kawaii";
var attemptedSoftFailures = new[]
{
    VisionHardGatePolicy.Enforce(VisionHardGatePolicy.SubjectMatch, VisionHardGatePolicy.Fail, VisionHardGatePolicy.Soft, selectedStyle),
    VisionHardGatePolicy.Enforce(VisionHardGatePolicy.SceneParticipantsMatch, VisionHardGatePolicy.Fail, VisionHardGatePolicy.Soft, selectedStyle),
    VisionHardGatePolicy.Enforce(VisionHardGatePolicy.SingleComposition, VisionHardGatePolicy.Fail, VisionHardGatePolicy.Soft, selectedStyle),
    VisionHardGatePolicy.Enforce(VisionHardGatePolicy.StyleMatch, VisionHardGatePolicy.Fail, VisionHardGatePolicy.Soft, selectedStyle),
    VisionHardGatePolicy.Enforce(VisionHardGatePolicy.BoldEasyMatch, VisionHardGatePolicy.Fail, VisionHardGatePolicy.Soft, selectedStyle),
    VisionHardGatePolicy.Enforce(VisionHardGatePolicy.CozyMatch, VisionHardGatePolicy.Fail, VisionHardGatePolicy.Soft, selectedStyle),
    VisionHardGatePolicy.Enforce(VisionHardGatePolicy.LineWeightMatch, VisionHardGatePolicy.Fail, VisionHardGatePolicy.Soft, selectedStyle)
};

Require(attemptedSoftFailures.All(x => x.Severity == VisionHardGatePolicy.Hard),
    "A provider/user payload must not be able to downgrade semantic HARD checks to SOFT.");
Require(attemptedSoftFailures.All(x => x.BlocksApproval),
    "Every failed semantic HARD check must block approval.");
var blocked = VisionHardGatePolicy.Aggregate(attemptedSoftFailures);
Require(blocked.OverallStatus == VisionHardGatePolicy.Fail && blocked.BlocksApproval,
    "Any semantic HARD failure must force overall FAIL and block approval.");
Require(blocked.HardFailureCount == attemptedSoftFailures.Length,
    "All attempted downgraded semantic failures must remain counted as HARD failures.");

// Soft quality judgments remain soft after semantic compliance; they may request review but never
// silently become approval-blocking HARD criteria.
var softQuality = new[]
{
    VisionHardGatePolicy.Enforce("style_quality", VisionHardGatePolicy.Fail, VisionHardGatePolicy.Soft, selectedStyle),
    VisionHardGatePolicy.Enforce("composition_readability", VisionHardGatePolicy.Review, VisionHardGatePolicy.Soft, selectedStyle)
};
Require(softQuality.All(x => x.Severity == VisionHardGatePolicy.Soft && !x.BlocksApproval),
    "Aesthetic/readability judgments must remain SOFT when the semantic gates themselves passed.");
var review = VisionHardGatePolicy.Aggregate(softQuality);
Require(review.OverallStatus == VisionHardGatePolicy.Review && !review.BlocksApproval,
    "Soft failure/review must produce REVIEW rather than a false HARD block.");

// style_match is conditional: it is HARD only when Diez has a selected style to enforce.
var noSelectedStyle = VisionHardGatePolicy.Enforce(
    VisionHardGatePolicy.StyleMatch,
    VisionHardGatePolicy.Fail,
    VisionHardGatePolicy.Soft,
    selectedStyle: string.Empty);
Require(noSelectedStyle.Severity == VisionHardGatePolicy.Soft && !noSelectedStyle.BlocksApproval,
    "style_match must not invent a HARD style when no explicit style exists.");

var naScene = VisionHardGatePolicy.Enforce(
    VisionHardGatePolicy.SceneParticipantsMatch,
    VisionHardGatePolicy.NotApplicable,
    VisionHardGatePolicy.Soft,
    selectedStyle);
Require(naScene.Severity == VisionHardGatePolicy.Hard && !naScene.BlocksApproval,
    "A non-applicable structured-scene gate may remain HARD policy without blocking approval.");

var passSet = new[]
{
    VisionHardGatePolicy.Enforce(VisionHardGatePolicy.SubjectMatch, VisionHardGatePolicy.Pass, VisionHardGatePolicy.Soft, selectedStyle),
    VisionHardGatePolicy.Enforce(VisionHardGatePolicy.SceneParticipantsMatch, VisionHardGatePolicy.NotApplicable, VisionHardGatePolicy.Soft, selectedStyle),
    VisionHardGatePolicy.Enforce(VisionHardGatePolicy.SingleComposition, VisionHardGatePolicy.Pass, VisionHardGatePolicy.Hard, selectedStyle),
    VisionHardGatePolicy.Enforce(VisionHardGatePolicy.StyleMatch, VisionHardGatePolicy.Pass, VisionHardGatePolicy.Hard, selectedStyle),
    VisionHardGatePolicy.Enforce(VisionHardGatePolicy.BoldEasyMatch, VisionHardGatePolicy.Pass, VisionHardGatePolicy.Hard, selectedStyle),
    VisionHardGatePolicy.Enforce(VisionHardGatePolicy.CozyMatch, VisionHardGatePolicy.Pass, VisionHardGatePolicy.Hard, selectedStyle),
    VisionHardGatePolicy.Enforce(VisionHardGatePolicy.LineWeightMatch, VisionHardGatePolicy.Pass, VisionHardGatePolicy.Hard, selectedStyle)
};
var passed = VisionHardGatePolicy.Aggregate(passSet);
Require(passed.OverallStatus == VisionHardGatePolicy.Pass && !passed.BlocksApproval,
    "PASS/NA semantic gates must allow approval.");

var instructions = VisionHardGatePolicy.InstructionMarkdown();
foreach (var key in new[]
         {
             VisionHardGatePolicy.SubjectMatch,
             VisionHardGatePolicy.SceneParticipantsMatch,
             VisionHardGatePolicy.SingleComposition,
             VisionHardGatePolicy.StyleMatch,
             VisionHardGatePolicy.BoldEasyMatch,
             VisionHardGatePolicy.CozyMatch,
             VisionHardGatePolicy.LineWeightMatch
         })
    Require(instructions.Contains($"`{key}`", StringComparison.Ordinal),
        $"Prompt Pack instructions must name canonical Vision gate {key}.");
Require(instructions.Contains("One HARD failure forces `overall_status = FAIL`", StringComparison.Ordinal),
    "Prompt Pack instructions must state the same blocking rule enforced by the Core policy.");

Console.WriteLine("VISION PIANIST PASS: semantic failures could not be downgraded, soft quality stayed soft, and one HARD failure blocked approval.");
