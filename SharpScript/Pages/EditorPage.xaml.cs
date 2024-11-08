using SharpScript.Helpers;
using SharpScript.ViewModels;
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Data;
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
        private bool _isInitialized;
        public EditorViewModel Provider { get; }

        public EditorPage()
        {
            InitializeComponent();
            Provider = new EditorViewModel(Dispatcher)
            {
                Options =
                {
                    LanguageType = SettingsHelper.Get<LanguageType>(SettingsHelper.LanguageType),
                    OutputType = SettingsHelper.Get<OutputType>(SettingsHelper.OutputType)
                }
            };
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            string code = SettingsHelper.Get<string>(SettingsHelper.CachedCode);
            Input.Editor.SetText(code);
            _ = Provider.ProcessAsync(code).ContinueWith(_ => _isInitialized = true);
            Input.Editor.Modified += Editor_Modified;
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            Input.Editor.Modified -= Editor_Modified;
            SettingsHelper.Set(SettingsHelper.CachedCode, Input.Editor.GetTargetText());
        }

        private void Editor_Modified(Editor sender, ModifiedEventArgs args) => _ = ProcessAsync(sender);

        private void ComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _ = ProcessAsync(Input.Editor);
            if (sender is ComboBox element)
            {
                switch (element.Name)
                {
                    case nameof(LanguageType):
                        SettingsHelper.Set(SettingsHelper.LanguageType, (LanguageType)e.AddedItems.FirstOrDefault());
                        break;
                    case nameof(OutputType):
                        SettingsHelper.Set(SettingsHelper.OutputType, (OutputType)e.AddedItems.FirstOrDefault());
                        break;
                }
            }
        }

        private uint count = 0;
        private async Task ProcessAsync(Editor sender)
        {
            if (!_isInitialized) { return; }
            try
            {
                count++;
                await Task.Delay(500);
                if (count > 1) { return; }
                string code = sender.GetTargetText().TrimStart('\0');
                await Provider.ProcessAsync(code).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                SettingsHelper.LogManager.GetLogger(nameof(EditorPage)).Error(ex.ExceptionToMessage(), ex);
            }
            finally
            {
                count--;
            }
        }
    }

    public partial class LanguageVersionFormatConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            string result = value?.ToString() ?? string.Empty;
            switch (value)
            {
                case Microsoft.CodeAnalysis.CSharp.LanguageVersion
                    or ICSharpCode.Decompiler.CSharp.LanguageVersion:
                    result = value.ToString().Replace("CSharp", "C# ");
                    break;
                case Microsoft.CodeAnalysis.VisualBasic.LanguageVersion:
                    result = value.ToString().Replace("VisualBasic", "Visual Basic ");
                    break;
            }
            return result.Replace('_', '.');
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language) =>
            Enum.Parse(targetType, value?.ToString().Replace("C# ", "CSharp").Replace("Visual Basic ", "VisualBasic").Replace('.', '_') ?? string.Empty);
    }
}
