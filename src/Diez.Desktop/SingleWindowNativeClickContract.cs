using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace DiezPublishingStudio;

/// <summary>
/// Exercises the actual production click route instead of calling the destination pages directly.
/// This closes the gap where the headless contract could be green while a physical user's Avanti click failed.
/// </summary>
internal static class SingleWindowNativeClickContract
{
    public static async Task RunAsync(MainWindow window)
    {
        var temp = Path.Combine(Path.GetTempPath(), "diez-native-click-" + Guid.NewGuid().ToString("N") + ".diez");
        var host = SingleWindowEntryPointUi.GetHost(window);
        var pageHost = Field<ContentControl>(host, "_pageHost")
            ?? throw new InvalidOperationException("PageHost nativo non disponibile per il click contract.");
        Exception? dispatcherUnhandled = null;

        void CaptureUnhandled(object? sender, DispatcherUnhandledExceptionEventArgs e)
        {
            dispatcherUnhandled ??= e.Exception;
            e.Handled = true;
        }

        Dispatcher.UIThread.UnhandledException += CaptureUnhandled;
        try
        {
            var project = ProjectFileStore.Create("Native Click Contract");
            BookTypeProfileService.Set(project, BookTypeProfileService.ColoringBook);
            await ProjectFileStore.SaveAsync(temp, project);
            SetSession(window, project, temp);

            var entry = Descendants(window).OfType<Button>().FirstOrDefault(b =>
                string.Equals(b.Name, SingleWindowNativeEntryBridgeUi.NativeEntryName, StringComparison.Ordinal));
            if (entry is null || !entry.IsVisible || !entry.IsEnabled)
                throw new InvalidOperationException("Ingresso Percorso libro nativo non visibile/abilitato.");

            entry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntilAsync(() => HasControl(pageHost, "DiezNativeBookTypePage"),
                "pagina Tipo libro dopo click Percorso libro", () => dispatcherUnhandled);

            var typePage = pageHost.Content as Control
                ?? throw new InvalidOperationException("Pagina Tipo libro non materializzata.");
            var combo = Descendants(typePage).OfType<ComboBox>().FirstOrDefault(c => c.Name == "DiezNativeBookTypeCombo")
                ?? throw new InvalidOperationException("Combo Tipo libro nativa mancante.");
            var apply = Descendants(typePage).OfType<Button>().FirstOrDefault(b => b.Name == "DiezNativeBookTypeApply")
                ?? throw new InvalidOperationException("Pulsante Tipo libro nativo mancante.");
            combo.SelectedItem = BookTypeProfileService.ColoringBook;
            if (!apply.IsEnabled) throw new InvalidOperationException("Pulsante Tipo libro disabilitato prima del click.");

            apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntilAsync(() => HasControl(pageHost, "DiezNativeV11QuantityPage"),
                "pagina Quantità dopo click Tipo libro", () => dispatcherUnhandled);

            var quantity = pageHost.Content as Control
                ?? throw new InvalidOperationException("Pagina Quantità non materializzata.");
            var count = Descendants(quantity).OfType<NumericUpDown>().FirstOrDefault(c => c.Name == "ExactImageCount")
                ?? throw new InvalidOperationException("Numero immagini nativo mancante.");
            count.Value = 3;

            var consistent = Descendants(quantity).OfType<CheckBox>().FirstOrDefault(c => c.Name == "NativeConsistent")
                ?? throw new InvalidOperationException("Consistent nativo mancante.");
            consistent.IsChecked = false;
            await DrainAsync();
            ThrowIfUnhandled(dispatcherUnhandled, "preparazione pagina Quantità");

            var next = Descendants(quantity).OfType<Button>().FirstOrDefault(b =>
                (b.Content?.ToString() ?? string.Empty).Contains("Avanti", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("Pulsante Avanti nativo mancante.");
            if (!next.IsEnabled)
                throw new InvalidOperationException("Avanti è disabilitato con Consistent OFF prima del click reale.");

            next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntilAsync(() => HasControl(pageHost, "DiezNativeV11PromptPage"),
                "pagina Istruzioni dopo click Avanti", () => dispatcherUnhandled);

            if (!window.IsEnabled)
                throw new InvalidOperationException("MainWindow risulta disabilitata dopo il click Avanti.");

            var prompt = pageHost.Content as Control
                ?? throw new InvalidOperationException("Pagina Istruzioni non materializzata.");
            foreach (var name in new[] { "MustDoEditor", "MustNotDoEditor", "PromptEditor" })
            {
                var editor = Descendants(prompt).OfType<TextBox>().FirstOrDefault(t => t.Name == name);
                if (editor is null || !editor.IsEnabled || editor.IsReadOnly)
                    throw new InvalidOperationException("Editor Istruzioni non operativo dopo il click reale: " + name);
            }
            ThrowIfUnhandled(dispatcherUnhandled, "completamento click Avanti");
        }
        finally
        {
            Dispatcher.UIThread.UnhandledException -= CaptureUnhandled;
            try { SingleWindowEntryPointUi.Invoke(host, "ShowHome"); } catch { }
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
        }
    }

    private static async Task WaitUntilAsync(
        Func<bool> condition,
        string description,
        Func<Exception?> getUnhandled)
    {
        for (var i = 0; i < 40; i++)
        {
            await DrainAsync();
            ThrowIfUnhandled(getUnhandled(), description);
            if (condition()) return;
            await Task.Delay(25);
        }
        ThrowIfUnhandled(getUnhandled(), description);
        throw new TimeoutException("Timeout nel click contract: " + description + ".");
    }

    private static void ThrowIfUnhandled(Exception? exception, string stage)
    {
        if (exception is null) return;
        throw new InvalidOperationException("Eccezione async UI durante " + stage + ": " + exception.Message, exception);
    }

    private static async Task DrainAsync()
    {
        await Task.Yield();
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
    }

    private static bool HasControl(ContentControl host, string name) =>
        host.Content is Control root && Descendants(root).Any(c => string.Equals(c.Name, name, StringComparison.Ordinal));

    private static void SetSession(MainWindow window, PreviewProject project, string path)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(MainWindow).GetField("_project", flags)?.SetValue(window, project);
        typeof(MainWindow).GetField("_currentProjectPath", flags)?.SetValue(window, path);
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
