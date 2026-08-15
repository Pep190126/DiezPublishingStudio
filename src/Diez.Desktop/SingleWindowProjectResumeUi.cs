using System.Reflection;
using Avalonia.Controls;
using Avalonia.Threading;

namespace DiezPublishingStudio;

/// <summary>
/// Keeps the loaded project active while Home/flow navigation changes. The Home entry itself is owned
/// exclusively by SingleWindowNativeEntryBridgeUi; this module only updates that one button's label/tooltip.
/// It must never create, replace or duplicate a Percorso libro button.
/// </summary>
internal static class SingleWindowProjectResumeUi
{
    private static readonly HashSet<MainWindow> Attached = [];

    public static void Attach(MainWindow window)
    {
        if (!Attached.Add(window)) return;

        var entry = Descendants(window).OfType<Button>().FirstOrDefault(b =>
            string.Equals(b.Name, SingleWindowNativeEntryBridgeUi.NativeEntryName, StringComparison.Ordinal));
        if (entry is null)
            throw new InvalidOperationException("Ingresso nativo unico Percorso libro non disponibile per il resume progetto.");

        void RefreshLabel()
        {
            entry.Content = TrySession(window) ? "Avanti · Tipo libro" : "Percorso libro";
            ToolTip.SetTip(entry, TrySession(window)
                ? "Il progetto aperto resta attivo. Riprendi dalla scelta del Tipo libro."
                : "Crea o apri un progetto, poi percorri il libro nella stessa finestra.");
        }

        entry.PointerEntered += (_, _) => RefreshLabel();
        entry.GotFocus += (_, _) => RefreshLabel();

        foreach (var projectButton in Descendants(window).OfType<Button>().Where(b =>
                     string.Equals(b.Content?.ToString(), "Nuovo progetto", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(b.Content?.ToString(), "Apri progetto", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(b.Content?.ToString(), "Apri .diez", StringComparison.OrdinalIgnoreCase)))
        {
            projectButton.Click += (_, _) => Dispatcher.UIThread.Post(RefreshLabel, DispatcherPriority.Background);
        }

        var host = SingleWindowEntryPointUi.GetHost(window);
        if (host.GetType().GetField("_pageHost", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host) is ContentControl pageHost)
        {
            pageHost.PropertyChanged += (_, e) =>
            {
                if (e.Property == ContentControl.ContentProperty)
                    Dispatcher.UIThread.Post(RefreshLabel, DispatcherPriority.Background);
            };
        }

        window.Opened += (_, _) => RefreshLabel();
        window.Closed += (_, _) => Attached.Remove(window);
        RefreshLabel();
        SafeStartupTrace.Write("project-resume | native-entry-reused=true");
    }

    internal static void Resume(MainWindow window)
    {
        if (TrySession(window)) SingleWindowStableEntryBridgeUi.ShowStartPrepared(window);
    }

    internal static bool HasActiveProject(MainWindow window) => TrySession(window);

    internal static async Task RunContractAsync(MainWindow window)
    {
        var host = SingleWindowEntryPointUi.GetHost(window);
        var pageHost = host.GetType().GetField("_pageHost", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host) as ContentControl
            ?? throw new InvalidOperationException("PageHost non disponibile nel contratto Home/Resume.");
        var tempPath = Path.Combine(Path.GetTempPath(), "diez-resume-contract-" + Guid.NewGuid().ToString("N") + ".diez");
        try
        {
            var project = ProjectFileStore.Create("Resume Contract");
            BookTypeProfileService.Set(project, BookTypeProfileService.ColoringBook);
            await ProjectFileStore.SaveAsync(tempPath, project);
            SetSession(window, project, tempPath);

            SingleWindowStableEntryBridgeUi.ShowStartPrepared(window);
            await WaitAsync();
            AssertText(pageHost.Content as Control, "Quale libro stai preparando?");

            SingleWindowEntryPointUi.Invoke(host, "ShowHome");
            await WaitAsync();
            if (!HasActiveProject(window))
                throw new InvalidOperationException("Tornando Home il progetto attivo è stato perso.");

            var entries = Descendants(window).OfType<Button>().Where(b =>
                string.Equals(b.Name, SingleWindowNativeEntryBridgeUi.NativeEntryName, StringComparison.Ordinal)).ToList();
            if (entries.Count != 1)
                throw new InvalidOperationException($"La Home deve avere un solo ingresso nativo al percorso libro; trovati {entries.Count}.");
            if (!string.Equals(entries[0].Content?.ToString(), "Avanti · Tipo libro", StringComparison.Ordinal))
                throw new InvalidOperationException("Con progetto attivo la Home non mostra 'Avanti · Tipo libro'.");

            var visibleBookFlowButtons = Descendants(window).OfType<Button>().Where(b => b.IsVisible &&
                ((b.Content?.ToString() ?? string.Empty).Contains("Percorso libro", StringComparison.OrdinalIgnoreCase) ||
                 (b.Content?.ToString() ?? string.Empty).Contains("Tipo libro", StringComparison.OrdinalIgnoreCase)))
                .ToList();
            if (visibleBookFlowButtons.Count != 1)
                throw new InvalidOperationException($"Sono visibili più ingressi al percorso libro: {visibleBookFlowButtons.Count}.");

            Resume(window);
            await WaitAsync();
            AssertText(pageHost.Content as Control, "Quale libro stai preparando?");
            if (pageHost.Content is Control page && Descendants(page).OfType<TextBlock>().Any(t =>
                    (t.Text ?? string.Empty).Contains("Crea o apri un progetto", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Riprendendo dalla Home Diez è tornato erroneamente a Nuovo/Apri progetto.");
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
        }
    }

    private static async Task WaitAsync()
    {
        await Task.Delay(120);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
    }

    private static void AssertText(Control? root, string expected)
    {
        if (root is null || !Descendants(root).OfType<TextBlock>().Any(t =>
                (t.Text ?? string.Empty).Contains(expected, StringComparison.Ordinal)))
            throw new InvalidOperationException("Testo UI mancante nel contratto Home/Resume: " + expected);
    }

    private static void SetSession(MainWindow window, PreviewProject project, string path)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(MainWindow).GetField("_project", flags)?.SetValue(window, project);
        typeof(MainWindow).GetField("_currentProjectPath", flags)?.SetValue(window, path);
    }

    private static bool TrySession(MainWindow window)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var project = typeof(MainWindow).GetField("_project", flags)?.GetValue(window) as PreviewProject;
        var path = typeof(MainWindow).GetField("_currentProjectPath", flags)?.GetValue(window) as string;
        return project is not null && !string.IsNullOrWhiteSpace(path);
    }

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
