using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia.Controls;
using Avalonia.Layout;

namespace DiezPublishingStudio;

/// <summary>
/// Replaces only the Home create/open project entry points on Windows.
/// Avalonia's StorageProvider uses the modern shell picker; on some machines that picker can spend
/// many seconds resolving shell/Quick Access providers before returning. The classic comdlg32 path
/// is intentionally narrower and leaves all project persistence logic unchanged.
/// </summary>
internal static class FastWindowsProjectDialogUi
{
    private static readonly HashSet<MainWindow> Attached = [];
    private static string? _lastDirectory;

    public static void Attach(MainWindow window)
    {
        if (!OperatingSystem.IsWindows() || !Attached.Add(window)) return;
        if (!TryProjectRow(window, out var row))
            throw new InvalidOperationException("Riga comandi progetto non disponibile per i dialoghi rapidi Windows.");

        var oldNew = row.Children.OfType<Button>().FirstOrDefault(b =>
            string.Equals(b.Content?.ToString(), "Nuovo progetto", StringComparison.OrdinalIgnoreCase));
        var oldOpen = row.Children.OfType<Button>().FirstOrDefault(b =>
            string.Equals(b.Content?.ToString(), "Apri progetto", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(b.Content?.ToString(), "Apri .diez", StringComparison.OrdinalIgnoreCase));
        if (oldNew is null || oldOpen is null)
            throw new InvalidOperationException("Pulsanti Nuovo/Apri progetto non disponibili per la sostituzione rapida.");

        var fastNew = new Button
        {
            Name = "DiezFastNewProject",
            Content = "Nuovo progetto",
            Width = oldNew.Width,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        var fastOpen = new Button
        {
            Name = "DiezFastOpenProject",
            Content = "Apri progetto",
            Width = oldOpen.Width,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };

        ToolTip.SetTip(fastNew, "Crea un progetto .diez usando il dialogo file classico e rapido di Windows.");
        ToolTip.SetTip(fastOpen, "Apri un progetto .diez usando il dialogo file classico e rapido di Windows.");
        fastNew.Click += async (_, _) => await CreateProjectAsync(window, fastNew, fastOpen);
        fastOpen.Click += async (_, _) => await OpenProjectAsync(window, fastNew, fastOpen);

        var newIndex = row.Children.IndexOf(oldNew);
        row.Children.Insert(Math.Max(0, newIndex), fastNew);
        var openIndex = row.Children.IndexOf(oldOpen);
        row.Children.Insert(Math.Max(0, openIndex), fastOpen);

        DisableLegacyButton(oldNew);
        DisableLegacyButton(oldOpen);

        window.Closed += (_, _) => Attached.Remove(window);
        SafeStartupTrace.Write("fast-project-dialog | attached=true | provider=comdlg32-classic | legacy-home-buttons-disabled=true");
    }

    private static async Task CreateProjectAsync(MainWindow window, Button newButton, Button openButton)
    {
        SetBusy(newButton, openButton, true);
        try
        {
            var dialogWatch = Stopwatch.StartNew();
            var selection = SelectProjectPath(save: true, "NuovoProgetto.diez");
            dialogWatch.Stop();
            SafeStartupTrace.Write(
                "fast-project-dialog | operation=create | phase=dialog-returned" +
                " | elapsedMs=" + dialogWatch.ElapsedMilliseconds +
                " | selected=" + (selection.Path is not null) +
                " | error=" + selection.ErrorCode);
            if (selection.Path is null) return;

            var totalWatch = Stopwatch.StartNew();
            var path = EnsureDiezExtension(selection.Path);
            _lastDirectory = Path.GetDirectoryName(path);
            var project = ProjectFileStore.Create(Path.GetFileNameWithoutExtension(path));
            await ProjectFileStore.SaveAsync(path, project);
            SetSession(window, project, path);
            RefreshViews(window);
            SetStatus(window, $"Creato pacchetto .diez: {path}");
            RefreshNativeEntry(window);
            totalWatch.Stop();
            SafeStartupTrace.Write(
                "fast-project-dialog | operation=create | phase=completed | postDialogMs=" + totalWatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            SetStatus(window, "Errore creazione: " + ex.GetBaseException().Message);
            SafeStartupTrace.Write("fast-project-dialog | operation=create | error=" + ex);
            CrashDiagnostics.Error("fast-create-project", ex);
        }
        finally { SetBusy(newButton, openButton, false); }
    }

    private static async Task OpenProjectAsync(MainWindow window, Button newButton, Button openButton)
    {
        SetBusy(newButton, openButton, true);
        try
        {
            var dialogWatch = Stopwatch.StartNew();
            var selection = SelectProjectPath(save: false, null);
            dialogWatch.Stop();
            SafeStartupTrace.Write(
                "fast-project-dialog | operation=open | phase=dialog-returned" +
                " | elapsedMs=" + dialogWatch.ElapsedMilliseconds +
                " | selected=" + (selection.Path is not null) +
                " | error=" + selection.ErrorCode);
            if (selection.Path is null) return;

            var totalWatch = Stopwatch.StartNew();
            var phaseWatch = Stopwatch.StartNew();
            var path = selection.Path;
            _lastDirectory = Path.GetDirectoryName(path);
            var wasPackage = ProjectFileStore.IsPackageFile(path);
            phaseWatch.Stop();
            SafeStartupTrace.Write("project-load | phase=package-check | elapsedMs=" + phaseWatch.ElapsedMilliseconds);

            phaseWatch.Restart();
            var project = await ProjectFileStore.LoadAsync(path);
            phaseWatch.Stop();
            SafeStartupTrace.Write("project-load | phase=load | elapsedMs=" + phaseWatch.ElapsedMilliseconds);

            phaseWatch.Restart();
            ConsistencyEngine.Rebuild(project);
            phaseWatch.Stop();
            SafeStartupTrace.Write("project-load | phase=consistency-rebuild | elapsedMs=" + phaseWatch.ElapsedMilliseconds);

            SetSession(window, project, path);
            phaseWatch.Restart();
            RefreshViews(window);
            phaseWatch.Stop();
            SafeStartupTrace.Write("project-load | phase=refresh-ui | elapsedMs=" + phaseWatch.ElapsedMilliseconds);

            SetStatus(window, wasPackage
                ? $"Aperto: {project.Name} · {project.Materials.Count} materiali · {project.ContentNodes.Count} contenuti"
                : $"Aperto progetto legacy: {project.Name}. Al prossimo Salva verrà convertito nel pacchetto .diez corrente.");
            RefreshNativeEntry(window);
            totalWatch.Stop();
            SafeStartupTrace.Write("project-load | phase=completed | postDialogMs=" + totalWatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            SetStatus(window, "Errore apertura: " + ex.GetBaseException().Message);
            SafeStartupTrace.Write("fast-project-dialog | operation=open | error=" + ex);
            CrashDiagnostics.Error("fast-open-project", ex);
        }
        finally { SetBusy(newButton, openButton, false); }
    }

    private static DialogSelection SelectProjectPath(bool save, string? defaultFileName)
    {
        var buffer = new StringBuilder(4096);
        if (!string.IsNullOrWhiteSpace(defaultFileName)) buffer.Append(defaultFileName);

        var initialDirectory = _lastDirectory;
        if (string.IsNullOrWhiteSpace(initialDirectory) || !Directory.Exists(initialDirectory))
            initialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        var data = new OpenFileName
        {
            StructSize = Marshal.SizeOf<OpenFileName>(),
            Filter = "Progetto Diez (*.diez)\0*.diez\0Tutti i file (*.*)\0*.*\0\0",
            File = buffer,
            MaxFile = buffer.Capacity,
            InitialDir = initialDirectory,
            Title = save ? "Crea progetto Diez" : "Apri progetto Diez",
            DefExt = "diez",
            Flags = OfnExplorer | OfnNoChangeDir | OfnPathMustExist |
                    (save ? OfnOverwritePrompt : OfnFileMustExist)
        };

        var ok = save ? GetSaveFileName(ref data) : GetOpenFileName(ref data);
        if (ok) return new DialogSelection(buffer.ToString(), 0);
        return new DialogSelection(null, CommDlgExtendedError());
    }

    private static string EnsureDiezExtension(string path) =>
        string.Equals(Path.GetExtension(path), ".diez", StringComparison.OrdinalIgnoreCase)
            ? path
            : path + ".diez";

    private static void SetBusy(Button newButton, Button openButton, bool busy)
    {
        newButton.IsEnabled = !busy;
        openButton.IsEnabled = !busy;
    }

    private static void DisableLegacyButton(Button button)
    {
        button.IsVisible = false;
        button.IsEnabled = false;
        button.IsHitTestVisible = false;
    }

    private static bool TryProjectRow(MainWindow window, out StackPanel row)
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

    private static void SetSession(MainWindow window, PreviewProject project, string path)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(MainWindow).GetField("_project", flags)?.SetValue(window, project);
        typeof(MainWindow).GetField("_currentProjectPath", flags)?.SetValue(window, path);
    }

    private static void RefreshViews(MainWindow window)
    {
        var method = typeof(MainWindow).GetMethod("RefreshViews", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(nameof(MainWindow), "RefreshViews");
        method.Invoke(window, null);
    }

    private static void SetStatus(MainWindow window, string text)
    {
        var block = typeof(MainWindow).GetField("_status", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(window) as TextBlock;
        if (block is not null) block.Text = text;
    }

    private static void RefreshNativeEntry(MainWindow window)
    {
        var entry = Descendants(window).OfType<Button>().FirstOrDefault(b =>
            string.Equals(b.Name, SingleWindowNativeEntryBridgeUi.NativeEntryName, StringComparison.Ordinal));
        if (entry is null) return;
        entry.Content = SingleWindowProjectResumeUi.HasActiveProject(window) ? "Avanti · Tipo libro" : "Percorso libro";
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

    private readonly record struct DialogSelection(string? Path, int ErrorCode);

    private const int OfnOverwritePrompt = 0x00000002;
    private const int OfnNoChangeDir = 0x00000008;
    private const int OfnPathMustExist = 0x00000800;
    private const int OfnFileMustExist = 0x00001000;
    private const int OfnExplorer = 0x00080000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OpenFileName
    {
        public int StructSize;
        public IntPtr Owner;
        public IntPtr Instance;
        [MarshalAs(UnmanagedType.LPWStr)] public string Filter;
        [MarshalAs(UnmanagedType.LPWStr)] public string? CustomFilter;
        public int MaxCustomFilter;
        public int FilterIndex;
        [MarshalAs(UnmanagedType.LPWStr)] public StringBuilder File;
        public int MaxFile;
        [MarshalAs(UnmanagedType.LPWStr)] public string? FileTitle;
        public int MaxFileTitle;
        [MarshalAs(UnmanagedType.LPWStr)] public string? InitialDir;
        [MarshalAs(UnmanagedType.LPWStr)] public string Title;
        public int Flags;
        public short FileOffset;
        public short FileExtension;
        [MarshalAs(UnmanagedType.LPWStr)] public string? DefExt;
        public IntPtr CustData;
        public IntPtr Hook;
        [MarshalAs(UnmanagedType.LPWStr)] public string? TemplateName;
        public IntPtr Reserved;
        public int Reserved2;
        public int FlagsEx;
    }

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetOpenFileName(ref OpenFileName data);

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSaveFileName(ref OpenFileName data);

    [DllImport("comdlg32.dll")]
    private static extern int CommDlgExtendedError();
}
