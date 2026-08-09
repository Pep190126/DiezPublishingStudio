using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace DiezPublishingStudio;

internal enum OutputDestination
{
    Computer,
    Google,
    Both
}

internal static class OutputDestinationUi
{
    public static async Task<OutputDestination?> ChooseAsync(Window owner, string googleDestination, string what)
    {
        var dialog = new OutputDestinationWindow(googleDestination, what);
        return await dialog.ShowDialog<OutputDestination?>(owner);
    }

    public static string TempPath(string suggestedFileName)
    {
        var safe = string.Concat((suggestedFileName ?? "diez-output").Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
        if (string.IsNullOrWhiteSpace(safe)) safe = "diez-output";
        var folder = Path.Combine(Path.GetTempPath(), "DiezPublishingStudio", "GoogleOutput");
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, Guid.NewGuid().ToString("N") + "-" + safe);
    }
}

internal sealed class OutputDestinationWindow : Window
{
    public OutputDestinationWindow(string googleDestination, string what)
    {
        Title = "Dove vuoi usarlo?";
        Width = 520;
        Height = 300;
        MinWidth = 480;
        MinHeight = 280;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var local = Choice("Sul computer", "Salva il file normale sul PC.", OutputDestination.Computer);
        var google = Choice(googleDestination, $"Carica il file nel tuo Drive e aprilo in {googleDestination} nel browser.", OutputDestination.Google);
        var both = Choice("Entrambi", $"Salva una copia sul PC e apri la stessa uscita anche in {googleDestination}.", OutputDestination.Both);
        var cancel = new Button
        {
            Content = "Annulla",
            Width = 110,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        cancel.Click += (_, _) => Close(null);

        Content = new Border
        {
            Padding = new Thickness(18),
            Child = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = "Dove vuoi usare questo output?", FontSize = 21, HorizontalAlignment = HorizontalAlignment.Center },
                    new TextBlock
                    {
                        Text = what,
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                        HorizontalAlignment = HorizontalAlignment.Center
                    },
                    local,
                    google,
                    both,
                    cancel
                }
            }
        };
    }

    private Button Choice(string title, string help, OutputDestination result)
    {
        var button = new Button
        {
            Content = title,
            Height = 38,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        ToolTip.SetTip(button, help);
        button.Click += (_, _) => Close(result);
        return button;
    }
}
