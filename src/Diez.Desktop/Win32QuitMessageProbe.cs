using System.Runtime.InteropServices;

namespace DiezPublishingStudio;

internal static class Win32QuitMessageProbe
{
    private const uint WmQuit = 0x0012;
    private const uint PmNoRemove = 0x0000;
    private const uint PmRemove = 0x0001;

    public static bool ProbeAndConsume(out nint exitCode)
    {
        exitCode = 0;
        if (!OperatingSystem.IsWindows())
            return false;

        try
        {
            if (!PeekMessage(out var peeked, IntPtr.Zero, WmQuit, WmQuit, PmNoRemove))
            {
                SafeStartupTrace.Write("win32-wm-quit-probe | present=false");
                return false;
            }

            exitCode = peeked.WParam;
            SafeStartupTrace.Write("win32-wm-quit-probe | present=true | wParam=" + exitCode);

            if (PeekMessage(out var removed, IntPtr.Zero, WmQuit, WmQuit, PmRemove))
            {
                SafeStartupTrace.Write("win32-wm-quit-consumed | message=0x" + removed.Message.ToString("X4") + " | wParam=" + removed.WParam);
                return true;
            }

            SafeStartupTrace.Write("win32-wm-quit-consume-failed");
            return false;
        }
        catch (Exception ex)
        {
            SafeStartupTrace.Write("win32-wm-quit-probe-error | " + ex);
            return false;
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessage(
        out NativeMessage message,
        IntPtr windowHandle,
        uint messageFilterMin,
        uint messageFilterMax,
        uint removeMessage);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public IntPtr HWnd;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public NativePoint Point;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }
}
