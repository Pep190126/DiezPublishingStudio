using System.Runtime.CompilerServices;
using DiezPublishingStudio;

internal static class AvaloniaHardGateAssertions
{
    [ModuleInitializer]
    internal static void Run()
    {
        var style = "Kawaii";
        var restored = new[]
        {
            VisionHardGatePolicy.EnvironmentMatch,
            VisionHardGatePolicy.BookTypeFit,
            VisionHardGatePolicy.ColorOutputMatch,
            VisionHardGatePolicy.DrawingCraft,
            VisionHardGatePolicy.ColorableRegions,
            VisionHardGatePolicy.CleanContours,
            VisionHardGatePolicy.MicroDetailFit,
            VisionHardGatePolicy.SubjectReadability,
            VisionHardGatePolicy.VisibleTextOrWatermark,
            VisionHardGatePolicy.MustDo,
            VisionHardGatePolicy.MustNotDo
        };

        foreach (var key in restored)
        {
            var attemptedDowngrade = VisionHardGatePolicy.Enforce(
                key,
                VisionHardGatePolicy.Fail,
                VisionHardGatePolicy.Soft,
                style);
            if (attemptedDowngrade.Severity != VisionHardGatePolicy.Hard || !attemptedDowngrade.BlocksApproval)
                throw new InvalidOperationException($"Avalonia HARD gate {key} was downgraded or stopped blocking approval.");
        }

        var instructions = VisionHardGatePolicy.InstructionMarkdown();
        foreach (var key in restored)
            if (!instructions.Contains($"`{key}`", StringComparison.Ordinal))
                throw new InvalidOperationException($"Vision policy instructions omit restored HARD gate {key}.");

        if (!instructions.Contains("Primitive geometric placeholder art", StringComparison.OrdinalIgnoreCase) ||
            !instructions.Contains("cold, empty, schematic", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Vision policy lost the physical-test semantics for Kawaii/Cozy false positives.");
    }
}
