using SharpScript.Helpers;
using SharpScript.ViewModels;
using System.Threading.Tasks;
using Windows.UI.Xaml.Controls;
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
            string code = SettingsHelper.Get<string>(SettingsHelper.CachedCode);
            Input.Editor.SetText(code);
            _ = Provider.CompilateAsync(code);
            Input.Editor.Modified += Editor_Modified;
        }

        private uint count = 0;
        private async void Editor_Modified(Editor sender, ModifiedEventArgs args)
        {
            try
            {
                count++;
                await Task.Delay(500);
                if (count > 1) { return; }
                string code = sender.GetTargetText().TrimStart('\0');
                await Provider.CompilateAsync(code).ConfigureAwait(false);
            }
            finally
            {
                count--;
            }
        }
    }
}
