using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;

namespace DiezPublishingStudio;

/// <summary>
/// Makes the production Home entry use the same native SW-FLOW-12 pages exercised by CI.
/// The overlay host is still the single physical host, but its older SW-FLOW-2 entry button is
/// retired from user interaction so production and contract navigation cannot diverge.
/// </summary>
internal static class SingleWindowNativeEntryBridgeUi
{
    internal const string NativeEntryName = "DiezNativeBookFlowEntry";
    private static readonly HashSet<MainWindow> Attached = [];
    private static readonly HashSet<Button> WiredButtons = [];

    public static void Attach(MainWindow window)
    {
        if (!Attached.Add(window)) return;
        if (!TryCommandRow(window, out var row))
            throw new InvalidOperationException("Riga comandi progetto non disponibile per l'ingresso nativo.");

        foreach (var legacy in row.Children.OfType<Button>().Where(IsLegacyBookFlowEntry).ToList())
        {
            legacy.IsVisible = false;
            legacy.IsEnabled = false;
        }

        var entry = row.Children.OfType<Button>().FirstOrDefault(b => string.Equals(b.Name, NativeEntryName, StringComparison.Ordinal));
        if (entry is null)
        {
            entry = new Button
            {
                Name = NativeEntryName,
                Content = "Percorso libro",
                Width = 150,
                HorizontalContentAlignment = HorizontalAlignment.Center
            };
            ToolTip.SetTip(entry, "Percorso nativo SW-FLOW-12 nella stessa MainWindow.");
            row.Children.Add(entry);
        }

        entry.IsVisible = true;
        entry.IsEnabled = true;
        entry.Click += (_, _) => OpenNative(window);

        var host = SingleWindowEntryPointUi.GetHost(window);
        var pageHost = Field<ContentControl>(host, "_pageHost")
            ?? throw new InvalidOperationException("PageHost single-window non disponibile.");
        pageHost.PropertyChanged += (_, e) =>
        {
            if (e.Property != ContentControl.ContentProperty) return;
            Dispatcher.UIThread.Post(() => TraceCurrentPage(window, host, pageHost), DispatcherPriority.Loaded);
        };

        window.Closed += (_, _) => Attached.Remove(window);
        SafeStartupTrace.Write("native-entry-bridge-attached | legacy-disabled=true");
    }

    private static void OpenNative(MainWindow window)
    {
        SafeStartupTrace.Write(
            "ui-click | action=Percorso libro | route=native-v11" +
            " | windowEnabled=" + window.IsEnabled +
            " | active=" + window.IsActive);
        try
        {
            SingleWindowNativeV11Ui.ShowStart(window);
            SafeStartupTrace.Write("ui-navigation | target=native-v11-start | success=true");
        }
        catch (Exception ex)
        {
            SafeStartupTrace.Write("ui-navigation | target=native-v11-start | error=" + ex);
            CrashDiagnostics.Error("native-book-flow-entry", ex);
        }
    }

    private static void TraceCurrentPage(MainWindow window, object host, ContentControl pageHost)
    {
        try
        {
            var title = Field<TextBlock>(host, "_title")?.Text ?? "<untitled>";
            var page = pageHost.Content as Control;
            var buttons = page is null ? [] : Descendants(page).OfType<Button>().ToList();

            foreach (var button in buttons)
            {
                if (!WiredButtons.Add(button)) continue;
                button.AddHandler(Button.ClickEvent, (_, _) =>
                {
                    SafeStartupTrace.Write(
                        "ui-click | button=" + (button.Name ?? "<unnamed>") +
                        " | content=" + (button.Content?.ToString() ?? "<null>") +
                        " | enabled=" + button.IsEnabled +
                        " | windowEnabled=" + window.IsEnabled +
                        " | active=" + window.IsActive);
                }, RoutingStrategies.Bubble, handledEventsToo: true);
            }

            SafeStartupTrace.Write(
                "ui-page | title=" + title +
                " | windowEnabled=" + window.IsEnabled +
                " | active=" + window.IsActive +
                " | buttons=" + string.Join(";", buttons.Select(b =>
                    (b.Name ?? "<unnamed>") + ":" + (b.Content?.ToString() ?? "<null>") +
                    ":enabled=" + b.IsEnabled + ":visible=" + b.IsVisible)));
        }
        catch (Exception ex)
        {
            SafeStartupTrace.Write("ui-page-trace-error | " + ex.GetBaseException().Message);
        }
    }

    private static bool IsLegacyBookFlowEntry(Button button)
    {
        var text = button.Content?.ToString() ?? string.Empty;
        return string.Equals(text, "Percorso libro", StringComparison.Ordinal) ||
               text.Contains("Percorso libro · SW-FLOW-", StringComparison.Ordinal);
    }

    private static bool TryCommandRow(MainWindow window, out StackPanel row)
    {
        row = null!;
        if (window.Content is not Border border || border.Child is not Grid desktop) return false;
        var header = desktop.Children.OfType<Grid>().FirstOrDefault(c => Grid.GetRow(c) == 0);
        if (header is null) return false;
        row = header.Children.OfType<StackPanel>().FirstOrDefault(p =>
            p.Orientation == Orientation.Horizontal &&
            p.Children.OfType<Button>().Any(b => string.Equals(b.Content?.ToString(), "Nuovo progetto", StringComparison.OrdinalIgnoreCase)))!;
        return row is not null;
    }

    private static T? Field<T>(object host, string name) where T : class =>
        host.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host) as T;

    private static IEnumerable<Control> Descendants(Control root)
    {
        var stack = new Stack<Control>();
        var seen = new HashSet<Control>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!seen.Add(current)) continue;
            yield return current;
            switch (current)
            {
                case Panel panel:
                    for (var i = panel.Children.Count - 1; i >= 0; i--) stack.Push(panel.Children[i]);
                    break;
                case Border border when border.Child is Control child: stack.Push(child); break;
                case ScrollViewer scroll when scroll.Content is Control child: stack.Push(child); break;
                case ContentControl content when content.Content is Control child: stack.Push(child); break;
            }
        }
    }
}
