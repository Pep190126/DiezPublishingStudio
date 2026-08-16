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
        TraceCachedState("after-platform-before-repair");

        try
        {
            var cached = UiThreadField?.GetValue(null) as Dispatcher;
            var cachedImpl = cached is null ? null : ImplField?.GetValue(cached);
            var cachedImplName = cachedImpl?.GetType().FullName ?? "<null>";

            SafeStartupTrace.Write(
                "dispatcher-bootstrap | stage=repair-check" +
                " | impl=" + cachedImplName +
                " | supportsRunLoops=" + (cached?.SupportsRunLoops.ToString() ?? "<null>"));

            // Avalonia 11.3.18 caches Dispatcher.UIThread once. If it is first touched before the Win32
            // IDispatcherImpl binding exists, CreateUIThreadDispatcher permanently captures NullDispatcherImpl.
            // At this callback platform services are fully initialized but App has not been created yet, so it is
            // safe to discard only that premature null dispatcher and let the public getter recreate it from the
            // now-registered Win32DispatcherImpl.
            if (cached is not null &&
                !cached.SupportsRunLoops &&
                string.Equals(cachedImplName, "Avalonia.Threading.NullDispatcherImpl", StringComparison.Ordinal))
            {
                if (UiThreadField is null)
                    throw new InvalidOperationException("Avalonia Dispatcher.s_uiThread field not found.");

                UiThreadField.SetValue(null, null);
                SafeStartupTrace.Write("dispatcher-bootstrap | repair=cleared-premature-null-dispatcher");
            }

            var dispatcher = Dispatcher.UIThread;
            var impl = ImplField?.GetValue(dispatcher);

            SafeStartupTrace.Write(
                "dispatcher-bootstrap | stage=after-platform-after-repair" +
                " | impl=" + (impl?.GetType().FullName ?? "<null>") +
                " | assembly=" + (impl?.GetType().Assembly.GetName().Version?.ToString() ?? "<null>") +
                " | controlled=" + (impl is IControlledDispatcherImpl) +
                " | supportsRunLoops=" + dispatcher.SupportsRunLoops +
                " | checkAccess=" + dispatcher.CheckAccess());

            if (!dispatcher.SupportsRunLoops)
                throw new PlatformNotSupportedException("Avalonia UI dispatcher still does not support run loops after Win32 bootstrap repair.");
        }
        catch (Exception ex)
        {
            SafeStartupTrace.Write("dispatcher-bootstrap | repair-error=" + ex);
        }
    }
}
