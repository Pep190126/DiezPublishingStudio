using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace DiezPublishingStudio;

internal static class SafeDesktopStartupUi
{
    public static Window CreateStandalone(Func<Window, Task> activateAsync)
    {
        var status = new TextBlock
        {
            Text = "Avvio sicuro attivo. MainWindow e la UI completa non sono ancora stati creati.",
            FontSize = 16,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            MaxWidth = 720,
            Foreground = Brushes.Black
        };
        var details = new TextBlock
        {
            Text = "Questa è una Window Avalonia minimale con rendering software forzato. Se resta visibile e reattiva, il percorso grafico di base è stabile. Premi il pulsante per creare MainWindow e caricare i moduli tracciati.",
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            MaxWidth = 720,
            Foreground = Brushes.Black
        };
        var activate = new Button
        {
            Content = "Carica interfaccia completa",
            Width = 260,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var window = new Window
        {
            Title = ProductInfo.WindowTitle + " — avvio sicuro software",
            Width = 820,
            Height = 520,
            MinWidth = 640,
            MinHeight = 420,
            Background = Brushes.WhiteSmoke,
            Content = new Border
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
            }
        };

        SafeStartupTrace.Write("bare-shell-created | renderer=Win32RenderingMode.Software");

        window.Opened += (_, _) =>
        {
            SafeStartupTrace.Write("bare-shell-opened");

            // ClassicDesktopStyleApplicationLifetime shows MainWindow before entering the Win32 dispatcher loop.
            // If a stray WM_QUIT is already queued on this UI thread, GetMessage returns 0 immediately and the
            // application exits without Closing/Closed/ShutdownRequested events. Probe that exact condition here.
            var consumedQuit = Win32QuitMessageProbe.ProbeAndConsume(out var quitCode);
            SafeStartupTrace.Write("bare-shell-opened-wmquit-result | consumed=" + consumedQuit + " | code=" + quitCode);

            Dispatcher.UIThread.Post(
                () => SafeStartupTrace.Write("bare-shell-loaded-dispatcher-turn"),
                DispatcherPriority.Loaded);
        };

        activate.Click += async (_, _) =>
        {
            activate.IsEnabled = false;
            status.Text = "Creazione della MainWindow…";
            window.Title = ProductInfo.WindowTitle + " — creazione MainWindow";
            SafeStartupTrace.Write("activation-clicked");

            try
            {
                await activateAsync(window);
            }
            catch (Exception ex)
            {
                SafeStartupTrace.Write("activation-failed: " + ex);
                CrashDiagnostics.Error("safe-startup-activation", ex);
                window.Title = ProductInfo.WindowTitle + " — caricamento incompleto";
                status.Text = "Caricamento incompleto. Il dettaglio è stato scritto nel log di avvio.";
            }
        };

        return window;
    }
}
