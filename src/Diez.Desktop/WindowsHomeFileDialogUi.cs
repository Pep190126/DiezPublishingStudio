using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Layout;

namespace DiezPublishingStudio;

/// <summary>
/// Single Windows owner for the three Home file actions.  The previous implementation mixed
/// Avalonia StorageProvider with an ownerless OPENFILENAME call; on affected Win32 machines the
/// buttons could be clicked without a visible picker.  These actions now share the same owned
/// Win32 common-dialog path and always log before entering user32/comdlg32.
/// </summary>
internal static class WindowsHomeFileDialogUi
{
    private static readonly HashSet<MainWindow> Attached = [];
    private static string? _lastDirectory;

    public static void Attach(MainWindow window)
    {
        if (!OperatingSystem.IsWindows() || !Attached.Add(window)) return;
        if (!TryProjectRow(window, out var row))
            throw new InvalidOperationException("Riga comandi progetto non disponibile per i dialoghi Windows owned.");

        RemoveHomeFileButtons(row);

        var create = HomeButton("DiezOwnedNewProject", "Nuovo progetto");
        var open = HomeButton("DiezOwnedOpenProject", "Apri progetto");
        var materials = HomeButton("DiezOwnedImportMaterials", "Aggiungi materiali");

        create.Click += async (_, _) => await CreateProjectAsync(window, create, open, materials);
        open.Click += async (_, _) => await OpenProjectAsync(window, create, open, materials);
        materials.Click += async (_, _) => await ImportMaterialsAsync(window, create, open, materials);

        row.Children.Insert(0, create);
        row.Children.Insert(1, open);
        row.Children.Insert(2, materials);

        window.Closed += (_, _) => Attached.Remove(window);
        SafeStartupTrace.Write(
            "home-file-dialog | attached=true | provider=comdlg32-owned" +
            " | apartment=" + Thread.CurrentThread.GetApartmentState() +
            " | hwnd=" + HandleText(window));
    }

