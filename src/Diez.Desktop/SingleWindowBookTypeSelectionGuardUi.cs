using System.Collections;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Threading;

namespace DiezPublishingStudio;

/// <summary>
/// Replaces the original book-type apply button with a guarded version.
/// The previous async click handler allowed an exception from persistence/navigation
/// to escape the UI event and terminate the desktop process. This adapter keeps the
/// app alive, reports the error visibly, logs diagnostics and exercises the exact
/// save/navigation path in installer CI.
/// </summary>
internal static class SingleWindowBookTypeSelectionGuardUi
{
    private const string SafeButtonName = "DiezSafeBookTypeApply";
    private static readonly SemaphoreSlim ApplyGate = new(1, 1);
    private static readonly HashSet<MainWindow> Attached = [];

    public static void Attach(MainWindow window)
    {
        if (!Attached.Add(window)) return;
        var host = SingleWindowEntryPointUi.GetHost(window);
        var pageHost = PageHost(host);
        pageHost.PropertyChanged += (_, e) =>
        {
            if (e.Property == ContentControl.ContentProperty)
                Dispatcher.UIThread.Post(() => EnsureCurrentPage(window), DispatcherPriority.Loaded);
        };
        window.Closed += (_, _) => Attached.Remove(window);
        EnsureCurrentPage(window);
    }

    internal static void EnsureCurrentPage(MainWindow window)
    {
        object host;
        try { host = SingleWindowEntryPointUi.GetHost(window); }
        catch { return; }
        var pageHost = PageHost(host);
        if (pageHost.Content is not Control page) return;
        if (!Descendants(page).OfType<TextBlock>().Any(t =>
                (t.Text ?? string.Empty).Contains("Quale libro stai preparando?", StringComparison.Ordinal)))
            return;

        if (Descendants(page).OfType<Button>().Any(b => string.Equals(b.Name, SafeButtonName, StringComparison.Ordinal)))
            return;

        var original = Descendants(page).OfType<Button>().FirstOrDefault(b =>
            string.Equals(b.Content?.ToString(), "Usa questo Tipo libro", StringComparison.Ordinal));
        var combo = Descendants(page).OfType<ComboBox>().FirstOrDefault();
        if (original is null || combo is null) return;

        var parent = Descendants(page).OfType<Panel>().FirstOrDefault(p => p.Children.Contains(original));
        if (parent is null) return;
        var index = parent.Children.IndexOf(original);
        parent.Children.Remove(original);

        var safe = new Button
        {
            Name = SafeButtonName,
            Content = "Usa questo Tipo libro",
            Width = 180,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center
        };
        safe.Click += async (_, _) => await ApplySelectedBookTypeAsync(window, host, combo, safe);
        parent.Children.Insert(Math.Max(0, index), safe);
    }

