using System.Reflection;
using Avalonia.Threading;

namespace DiezPublishingStudio;

internal static class DispatcherBootstrapProbe
{
    private static readonly FieldInfo? UiThreadField = typeof(Dispatcher).GetField(
        "s_uiThread",
        BindingFlags.Static | BindingFlags.NonPublic);

    private static readonly FieldInfo? ImplField = typeof(Dispatcher).GetField(
        "_impl",
        BindingFlags.Instance | BindingFlags.NonPublic);

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
            var dispatcher = Dispatcher.UIThread;
            var impl = ImplField?.GetValue(dispatcher);

            SafeStartupTrace.Write(
                "dispatcher-bootstrap | stage=after-platform-after-getter" +
                " | impl=" + (impl?.GetType().FullName ?? "<null>") +
                " | assembly=" + (impl?.GetType().Assembly.GetName().Version?.ToString() ?? "<null>") +
                " | controlled=" + (impl is IControlledDispatcherImpl) +
                " | supportsRunLoops=" + dispatcher.SupportsRunLoops +
                " | checkAccess=" + dispatcher.CheckAccess());
        }
        catch (Exception ex)
        {
            SafeStartupTrace.Write("dispatcher-bootstrap | pin-error=" + ex);
        }
    }
}
