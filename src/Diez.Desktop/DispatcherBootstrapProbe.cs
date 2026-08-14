using System.Reflection;
using Avalonia;
using Avalonia.Threading;

namespace DiezPublishingStudio;

internal static class DispatcherBootstrapProbe
{
    private static readonly FieldInfo? UiThreadField = typeof(Dispatcher).GetField(
        "s_uiThread",
        BindingFlags.Static | BindingFlags.NonPublic);

    public static void TraceCachedState(string stage)
    {
        try
        {
            var cached = UiThreadField?.GetValue(null) as Dispatcher;
            SafeStartupTrace.Write(
                "dispatcher-bootstrap | stage=" + stage +
                " | cached=" + (cached is not null) +
                (cached is null ? string.Empty : " | cachedSupportsRunLoops=" + cached.SupportsRunLoops));
        }
        catch (Exception ex)
        {
            SafeStartupTrace.Write("dispatcher-bootstrap | stage=" + stage + " | inspect-error=" + ex.GetType().Name + ": " + ex.Message);
        }
    }

    public static void PinAfterPlatformServicesSetup()
    {
        TraceCachedState("after-platform-before-getter");

        try
        {
            var platformImpl = AvaloniaLocator.Current.GetService<IDispatcherImpl>();
            SafeStartupTrace.Write(
                "dispatcher-bootstrap | platformImpl=" + (platformImpl?.GetType().FullName ?? "<null>") +
                " | assembly=" + (platformImpl?.GetType().Assembly.GetName().Version?.ToString() ?? "<null>") +
                " | controlled=" + (platformImpl is IControlledDispatcherImpl));

            var dispatcher = Dispatcher.UIThread;
            SafeStartupTrace.Write(
                "dispatcher-bootstrap | stage=after-platform-after-getter" +
                " | supportsRunLoops=" + dispatcher.SupportsRunLoops +
                " | checkAccess=" + dispatcher.CheckAccess());
        }
        catch (Exception ex)
        {
            SafeStartupTrace.Write("dispatcher-bootstrap | pin-error=" + ex);
        }
    }
}
