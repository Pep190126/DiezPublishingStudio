using System.Reflection;
using Avalonia.Controls;
using Avalonia.Threading;

namespace DiezPublishingStudio;

/// <summary>
/// Replaces the historical ContentControl.ContentProperty page-change lifecycle when the workflow page host
/// keeps one stable ContentPresenter child. Page-dependent modules are refreshed explicitly in the same order
/// in which they are attached at startup, while the real page controls live below the stable host Grid.
/// </summary>
internal static class StablePageObserverLifecycleUi
{
    private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void Refresh(MainWindow window, ContentControl pageHost, string reason)
    {
        RefreshPass(window, pageHost, reason + "-immediate");
        Dispatcher.UIThread.Post(
            () => RefreshPass(window, pageHost, reason + "-loaded"),
            DispatcherPriority.Loaded);
        Dispatcher.UIThread.Post(
            () => RefreshPass(window, pageHost, reason + "-background"),
            DispatcherPriority.Background);
    }

    private static void RefreshPass(MainWindow window, ContentControl pageHost, string phase)
    {
        if (pageHost.Content is not Control) return;

        Step("image-specs", () => InvokePrivate(
            typeof(SingleWindowImageSpecsQuantityOnlyUi),
            "EnsureQuantityPage",
            window,
            pageHost));
        Step("custom-dimensions", () => SingleWindowCustomDimensionsUi.EnsureCurrentPage(window));
        Step("persistent-image-count", () => SingleWindowPersistentImageCountUi.Refresh(window));
        Step("coloring-hard-profile", () => SingleWindowColoringStylePolicyUi.Refresh(window));
        Step("multi-subject-label", () => SingleWindowMultiSubjectLabelUi.Refresh(window));
        Step("structured-scenes", () => SingleWindowStructuredSceneUi.Refresh(window));
        Step("custom-style-consent", () => SingleWindowCustomStyleConsentUi.Refresh(window));
        Step("prompt-target-ai", () => SingleWindowPromptTargetAiUi.EnsureCurrentPage(window));
        Step("ai-image-context", () => SingleWindowAiImageContextUi.EnsureCurrentPage(window));
        Step("quantity-configure", () => InvokePrivate(
            typeof(SingleWindowQuantityUsabilityUi),
            "ConfigureCurrentPage",
            window,
            pageHost,
            "stable-page-observers-" + phase));

        var host = SingleWindowEntryPointUi.GetHost(window);
        var previewHost = host.GetType().GetField("_previewHost", PrivateInstance)?.GetValue(host) as ContentControl;
        if (previewHost is not null)
        {
            Step("quantity-preview", () =>
            {
                if (InvokePrivate(
                        typeof(SingleWindowQuantityUsabilityUi),
                        "LoadPreviewAsync",
                        window,
                        pageHost,
                        previewHost) is Task task)
                    _ = ObserveAsync(task, phase);
            });
        }

        Step("dynamic-layout", () => InvokePrivate(
            typeof(SingleWindowDynamicLayoutPumpUi),
            "WireCurrentPage",
            window,
            pageHost));
        Step("safe-image-export", () => SingleWindowSafeImageContextExportUi.EnsureCurrentPage(window));
        Step("vision-validation", () => SingleWindowVisionValidationUi.EnsureCurrentPage(window));
        Step("project-resume-label", () => SingleWindowProjectResumeUi.RefreshEntry(window));

        var layoutExecuted = AvaloniaLayoutPumpUi.Execute(window, "stable-page-observers-" + phase);
        SafeStartupTrace.Write(
            "stable-page-observers | phase=" + phase +
            " | layout=" + layoutExecuted +
            " | contentType=" + pageHost.Content.GetType().Name);
    }

    private static object? InvokePrivate(Type owner, string method, params object?[] args) =>
        owner.GetMethod(method, PrivateStatic)?.Invoke(null, args);

    private static void Step(string name, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            SafeStartupTrace.Write(
                "stable-page-observers | step=" + name +
                " | error=" + ex.GetBaseException().Message.Replace('|', '/'));
        }
    }

    private static async Task ObserveAsync(Task task, string phase)
    {
        try
        {
            await task;
        }
        catch (Exception ex)
        {
            SafeStartupTrace.Write(
                "stable-page-observers | step=quantity-preview" +
                " | phase=" + phase +
                " | error=" + ex.GetBaseException().Message.Replace('|', '/'));
        }
    }
}
