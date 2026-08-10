using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit;

namespace DiezPublishingStudio;

/// <summary>
/// Keeps the existing TextBox instances as the logical/model bridge so all existing
/// TextChanged handlers, validation and save code continue to work, but renders a
/// real AvaloniaEdit editor on top. This avoids the Fluent TextBox rendering failure
/// observed on Windows while preserving the current project model and workflow logic.
/// </summary>
internal static class VisibleEditorBridgeUi
{
    private static readonly HashSet<MainWindow> Attached = [];

    public static void Attach(MainWindow window)
    {
        if (!Attached.Add(window)) return;
        var host = SingleWindowEntryPointUi.GetHost(window);
        var pageHost = host.GetType().GetField("_pageHost", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host) as ContentControl
            ?? throw new InvalidOperationException("PageHost single-window non disponibile per gli editor visibili.");

        pageHost.PropertyChanged += (_, e) =>
        {
            if (e.Property != ContentControl.ContentProperty) return;
            BridgeCurrentPage(pageHost);
            Dispatcher.UIThread.Post(() => BridgeCurrentPage(pageHost), DispatcherPriority.Loaded);
            Dispatcher.UIThread.Post(() => BridgeCurrentPage(pageHost), DispatcherPriority.Background);
        };

        window.Closed += (_, _) => Attached.Remove(window);
        BridgeCurrentPage(pageHost);
    }

    internal static void BridgeCurrentPage(ContentControl pageHost)
    {
        if (pageHost.Content is Control page) BridgeContainer(page);
    }

    private static void BridgeContainer(Control control)
    {
        switch (control)
        {
            case Panel panel:
                for (var i = 0; i < panel.Children.Count; i++)
                {
                    var child = panel.Children[i];
                    if (child is TextBox box && ShouldBridge(box))
                    {
                        panel.Children.RemoveAt(i);
                        panel.Children.Insert(i, BuildBridge(box));
                    }
                    else
                    {
                        BridgeContainer(child);
                    }
                }
                break;

            case Border border when border.Child is Control child:
                if (child is TextBox box && ShouldBridge(box)) border.Child = BuildBridge(box);
                else BridgeContainer(child);
                break;

            case ScrollViewer scroll when scroll.Content is Control child:
                if (child is TextBox box && ShouldBridge(box)) scroll.Content = BuildBridge(box);
                else BridgeContainer(child);
                break;

            case ContentControl content when content.Content is Control child:
                if (child is TextBox box && ShouldBridge(box)) content.Content = BuildBridge(box);
                else BridgeContainer(child);
                break;
        }
    }

    private static bool ShouldBridge(TextBox box) =>
        !box.IsReadOnly && box.Parent is not BridgeHostPanel;

    private static Control BuildBridge(TextBox source)
    {
        var syncing = false;
        var visible = new TextEditor
        {
            Name = string.IsNullOrWhiteSpace(source.Name) ? null : "Visible_" + source.Name,
            Text = source.Text ?? string.Empty,
            Watermark = source.Watermark ?? string.Empty,
            WordWrap = true,
            ShowLineNumbers = false,
            IsReadOnly = source.IsReadOnly,
            IsEnabled = source.IsEnabled,
            Background = Brushes.White,
            Foreground = Brushes.Black,
            FontFamily = source.FontFamily,
            FontSize = source.FontSize,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Focusable = true
        };

        visible.TextArea.Options.EnableHyperlinks = false;
        visible.TextArea.Options.EnableEmailHyperlinks = false;
        visible.TextArea.Options.ShowTabs = false;
        visible.TextArea.Options.ShowSpaces = false;
        visible.TextArea.Options.ShowEndOfLine = false;

        var shell = new Border
        {
            Name = string.IsNullOrWhiteSpace(source.Name) ? null : "VisibleEditorShell_" + source.Name,
            Background = Brushes.White,
            BorderBrush = Brushes.DimGray,
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(5),
            MinHeight = Math.Max(70, source.MinHeight),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            IsEnabled = source.IsEnabled,
            IsVisible = source.IsVisible,
            Child = visible
        };

        if (!double.IsNaN(source.Height) && !double.IsInfinity(source.Height) && source.Height > 0)
            shell.Height = source.Height;

        var host = new BridgeHostPanel
        {
            Name = string.IsNullOrWhiteSpace(source.Name) ? null : "VisibleEditorHost_" + source.Name,
            IsVisible = source.IsVisible,
            MinHeight = shell.MinHeight,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        if (!double.IsNaN(source.Height) && !double.IsInfinity(source.Height) && source.Height > 0)
            host.Height = source.Height;

        // Keep the original TextBox alive in the logical tree. Existing handlers and
        // validation code still observe it, but it no longer provides the visible UI.
        source.Opacity = 0;
        source.IsHitTestVisible = false;
        source.HorizontalAlignment = HorizontalAlignment.Stretch;
        Panel.SetZIndex(source, 0);
        Panel.SetZIndex(shell, 1);
        host.Children.Add(source);
        host.Children.Add(shell);

        visible.TextChanged += (_, _) =>
        {
            if (syncing) return;
            var value = visible.Text ?? string.Empty;
            if (string.Equals(source.Text ?? string.Empty, value, StringComparison.Ordinal)) return;
            syncing = true;
            try { source.Text = value; }
            finally { syncing = false; }
        };

        source.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty)
            {
                if (syncing) return;
                var value = source.Text ?? string.Empty;
                if (string.Equals(visible.Text ?? string.Empty, value, StringComparison.Ordinal)) return;
                syncing = true;
                try { visible.Text = value; }
                finally { syncing = false; }
            }
            else if (e.Property == TextBox.WatermarkProperty)
            {
                visible.Watermark = source.Watermark ?? string.Empty;
            }
            else if (e.Property == Visual.IsVisibleProperty)
            {
                host.IsVisible = source.IsVisible;
                shell.IsVisible = source.IsVisible;
            }
            else if (e.Property == InputElement.IsEnabledProperty)
            {
                shell.IsEnabled = source.IsEnabled;
                visible.IsEnabled = source.IsEnabled;
            }
            else if (e.Property == Layoutable.HeightProperty)
            {
                if (!double.IsNaN(source.Height) && !double.IsInfinity(source.Height) && source.Height > 0)
                {
                    host.Height = source.Height;
                    shell.Height = source.Height;
                }
            }
            else if (e.Property == Layoutable.MinHeightProperty)
            {
                var min = Math.Max(70, source.MinHeight);
                host.MinHeight = min;
                shell.MinHeight = min;
            }
        };

        source.GotFocus += (_, _) => visible.TextArea.Focus();
        visible.GotFocus += (_, _) =>
        {
            shell.BorderBrush = Brushes.Black;
            shell.BorderThickness = new Thickness(2);
        };
        visible.LostFocus += (_, _) =>
        {
            shell.BorderBrush = Brushes.DimGray;
            shell.BorderThickness = new Thickness(2);
        };

        return host;
    }

    private sealed class BridgeHostPanel : Grid { }
}
