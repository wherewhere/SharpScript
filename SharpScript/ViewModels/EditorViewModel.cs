using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using SharpScript.Common;
using SharpScript.Helpers;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Windows.UI.Core;

namespace SharpScript.ViewModels
{
    public class EditorViewModel(CoreDispatcher dispatcher) : INotifyPropertyChanged
    {
        private static readonly ScriptOptions options =
            ScriptOptions.Default
                .WithLanguageVersion(LanguageVersion.Preview)
                .WithAllowUnsafe(true)
                .WithReferences(
                    "System.Private.CoreLib.dll",
                    "System.Runtime.dll",
                    "System.Console.dll",
                    "netstandard.dll",
                    "System.Text.RegularExpressions.dll",
                    "System.Linq.dll",
                    "System.Linq.Expressions.dll",
                    "System.IO.dll",
                    "System.Net.Primitives.dll",
                    "System.Net.Http.dll",
                    "System.Private.Uri.dll",
                    "System.Reflection.dll",
                    "System.ComponentModel.Primitives.dll",
                    "System.Globalization.dll",
                    "System.Collections.Concurrent.dll",
                    "System.Collections.NonGeneric.dll",
                    "Microsoft.CSharp.dll",
                    "System.Net.WebClient.dll");

        public CoreDispatcher Dispatcher { get; } = dispatcher;

        private List<string> diagnostics;
        public List<string> Diagnostics
        {
            get => diagnostics;
            set => SetProperty(ref diagnostics, value);
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected async void RaisePropertyChangedEvent([CallerMemberName] string name = null)
        {
            if (name != null)
            {
                await Dispatcher.ResumeForegroundAsync();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            }
        }

        protected void SetProperty<TProperty>(ref TProperty property, TProperty value, [CallerMemberName] string name = null)
        {
            if (property == null ? value != null : !property.Equals(value))
            {
                property = value;
                RaisePropertyChangedEvent(name);
            }
        }

        public async Task CompilateAsync(string code)
        {
            List<string> results = [];
            try
            {
                Diagnostics = ["Compilating..."];
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
                SettingsHelper.Set(SettingsHelper.CachedCode, code);
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
            finally
            {
                Diagnostics = results;
                GC.Collect();
            }
        }
    }
}
