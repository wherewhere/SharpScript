using SharpScript.Helpers;
using SharpScript.ViewModels;
using System.Threading.Tasks;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using WinUIEditor;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace SharpScript.Pages
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class EditorPage : Page
    {
        public EditorViewModel Provider { get; }

        public EditorPage()
        {
            InitializeComponent();
            Provider = new EditorViewModel(Dispatcher);
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            string code = SettingsHelper.Get<string>(SettingsHelper.CachedCode);
            Input.Editor.SetText(code);
            Provider.ProcessAsync(code).ContinueWith(_ =>
            {
                Input.Editor.Modified += Editor_Modified;
            });
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            Input.Editor.Modified -= Editor_Modified;
            SettingsHelper.Set(SettingsHelper.CachedCode, Input.Editor.GetTargetText());
        }

        private void Editor_Modified(Editor sender, ModifiedEventArgs args) => _ = ProcessAsync(sender);

        private uint count = 0;
        private async Task ProcessAsync(Editor sender)
        {
            try
            {
                count++;
                await Task.Delay(500);
                if (count > 1) { return; }
                string code = sender.GetTargetText().TrimStart('\0');
                await Provider.ProcessAsync(code).ConfigureAwait(false);
            }
            finally
            {
                count--;
            }
        }
    }
}
