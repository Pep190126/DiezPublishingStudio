using System.Reflection;
using Avalonia.Controls;

namespace DiezPublishingStudio;

/// <summary>
/// Executes Avalonia's own queued layout pass on the pinned 11.3.18 runtime. This is intentionally
/// isolated behind reflection because TopLevel.LayoutManager is internal. It never calls Measure or
/// Arrange directly; it only asks Avalonia to drain the layout work it already queued.
/// </summary>
internal static class AvaloniaLayoutPumpUi
{
    public static bool Execute(MainWindow window, string reason)
    {
        try
        {
            var layoutManagerProperty = typeof(TopLevel).GetProperty(
                "LayoutManager",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var layoutManager = layoutManagerProperty?.GetValue(window);
            var execute = layoutManager?.GetType().GetMethod(
                "ExecuteLayoutPass",
                BindingFlags.Instance | BindingFlags.Public);

            if (layoutManager is null || execute is null)
            {
                SafeStartupTrace.Write(
                    "avalonia-layout-pump | reason=" + reason + " | executed=false | reflection=unavailable");
                return false;
            }

            execute.Invoke(layoutManager, null);
            SafeStartupTrace.Write(
                "avalonia-layout-pump | reason=" + reason + " | executed=true");
            return true;
        }
        catch (Exception ex)
        {
            SafeStartupTrace.Write(
                "avalonia-layout-pump | reason=" + reason + " | executed=false | error=" +
                ex.GetBaseException().GetType().Name + ": " + ex.GetBaseException().Message);
            return false;
        }
    }
}
