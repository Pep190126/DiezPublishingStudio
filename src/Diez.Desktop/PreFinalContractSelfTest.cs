using System.Reflection;

namespace DiezPublishingStudio;

internal static class PreFinalContractSelfTest
{
    public static void Run()
    {
        if (!string.Equals(ProductInfo.Version, "1.0.0-rc4", StringComparison.Ordinal))
            throw new InvalidOperationException($"Identità prodotto inattesa: {ProductInfo.Version}");

        if (!ProductInfo.WindowTitle.Contains("1.0 RC4", StringComparison.Ordinal) ||
            !ProductInfo.WindowTitle.Contains("Pre-finale", StringComparison.Ordinal))
            throw new InvalidOperationException("Il titolo della build pre-finale non espone correttamente 1.0 RC4.");

        var informational = typeof(Program).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? string.Empty;
        if (!informational.StartsWith(ProductInfo.Version, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Versione assembly non allineata: '{informational}' vs '{ProductInfo.Version}'.");

        if (ProductInfo.Subtitle.Contains("Preview 0.", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("La UI pre-finale contiene ancora branding Preview 0.x.");

        if (ProductInfo.Subtitle.Contains("Preflight", StringComparison.OrdinalIgnoreCase) ||
            ProductInfo.Subtitle.Contains("Publication Candidate", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Il branding principale espone ancora gergo tecnico non necessario all'utente.");
    }
}
