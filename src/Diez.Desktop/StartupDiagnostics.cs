using System.Text;
using Avalonia.Controls;
using Avalonia.Media;

namespace DiezPublishingStudio;

internal static class StartupDiagnostics
{
    private const string LogFileName = "startup-errors.log";

    public static bool TryAttach(string moduleName, Action attach, out string? errorSummary)
    {
        try
        {
            attach();
            errorSummary = null;
            return true;
        }
        catch (Exception ex)
        {
            var logPath = Write(moduleName, ex);
            errorSummary = $"{moduleName}: {ex.Message} · log: {logPath}";
            return false;
        }
    }

    public static void ShowWarning(MainWindow window, IReadOnlyList<string> failures)
    {
        if (failures.Count == 0) return;

        var root = FindRoot(window);
        if (root is null) return;

        var warning = new TextBlock
        {
            Text = "Avvio in modalità diagnostica: una parte dell'interfaccia non è stata caricata. " + string.Join(" | ", failures),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 1040,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
        };
        root.Children.Insert(Math.Min(4, root.Children.Count), warning);
        window.Title = ProductInfo.WindowTitle + " — diagnostica";
    }

    private static StackPanel? FindRoot(MainWindow window)
    {
        if (window.Content is not Border border) return null;
        if (border.Child is StackPanel direct) return direct;
        if (border.Child is ScrollViewer scroll && scroll.Content is StackPanel nested) return nested;
        return null;
    }

    private static string Write(string moduleName, Exception ex)
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Diez Publishing Studio",
            "logs");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, LogFileName);

        var text = new StringBuilder()
            .AppendLine("============================================================")
            .AppendLine(DateTimeOffset.Now.ToString("O"))
            .AppendLine("Version: " + ProductInfo.Version)
            .AppendLine("Module: " + moduleName)
            .AppendLine(ex.ToString())
            .ToString();
        File.AppendAllText(path, text, Encoding.UTF8);
        return path;
    }
}
