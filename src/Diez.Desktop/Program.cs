using System.Text;
using Avalonia;
using Avalonia.Fonts.Inter;

namespace DiezPublishingStudio;

internal static class Program
{
    private const string AppMutexName = "DiezPublishingStudio.App";
    internal const string SelfTestErrorFileName = "self-test-error.txt";

    [STAThread]
    public static int Main(string[] args)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        if (args.Any(a => string.Equals(a, "--self-test", StringComparison.OrdinalIgnoreCase)))
        {
            var errorPath = Path.Combine(AppContext.BaseDirectory, SelfTestErrorFileName);
            try
            {
                if (File.Exists(errorPath)) File.Delete(errorPath);
                PreFinalContractSelfTest.Run();
                PackageSelfTest.RunAsync().GetAwaiter().GetResult();
                EditableMasterSelfTest.RunAsync().GetAwaiter().GetResult();
                EditionMetadataSelfTest.RunAsync().GetAwaiter().GetResult();
                EditionFreezeSelfTest.RunAsync().GetAwaiter().GetResult();
                PublicationCandidateSelfTest.RunAsync().GetAwaiter().GetResult();
                DocxExportSelfTest.RunAsync().GetAwaiter().GetResult();
                HandoffExportSelfTest.RunAsync().GetAwaiter().GetResult();
                ProductionPackageSelfTest.RunAsync().GetAwaiter().GetResult();
                AiProductionSelfTest.RunAsync().GetAwaiter().GetResult();
                AiImageBatchSelfTest.RunAsync().GetAwaiter().GetResult();
                HumanAiPromptEditingSelfTest.Run();
                ColoringAiCreationSelfTest.Run();
                AiExchangeSelfTest.RunAsync().GetAwaiter().GetResult();
                AiExchangeApiSelfTest.RunAsync().GetAwaiter().GetResult();
                AiExchangeImageContextSelfTest.RunAsync().GetAwaiter().GetResult();
                PromptEngineeringSelfTest.Run();
                PromptManualReconciliationSelfTest.Run();
                VisualPromptIsolationSelfTest.RunAsync().GetAwaiter().GetResult();
                AiExchangeThreeImageImportSelfTest.RunAsync().GetAwaiter().GetResult();
                ImageCollectionLayoutSelfTest.RunAsync().GetAwaiter().GetResult();
                WordSearchWorkspaceSelfTest.RunAsync().GetAwaiter().GetResult();
                WordSearchReplacementSelfTest.Run();
                CrosswordSelfTest.RunAsync().GetAwaiter().GetResult();
                CrosswordThemeSelfTest.RunAsync().GetAwaiter().GetResult();
                return 0;
            }
            catch (Exception ex)
            {
                try { File.WriteAllText(errorPath, ex.ToString(), Encoding.UTF8); } catch { }
                return 1;
            }
        }

        using var mutex = new Mutex(true, AppMutexName, out var createdNew);
        if (!createdNew) return 0;

        var exitCode = BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        GC.KeepAlive(mutex);
        return exitCode;
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
