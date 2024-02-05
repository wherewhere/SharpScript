using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Monaco;
using SharpScript.Common;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Windows.ApplicationModel.Core;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace SharpScript.Pages
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        private static readonly ScriptOptions options = ScriptOptions.Default.WithAllowUnsafe(true).WithReferences(Assembly.GetExecutingAssembly().GetReferencedAssemblies().Select(x => MetadataReference.CreateFromFile(Assembly.Load(x.Name).Location)));

        public MainPage() => InitializeComponent();

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            CoreApplicationViewTitleBar TitleBar = CoreApplication.GetCurrentView().TitleBar;
            TitleBar.LayoutMetricsChanged += TitleBar_LayoutMetricsChanged;
            TitleBar.IsVisibleChanged += TitleBar_IsVisibleChanged;
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            CoreApplicationViewTitleBar TitleBar = CoreApplication.GetCurrentView().TitleBar;
            TitleBar.LayoutMetricsChanged -= TitleBar_LayoutMetricsChanged;
            TitleBar.IsVisibleChanged -= TitleBar_IsVisibleChanged;
        }

        private uint count = 0;

        private async void Input_EditorContentChanged(object sender, EventArgs e)
        {
            if (sender is not MonacoEditor editor) { return; }
            try
            {
                count++;
                await Task.Delay(500);
                if (count > 1) { return; }
                List<string> results = [];
                try
                {
                    string code = editor.EditorContent;
                    await ThreadSwitcher.ResumeBackgroundAsync();
                    Script<object> script = CSharpScript.Create(code, options);
                    Compilation compilation = script.GetCompilation();
                    ImmutableArray<Diagnostic> diagnostics = compilation.GetDiagnostics();
                    bool isSuccessful = true;
                    results.AddRange(diagnostics.Select(x =>
                    {
                        if (x.Severity == DiagnosticSeverity.Error) { isSuccessful = false; }
                        FileLinePositionSpan line = x.Location.GetLineSpan();
                        return $"{(string.IsNullOrEmpty(line.Path) ? "Current" : string.Empty)}{x.Location.GetLineSpan()}: {x.Severity} {x.Id}: {x.GetMessage()}";
                    }));
                    if (isSuccessful)
                    {
                        ScriptState<object> scriptState = await script.RunAsync().ConfigureAwait(false);
                        results.Add($"{scriptState.ReturnValue ?? "null"}");
                    }
                }
                catch (CompilationErrorException cex)
                {
                    results.Add(cex.Message);
                }
                catch (AggregateException aex) when (aex.InnerExceptions?.Count > 1)
                {
                    results.Add(aex.Message);
                }
                catch (AggregateException aex)
                {
                    results.Add(aex.InnerException.ToString());
                }
                catch (Exception ex)
                {
                    results.Add(ex.ToString());
                }
                await DispatcherQueue.ResumeForegroundAsync();
                Output.ItemsSource = results;
            }
            finally
            {
                GC.Collect();
                count--;
            }
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
