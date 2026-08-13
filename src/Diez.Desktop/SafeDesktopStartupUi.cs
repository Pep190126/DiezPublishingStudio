using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace DiezPublishingStudio;

internal static class SafeDesktopStartupUi
{
    public static void Attach(MainWindow window, Func<Task> activateAsync)
    {
        var originalContent = window.Content;
        var status = new TextBlock
        {
            Text = "Avvio sicuro attivo. La UI completa non è ancora stata caricata.",
            FontSize = 16,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            MaxWidth = 720
        };
        var details = new TextBlock
        {
            Text = "Se questa schermata resta visibile e reattiva, il renderer Avalonia di base funziona. Premi il pulsante per caricare l'interfaccia completa a moduli tracciati.",
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            MaxWidth = 720
        };
        var activate = new Button
        {
            Content = "Carica interfaccia completa",
            Width = 260,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var shell = new Border
        {
            Background = Brushes.WhiteSmoke,
            Padding = new Thickness(32),
            Child = new StackPanel
            {
                Spacing = 18,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Diez Publishing Studio",
                        FontSize = 28,
                        Foreground = Brushes.Black,
                        HorizontalAlignment = HorizontalAlignment.Center
                    },
                    status,
                    details,
                    activate
                }
            }
        };
        status.Foreground = Brushes.Black;
        details.Foreground = Brushes.Black;

        window.Content = shell;
        window.Title = ProductInfo.WindowTitle + " — avvio sicuro";
        SafeStartupTrace.Reset("minimal-shell-installed");

        activate.Click += async (_, _) =>
        {
            activate.IsEnabled = false;
            status.Text = "Ripristino della UI base…";
            window.Title = ProductInfo.WindowTitle + " — ripristino UI base";
            SafeStartupTrace.Write("activation-clicked");

            try
            {
                await Task.Delay(60);
                window.Content = originalContent;
                SafeStartupTrace.Write("base-mainwindow-content-restored");
                window.Title = ProductInfo.WindowTitle + " — UI base ripristinata";

                // Give Avalonia one real dispatcher turn with only MainWindow's original controls.
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Loaded);
                await Task.Delay(100);
                SafeStartupTrace.Write("base-mainwindow-first-turn-completed");

                await activateAsync();
                SafeStartupTrace.Write("all-production-modules-completed");
                window.Title = ProductInfo.WindowTitle;
            }
            catch (Exception ex)
            {
                SafeStartupTrace.Write("activation-failed: " + ex);
                CrashDiagnostics.Error("safe-startup-activation", ex);
                window.Title = ProductInfo.WindowTitle + " — caricamento incompleto";
            }
        };
    }
}
