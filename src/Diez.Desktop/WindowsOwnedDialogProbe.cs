using System.Runtime.InteropServices;
using System.Text;
using Avalonia.Controls;

namespace DiezPublishingStudio;

internal static class WindowsOwnedDialogProbe
{
    public static async Task RunAsync(MainWindow window)
    {
        if (!OperatingSystem.IsWindows()) return;

        if (!window.IsVisible) window.Show();
        await Task.Delay(120);
        window.Activate();

        var buttons = Descendants(window).OfType<Button>().ToList();
        foreach (var name in new[] { "DiezOwnedNewProject", "DiezOwnedOpenProject", "DiezOwnedImportMaterials" })
        {
            var button = buttons.FirstOrDefault(b => string.Equals(b.Name, name, StringComparison.Ordinal))
                ?? throw new InvalidOperationException("Pulsante Home owned mancante: " + name);
            if (!button.IsVisible || !button.IsEnabled || !button.IsHitTestVisible)
                throw new InvalidOperationException("Pulsante Home owned non operativo: " + name);
        }

        var platform = window.TryGetPlatformHandle()
            ?? throw new InvalidOperationException("HWND MainWindow non disponibile nel probe dialoghi.");
        if (platform.Handle == IntPtr.Zero || !string.Equals(platform.HandleDescriptor, "HWND", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Handle MainWindow inatteso: " + platform.HandleDescriptor + " / " + platform.Handle);

        var owner = platform.Handle;
        var title = "Diez owned file dialog probe";
        var closer = Task.Run(() => WaitAndCloseOwnedDialog(owner, title));

        SafeStartupTrace.Write(
            "home-file-dialog-probe | phase=before-call | owner=0x" + owner.ToInt64().ToString("X") +
            " | descriptor=" + platform.HandleDescriptor +
            " | apartment=" + Thread.CurrentThread.GetApartmentState());

        var bufferChars = 4096;
        var buffer = Marshal.AllocHGlobal(bufferChars * sizeof(char));
        try
        {
            Marshal.Copy(new byte[bufferChars * sizeof(char)], 0, buffer, bufferChars * sizeof(char));
            var data = new OpenFileNameNative
            {
                StructSize = Marshal.SizeOf<OpenFileNameNative>(),
                Owner = owner,
                Filter = "Progetto Diez (*.diez)\0*.diez\0Tutti i file (*.*)\0*.*\0\0",
                File = buffer,
                MaxFile = bufferChars,
                InitialDir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                Title = title,
                DefExt = "diez",
                Flags = OfnExplorer | OfnNoChangeDir | OfnPathMustExist | OfnFileMustExist | OfnLongNames
            };

            var ok = GetOpenFileName(ref data);
            var error = ok ? 0 : CommDlgExtendedError();
            var dialogObserved = await closer;
            if (!dialogObserved)
                throw new InvalidOperationException("Il common dialog owned non è stato osservato come finestra #32770.");
            if (ok)
                throw new InvalidOperationException("Il probe ha selezionato un file invece di chiudere il dialog automaticamente.");
            if (error != 0)
                throw new InvalidOperationException("Il common dialog è tornato con errore Win32: " + error);

            SafeStartupTrace.Write(
                "home-file-dialog-probe | phase=completed | dialogObserved=true | closeResult=cancel | error=0");
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static bool WaitAndCloseOwnedDialog(IntPtr owner, string expectedTitle)
    {
        for (var attempt = 0; attempt < 120; attempt++)
        {
            var dialog = FindOwnedDialog(owner, expectedTitle);
            if (dialog != IntPtr.Zero)
            {
                PostMessage(dialog, WmClose, IntPtr.Zero, IntPtr.Zero);
                return true;
            }
            Thread.Sleep(50);
        }
        return false;
    }

    private static IntPtr FindOwnedDialog(IntPtr owner, string expectedTitle)
    {
        var found = IntPtr.Zero;
        EnumWindows((hwnd, _) =>
        {
            if (GetWindow(hwnd, GwOwner) != owner) return true;

            var className = new StringBuilder(64);
            GetClassName(hwnd, className, className.Capacity);
            if (!string.Equals(className.ToString(), "#32770", StringComparison.Ordinal)) return true;

            var title = new StringBuilder(256);
            GetWindowText(hwnd, title, title.Capacity);
            if (!title.ToString().Contains(expectedTitle, StringComparison.Ordinal)) return true;

            found = hwnd;
            return false;
        }, IntPtr.Zero);
        return found;
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

    private const int OfnNoChangeDir = 0x00000008;
    private const int OfnPathMustExist = 0x00000800;
    private const int OfnFileMustExist = 0x00001000;
    private const int OfnLongNames = 0x00200000;
    private const int OfnExplorer = 0x00080000;
    private const uint GwOwner = 4;
    private const uint WmClose = 0x0010;

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

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

    [DllImport("comdlg32.dll")]
    private static extern int CommDlgExtendedError();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hwnd, uint command);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder className, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
}
