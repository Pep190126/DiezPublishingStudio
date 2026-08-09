using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace DiezPublishingStudio;

internal enum OutputOpenChoice
{
    Local,
    Google,
    KeepSaved
}

internal readonly record struct OutputOpenResult(bool Success, string Message, string? GoogleUrl = null);

internal static class OutputOpenChoiceUi
{
    public static async Task<OutputOpenResult> AskAndOpenAsync(Window owner, string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return new(false, "Il file da aprire non esiste.");

        var choice = await new OutputOpenChoiceWindow(filePath, fallbackOnly: false).ShowDialog<OutputOpenChoice?>(owner);
        if (choice is null || choice == OutputOpenChoice.KeepSaved)
            return new(true, $"File salvato: {filePath}");

        if (choice == OutputOpenChoice.Google)
            return await OpenWithGoogleAsync(filePath);

        var local = TryOpenLocal(filePath);
        if (local.Success) return local;

        var fallback = await new OutputOpenChoiceWindow(filePath, fallbackOnly: true).ShowDialog<OutputOpenChoice?>(owner);
        if (fallback == OutputOpenChoice.Google)
            return await OpenWithGoogleAsync(filePath);
        return new(true, $"Nessun programma associato: il file resta salvato qui: {filePath}");
    }

    internal static OutputOpenResult TryOpenLocal(string filePath)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = filePath,
                UseShellExecute = true
            });
            return new(true, $"Apertura avviata con il programma associato a {Path.GetExtension(filePath)}.");
        }
        catch (Win32Exception)
        {
            return new(false, "Windows non trova un programma associato a questo tipo di file.");
        }
        catch (Exception ex)
        {
            return new(false, "Non riesco ad aprire il file sul computer: " + ex.Message);
        }
    }

    internal static async Task<OutputOpenResult> OpenWithGoogleAsync(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        GoogleDocsExportResult google;
        if (extension == ".docx")
            google = await GoogleDocsExportService.ExportDocxAsync(filePath, Path.GetFileName(filePath));
        else if (extension == ".xlsx")
            google = await GoogleDocsExportService.ExportXlsxAsync(filePath, Path.GetFileName(filePath));
        else if (extension == ".csv")
            google = await GoogleDocsExportService.ExportCsvAsSheetAsync(filePath, Path.GetFileNameWithoutExtension(filePath));
        else
            return new(false, "Questo formato non può essere aperto direttamente con Google Documenti o Fogli Google.");

        return new(google.Success, google.Message, google.DocumentUrl);
    }
}

internal sealed class OutputOpenChoiceWindow : Window
{
    public OutputOpenChoiceWindow(string filePath, bool fallbackOnly)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        var googleName = extension == ".docx" ? "Google Documenti" : "Fogli Google";

        Title = fallbackOnly ? "Nessun programma associato" : "Come vuoi aprire l'output?";
        Width = 560;
        Height = fallbackOnly ? 285 : 330;
        MinWidth = 520;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var stack = new StackPanel { Spacing = 12 };
        stack.Children.Add(new TextBlock
        {
            Text = fallbackOnly
                ? "Windows non trova un programma associato a questo tipo di file."
                : "Il file è stato creato. Come vuoi usarlo adesso?",
            FontSize = 20,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });
        stack.Children.Add(new TextBlock
        {
            Text = Path.GetFileName(filePath),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });

        if (!fallbackOnly)
        {
            var local = Choice("Apri sul computer", $"Usa il programma che il computer ha associato ai file {extension.ToUpperInvariant()}.", OutputOpenChoice.Local);
            stack.Children.Add(local);
        }

        var google = Choice($"Apri con {googleName}", $"Se serve, accedi al tuo account Google; poi Diez carica il file nel Drive e lo apre in {googleName} nel browser.", OutputOpenChoice.Google);
        var keep = Choice("Lascia il file salvato", "Non apre nulla: conserva il file esattamente nella posizione che hai scelto.", OutputOpenChoice.KeepSaved);
        stack.Children.Add(google);
        stack.Children.Add(keep);

        Content = new Border { Padding = new Thickness(20), Child = stack };
    }

    private Button Choice(string text, string help, OutputOpenChoice result)
    {
        var button = new Button
        {
            Content = text,
            Height = 42,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        ToolTip.SetTip(button, help);
        button.Click += (_, _) => Close(result);
        return button;
    }
}