    internal static async Task RunContractAsync(MainWindow window)
    {
        var temp = Path.Combine(Path.GetTempPath(), "diez-book-type-click-" + Guid.NewGuid().ToString("N") + ".diez");
        try
        {
            var project = ProjectFileStore.Create("Book Type Click Contract");
            await ProjectFileStore.SaveAsync(temp, project);
            SetSession(window, project, temp);
            SingleWindowV5StartupUi.ShowStart(window);
            EnsureCurrentPage(window);

            var host = SingleWindowEntryPointUi.GetHost(window);
            var page = PageHost(host).Content as Control
                ?? throw new InvalidOperationException("Pagina Tipo libro assente nel contract.");
            var combo = Descendants(page).OfType<ComboBox>().FirstOrDefault()
                ?? throw new InvalidOperationException("Combo Tipo libro assente nel contract.");
            var safe = Descendants(page).OfType<Button>().FirstOrDefault(b => string.Equals(b.Name, SafeButtonName, StringComparison.Ordinal))
                ?? throw new InvalidOperationException("Pulsante protetto Tipo libro assente nel contract.");

            combo.SelectedItem = BookTypeProfileService.ColoringBook;
            var ok = await ApplySelectedBookTypeAsync(window, host, combo, safe);
            if (!ok) throw new InvalidOperationException("Il click protetto Tipo libro non ha completato salvataggio e navigazione.");

            var reloaded = await ProjectFileStore.LoadAsync(temp);
            if (!string.Equals(BookTypeProfileService.Get(reloaded), BookTypeProfileService.ColoringBook, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Il Tipo libro non è stato persistito dal click reale.");

            var nextPage = PageHost(host).Content as Control;
            if (nextPage is null || !Descendants(nextPage).OfType<TextBlock>().Any(t =>
                    (t.Text ?? string.Empty).Contains("Quante immagini vuoi creare?", StringComparison.Ordinal)))
                throw new InvalidOperationException("Il click Tipo libro non ha navigato alla schermata successiva.");
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
        }
    }

    private static async Task<bool> ApplySelectedBookTypeAsync(MainWindow window, object host, ComboBox combo, Button button)
    {
        if (!await ApplyGate.WaitAsync(0))
        {
            Report(window, host, "Sto già salvando il Tipo libro. Attendi il completamento del click precedente.");
            return false;
        }

        button.IsEnabled = false;
        try
        {
            if (!TrySession(window, out var project, out var path))
                throw new InvalidOperationException("Sessione progetto non disponibile. Il progetto resta aperto: riprova dopo averlo salvato.");

            var chosen = combo.SelectedItem?.ToString() ?? BookTypeProfileService.Other;
            BookTypeProfileService.Set(project, chosen);
            await ProjectFileStore.SaveAsync(path, project);
            ClearHistory(host);

            if (BookTypeProfileService.IsImageCollection(project))
                SingleWindowEntryPointUi.Invoke(host, "OpenQuantity");
            else
                SingleWindowEntryPointUi.Invoke(host, "OpenCurrentBook");

            Report(window, host, $"Tipo libro salvato: {BookTypeProfileService.Get(project)}.");
            return true;
        }
        catch (Exception ex)
        {
            var detail = ex.GetBaseException().Message;
            Report(window, host, "Non sono riuscito ad applicare il Tipo libro. Diez resta aperto. Dettaglio: " + detail);
            WriteDiagnostic(ex);
            return false;
        }
        finally
        {
            button.IsEnabled = true;
            ApplyGate.Release();
        }
    }

    private static void ClearHistory(object host)
    {
        var history = host.GetType().GetField("_history", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host);
        if (history is IList list) list.Clear();
    }

    private static void Report(MainWindow window, object host, string text)
    {
        var hostStatus = host.GetType().GetField("_status", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host) as TextBlock;
        if (hostStatus is not null) hostStatus.Text = text;
        var mainStatus = typeof(MainWindow).GetField("_status", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(window) as TextBlock;
        if (mainStatus is not null) mainStatus.Text = text;
    }

    private static void WriteDiagnostic(Exception ex)
    {
        try
        {
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Diez Publishing Studio", "logs");
            Directory.CreateDirectory(root);
            File.AppendAllText(Path.Combine(root, "book-type-errors.log"),
                $"[{DateTimeOffset.Now:O}] {ex}\n\n");
        }
        catch { }
    }

    private static bool TrySession(MainWindow window, out PreviewProject project, out string path)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        project = typeof(MainWindow).GetField("_project", flags)?.GetValue(window) as PreviewProject ?? null!;
        path = typeof(MainWindow).GetField("_currentProjectPath", flags)?.GetValue(window) as string ?? string.Empty;
        return project is not null && !string.IsNullOrWhiteSpace(path);
    }

    private static void SetSession(MainWindow window, PreviewProject project, string path)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(MainWindow).GetField("_project", flags)?.SetValue(window, project);
        typeof(MainWindow).GetField("_currentProjectPath", flags)?.SetValue(window, path);
    }

    private static ContentControl PageHost(object host) =>
        host.GetType().GetField("_pageHost", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host) as ContentControl
        ?? throw new InvalidOperationException("PageHost single-window non disponibile.");

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
