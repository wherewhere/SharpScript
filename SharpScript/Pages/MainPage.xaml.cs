using Windows.ApplicationModel.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace SharpScript.Pages
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        public MainPage() => InitializeComponent();

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            CoreApplicationViewTitleBar TitleBar = CoreApplication.GetCurrentView().TitleBar;
            TitleBar.LayoutMetricsChanged += TitleBar_LayoutMetricsChanged;
            TitleBar.IsVisibleChanged += TitleBar_IsVisibleChanged;
            _ = CoreAppFrame.Navigate(typeof(EditorPage));
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            CoreApplicationViewTitleBar TitleBar = CoreApplication.GetCurrentView().TitleBar;
            TitleBar.LayoutMetricsChanged -= TitleBar_LayoutMetricsChanged;
            TitleBar.IsVisibleChanged -= TitleBar_IsVisibleChanged;
        }

        private void UpdateTitleBarLayout(CoreApplicationViewTitleBar TitleBar)
        {
            CustomTitleBar.Opacity = TitleBar.SystemOverlayLeftInset > 48 ? 0 : 1;
            LeftPaddingColumn.Width = new GridLength(TitleBar.SystemOverlayLeftInset);
            RightPaddingColumn.Width = new GridLength(TitleBar.SystemOverlayRightInset);
        }

        private void UpdateTitleBarVisible(bool IsVisible)
        {
            TopPaddingRow.Height = IsVisible ? new GridLength(32) : new GridLength(0);
            CustomTitleBar.Visibility = IsVisible ? Visibility.Visible : Visibility.Collapsed;
        }

        private void TitleBar_IsVisibleChanged(CoreApplicationViewTitleBar sender, object args) => UpdateTitleBarVisible(sender.IsVisible);

        private void TitleBar_LayoutMetricsChanged(CoreApplicationViewTitleBar sender, object args) => UpdateTitleBarLayout(sender);
    }
}
