using System.Reflection;
using Avalonia.Controls;

namespace DiezPublishingStudio;

/// <summary>
/// Headless-CI-only bridge: hosted test dispatchers do not guarantee the same Loaded/Background ordering as
/// the Windows desktop lifetime. Refresh the Custom-style owner synchronously whenever the native page changes.
/// </summary>
internal static class HeadlessCustomStyleLifecycleBridge
{
    private static readonly HashSet<MainWindow> Attached = [];

    public static void Attach(MainWindow window)
    {
        if (!Attached.Add(window)) return;
        var host = SingleWindowEntryPointUi.GetHost(window);
        var pageHost = host.GetType().GetField("_pageHost", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host) as ContentControl;
        if (pageHost is null) return;

        pageHost.PropertyChanged += (_, e) =>
        {
            if (e.Property == ContentControl.ContentProperty)
                SingleWindowCustomStyleConsentUi.Refresh(window);
        };
        window.Closed += (_, _) => Attached.Remove(window);
        SingleWindowCustomStyleConsentUi.Refresh(window);
    }
}