    private static async Task CreateProjectAsync(MainWindow window, params Button[] buttons)
    {
        SetBusy(buttons, true);
        try
        {
            var selected = ShowDialog(window, new NativeDialogRequest(
                Save: true,
                AllowMultiple: false,
                Title: "Crea progetto Diez",
                Filter: "Progetto Diez (*.diez)\0*.diez\0Tutti i file (*.*)\0*.*\0\0",
                DefaultFileName: "NuovoProgetto.diez",
                DefaultExtension: "diez"));
            if (selected.Count == 0) return;

            var watch = Stopwatch.StartNew();
            var path = EnsureDiezExtension(selected[0]);
            _lastDirectory = Path.GetDirectoryName(path);
            var project = ProjectFileStore.Create(Path.GetFileNameWithoutExtension(path));
            await ProjectFileStore.SaveAsync(path, project);
            SetSession(window, project, path);
            RefreshViews(window);
            SetStatus(window, $"Creato pacchetto .diez: {path}");
            RefreshNativeEntry(window);
            watch.Stop();
            SafeStartupTrace.Write("home-file-dialog | operation=create | phase=completed | postDialogMs=" + watch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            SetStatus(window, "Errore creazione: " + ex.GetBaseException().Message);
            SafeStartupTrace.Write("home-file-dialog | operation=create | error=" + ex);
            CrashDiagnostics.Error("home-create-project", ex);
        }
        finally { SetBusy(buttons, false); }
    }

    private static async Task OpenProjectAsync(MainWindow window, params Button[] buttons)
    {
        SetBusy(buttons, true);
        try
        {
            var selected = ShowDialog(window, new NativeDialogRequest(
                Save: false,
                AllowMultiple: false,
                Title: "Apri progetto Diez",
                Filter: "Progetto Diez (*.diez)\0*.diez\0Tutti i file (*.*)\0*.*\0\0",
                DefaultFileName: null,
                DefaultExtension: "diez"));
            if (selected.Count == 0) return;

            var total = Stopwatch.StartNew();
            var phase = Stopwatch.StartNew();
            var path = selected[0];
            _lastDirectory = Path.GetDirectoryName(path);
            var wasPackage = ProjectFileStore.IsPackageFile(path);
            phase.Stop();
            SafeStartupTrace.Write("project-load | phase=package-check | elapsedMs=" + phase.ElapsedMilliseconds);

            phase.Restart();
            var project = await ProjectFileStore.LoadAsync(path);
            phase.Stop();
            SafeStartupTrace.Write("project-load | phase=load | elapsedMs=" + phase.ElapsedMilliseconds);

            phase.Restart();
            ConsistencyEngine.Rebuild(project);
            phase.Stop();
            SafeStartupTrace.Write("project-load | phase=consistency-rebuild | elapsedMs=" + phase.ElapsedMilliseconds);

            SetSession(window, project, path);
            phase.Restart();
            RefreshViews(window);
            phase.Stop();
            SafeStartupTrace.Write("project-load | phase=refresh-ui | elapsedMs=" + phase.ElapsedMilliseconds);

            SetStatus(window, wasPackage
                ? $"Aperto: {project.Name} · {project.Materials.Count} materiali · {project.ContentNodes.Count} contenuti"
                : $"Aperto progetto legacy: {project.Name}. Al prossimo Salva verrà convertito nel pacchetto .diez corrente.");
            RefreshNativeEntry(window);
            total.Stop();
            SafeStartupTrace.Write("project-load | phase=completed | postDialogMs=" + total.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            SetStatus(window, "Errore apertura: " + ex.GetBaseException().Message);
            SafeStartupTrace.Write("home-file-dialog | operation=open | error=" + ex);
            CrashDiagnostics.Error("home-open-project", ex);
        }
        finally { SetBusy(buttons, false); }
    }

    private static async Task ImportMaterialsAsync(MainWindow window, params Button[] buttons)
    {
        if (!TrySession(window, out var project, out var projectPath))
        {
            SetStatus(window, "Prima crea o apri un progetto .diez.");
            SafeStartupTrace.Write("home-file-dialog | operation=materials | skipped=no-active-project");
            return;
        }

        SetBusy(buttons, true);
        try
        {
            var files = ShowDialog(window, new NativeDialogRequest(
                Save: false,
                AllowMultiple: true,
                Title: "Aggiungi materiali",
                Filter: "Materiali supportati\0*.txt;*.md;*.csv;*.xlsx;*.docx;*.odt;*.rtf;*.pdf;*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp\0Documenti\0*.txt;*.md;*.docx;*.odt;*.rtf;*.pdf\0Tabelle\0*.csv;*.xlsx\0Immagini\0*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp\0Tutti i file (*.*)\0*.*\0\0",
                DefaultFileName: null,
                DefaultExtension: null));
            if (files.Count == 0) return;

            var imported = 0;
            var duplicates = 0;
            var editorialNodes = 0;
            var entities = 0;
            var relations = 0;
            var errors = new List<string>();
            MaterialEntry? lastImported = null;
            SetStatus(window, $"Analisi di {files.Count} materiali in corso...");

            foreach (var sourcePath in files)
            {
                try
                {
                    var material = await MaterialImporter.ImportAsync(sourcePath);
                    if (project.Materials.Any(existing => string.Equals(existing.Sha256, material.Sha256, StringComparison.OrdinalIgnoreCase)))
                    {
                        duplicates++;
                        continue;
                    }

                    material.ExtractedText = await EditorialTextExtractor.ExtractAsync(sourcePath);
                    project.Materials.Add(material);
                    var nodes = ContentStructureAnalyzer.Analyze(material);
                    project.ContentNodes.AddRange(nodes);
                    var graph = ContentGraphEngine.Analyze(project, material, nodes);
                    editorialNodes += nodes.Count;
                    entities += graph.EntitiesCreated;
                    relations += graph.RelationsCreated;
                    imported++;
                    lastImported = material;
                }
                catch (Exception ex)
                {
                    errors.Add(Path.GetFileName(sourcePath) + ": " + ex.GetBaseException().Message);
                }
            }

            ConsistencyEngine.Rebuild(project);
            if (imported > 0) await ProjectFileStore.SaveAsync(projectPath, project);
            RefreshViews(window);
            SelectMaterial(window, project, lastImported);

            var message = $"Importati {imported} materiali · {editorialNodes} elementi · {entities} nuove entità · {relations} nuove relazioni";
            if (duplicates > 0) message += $" · {duplicates} duplicati ignorati";
            if (errors.Count > 0) message += $" · {errors.Count} errori: {string.Join("; ", errors.Take(2))}";
            else if (imported > 0) message += " · originali incorporati nel .diez";
            SetStatus(window, message);
            SafeStartupTrace.Write(
                "home-file-dialog | operation=materials | phase=completed" +
                " | selected=" + files.Count + " | imported=" + imported + " | duplicates=" + duplicates + " | errors=" + errors.Count);
        }
        catch (Exception ex)
        {
            SetStatus(window, "Errore importazione materiali: " + ex.GetBaseException().Message);
            SafeStartupTrace.Write("home-file-dialog | operation=materials | error=" + ex);
            CrashDiagnostics.Error("home-import-materials", ex);
        }
        finally { SetBusy(buttons, false); }
    }

    private static IReadOnlyList<string> ShowDialog(MainWindow window, NativeDialogRequest request)
    {
        window.Activate();
        var platformHandle = window.TryGetPlatformHandle();
        var owner = platformHandle?.Handle ?? IntPtr.Zero;
        var descriptor = platformHandle?.HandleDescriptor ?? "<none>";
        var initialDirectory = _lastDirectory;
        if (string.IsNullOrWhiteSpace(initialDirectory) || !Directory.Exists(initialDirectory))
            initialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        var capacity = request.AllowMultiple ? 32768 : 4096;
        var bytes = checked(capacity * sizeof(char));
        var fileBuffer = Marshal.AllocHGlobal(bytes);
        try
        {
            var zeros = new byte[bytes];
            Marshal.Copy(zeros, 0, fileBuffer, zeros.Length);
            if (!string.IsNullOrWhiteSpace(request.DefaultFileName))
            {
                var chars = (request.DefaultFileName + "\0").ToCharArray();
                Marshal.Copy(chars, 0, fileBuffer, Math.Min(chars.Length, capacity));
            }

            var data = new OpenFileNameNative
            {
                StructSize = Marshal.SizeOf<OpenFileNameNative>(),
                Owner = owner,
                Filter = request.Filter,
                File = fileBuffer,
                MaxFile = capacity,
                InitialDir = initialDirectory,
                Title = request.Title,
                DefExt = request.DefaultExtension,
                Flags = OfnExplorer | OfnNoChangeDir | OfnPathMustExist | OfnLongNames |
                        (request.Save ? OfnOverwritePrompt : OfnFileMustExist) |
                        (request.AllowMultiple ? OfnAllowMultiSelect : 0)
            };

            var watch = Stopwatch.StartNew();
            SafeStartupTrace.Write(
                "home-file-dialog | operation=" + OperationName(request) +
                " | phase=before-call | owner=0x" + owner.ToInt64().ToString("X") +
                " | descriptor=" + descriptor +
                " | apartment=" + Thread.CurrentThread.GetApartmentState() +
                " | allowMultiple=" + request.AllowMultiple);

            var ok = request.Save ? GetSaveFileName(ref data) : GetOpenFileName(ref data);
            var error = ok ? 0 : CommDlgExtendedError();
            watch.Stop();
            window.Activate();

            var result = ok ? ParseSelection(fileBuffer, capacity, request.AllowMultiple) : [];
            if (result.Count > 0)
                _lastDirectory = request.AllowMultiple && result.Count > 1
                    ? Path.GetDirectoryName(result[0])
                    : Path.GetDirectoryName(result[0]);

            SafeStartupTrace.Write(
                "home-file-dialog | operation=" + OperationName(request) +
                " | phase=returned | elapsedMs=" + watch.ElapsedMilliseconds +
                " | selected=" + result.Count + " | error=" + error +
                " | windowActive=" + window.IsActive);
            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(fileBuffer);
        }
    }

    private static IReadOnlyList<string> ParseSelection(IntPtr buffer, int capacity, bool allowMultiple)
    {
        var raw = Marshal.PtrToStringUni(buffer, capacity) ?? string.Empty;
        var parts = raw.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return [];
        if (!allowMultiple || parts.Length == 1) return [parts[0]];

        var directory = parts[0];
        var result = new List<string>(parts.Length - 1);
        for (var i = 1; i < parts.Length; i++)
            result.Add(Path.Combine(directory, parts[i]));
        return result;
    }

    private static string OperationName(NativeDialogRequest request) =>
        request.Save ? "create" : request.AllowMultiple ? "materials" : "open";

    private static string EnsureDiezExtension(string path) =>
        string.Equals(Path.GetExtension(path), ".diez", StringComparison.OrdinalIgnoreCase) ? path : path + ".diez";

    private static Button HomeButton(string name, string text) => new()
    {
        Name = name,
        Content = text,
        Width = 150,
        HorizontalContentAlignment = HorizontalAlignment.Center,
        IsEnabled = true,
        IsHitTestVisible = true
    };

    private static void RemoveHomeFileButtons(StackPanel row)
    {
        var remove = row.Children.OfType<Button>().Where(button =>
        {
            var text = button.Content?.ToString() ?? string.Empty;
            return string.Equals(button.Name, "DiezFastNewProject", StringComparison.Ordinal) ||
                   string.Equals(button.Name, "DiezFastOpenProject", StringComparison.Ordinal) ||
                   string.Equals(button.Name, "DiezOwnedNewProject", StringComparison.Ordinal) ||
                   string.Equals(button.Name, "DiezOwnedOpenProject", StringComparison.Ordinal) ||
                   string.Equals(button.Name, "DiezOwnedImportMaterials", StringComparison.Ordinal) ||
                   string.Equals(text, "Nuovo progetto", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(text, "Apri progetto", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(text, "Apri .diez", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(text, "Aggiungi materiali", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(text, "Importa materiali", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(text, "Carica materiali", StringComparison.OrdinalIgnoreCase);
        }).ToList();

        foreach (var button in remove) row.Children.Remove(button);
    }

    private static bool TryProjectRow(MainWindow window, out StackPanel row)
    {
        row = null!;
        if (window.Content is not Border border || border.Child is not Grid desktop) return false;
        var header = desktop.Children.OfType<Grid>().FirstOrDefault(c => Grid.GetRow(c) == 0);
        if (header is null) return false;
        row = header.Children.OfType<StackPanel>().FirstOrDefault(p =>
            p.Orientation == Orientation.Horizontal &&
            p.Children.OfType<Button>().Any(b =>
                string.Equals(b.Content?.ToString(), "Nuovo progetto", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(b.Name, "DiezFastNewProject", StringComparison.Ordinal)))!;
        return row is not null;
    }

    private static void SetBusy(IEnumerable<Button> buttons, bool busy)
    {
        foreach (var button in buttons) button.IsEnabled = !busy;
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

    private static void RefreshViews(MainWindow window)
    {
        var method = typeof(MainWindow).GetMethod("RefreshViews", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(nameof(MainWindow), "RefreshViews");
        method.Invoke(window, null);
    }

    private static void SelectMaterial(MainWindow window, PreviewProject project, MaterialEntry? material)
    {
        if (material is null) return;
        var list = typeof(MainWindow).GetField("_materialsList", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(window) as ListBox;
        if (list is not null) list.SelectedIndex = project.Materials.IndexOf(material);
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
        if (entry is not null)
            entry.Content = SingleWindowProjectResumeUi.HasActiveProject(window) ? "Avanti · Tipo libro" : "Percorso libro";
    }

    private static string HandleText(MainWindow window)
    {
        var handle = window.TryGetPlatformHandle();
        return handle is null ? "<none>" : "0x" + handle.Handle.ToInt64().ToString("X") + ":" + handle.HandleDescriptor;
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

    private readonly record struct NativeDialogRequest(
        bool Save,
        bool AllowMultiple,
        string Title,
        string Filter,
        string? DefaultFileName,
        string? DefaultExtension);

    private const int OfnOverwritePrompt = 0x00000002;
    private const int OfnNoChangeDir = 0x00000008;
    private const int OfnAllowMultiSelect = 0x00000200;
    private const int OfnPathMustExist = 0x00000800;
    private const int OfnFileMustExist = 0x00001000;
    private const int OfnLongNames = 0x00200000;
    private const int OfnExplorer = 0x00080000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OpenFileNameNative
    {
        public int StructSize;
        public IntPtr Owner;
        public IntPtr Instance;
        [MarshalAs(UnmanagedType.LPWStr)] public string Filter;
        public IntPtr CustomFilter;
        public int MaxCustomFilter;
        public int FilterIndex;
        public IntPtr File;
        public int MaxFile;
        public IntPtr FileTitle;
        public int MaxFileTitle;
        [MarshalAs(UnmanagedType.LPWStr)] public string? InitialDir;
        [MarshalAs(UnmanagedType.LPWStr)] public string Title;
        public int Flags;
        public short FileOffset;
        public short FileExtension;
        [MarshalAs(UnmanagedType.LPWStr)] public string? DefExt;
        public IntPtr CustData;
        public IntPtr Hook;
        public IntPtr TemplateName;
        public IntPtr Reserved;
        public int Reserved2;
        public int FlagsEx;
    }

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetOpenFileNameW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetOpenFileName(ref OpenFileNameNative data);

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetSaveFileNameW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSaveFileName(ref OpenFileNameNative data);

    [DllImport("comdlg32.dll")]
    private static extern int CommDlgExtendedError();
}
