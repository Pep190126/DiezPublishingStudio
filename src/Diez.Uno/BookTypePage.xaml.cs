using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace DiezPublishingStudio.UnoSpike;

public sealed partial class BookTypePage : Page
{
    public BookTypePage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        BookTitleBox.Text = SpikeState.BookTitle;
    }

    private void BookTitleBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox textBox)
            SpikeState.BookTitle = textBox.Text;
    }

    private void Home_Click(object sender, RoutedEventArgs e)
    {
        Frame.Navigate(typeof(HomePage));
    }

    private void OpenColoring_Click(object sender, RoutedEventArgs e)
    {
        Frame.Navigate(typeof(ColoringPage));
    }
}
