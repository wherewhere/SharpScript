using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Scripting;
using SharpScript.Common;
using SharpScript.Helpers;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Threading.Tasks;
using Windows.UI.Core;

namespace SharpScript.ViewModels
{
    public partial class EditorViewModel(CoreDispatcher dispatcher) : INotifyPropertyChanged
    {
        private static readonly MetadataReference[] references =
            GetMetadataReferences(
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
                    "System.Net.WebClient.dll")
                .ToArray();

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
                SyntaxTree syntaxTree =
                    SyntaxFactory.ParseSyntaxTree(
                        code,
                        new CSharpParseOptions(
                            LanguageVersion.Preview,
                            DocumentationMode.Parse,
                            SourceCodeKind.Regular),
                        null);
                Compilation compilation =
                    CSharpCompilation.Create(
                        "SharpScript",
                        [syntaxTree],
                        references,
                        new CSharpCompilationOptions(
                            OutputKind.ConsoleApplication,
                            allowUnsafe: true));
                using MemoryStream assemblyStream = new();
                EmitResult emitResult = compilation.Emit(assemblyStream);
                if (emitResult.Success)
                {
                    AssemblyLoadContext context = new("ExecutorContext", isCollectible: true);
                    try
                    {
                        assemblyStream.Position = 0;
                        Assembly assembly = context.LoadFromStream(assemblyStream);
                        if (assembly.EntryPoint is MethodInfo main)
                        {
                            string[][] args = main.GetParameters().Length > 0 ? [Array.Empty<string>()] : null;
                            object @return = main.Invoke(null, args);
                            results.Add($"Exits with code {@return ?? 0}.");
                        }
                    }
                    finally
                    {
                        context.Unload();
                    }
                }
                else
                {
                    results.AddRange(emitResult.Diagnostics.Select(x =>
                    {
                        FileLinePositionSpan line = x.Location.GetLineSpan();
                        return $"{(string.IsNullOrEmpty(line.Path) ? "Current" : string.Empty)}{x.Location.GetLineSpan()}: {x.Severity} {x.Id}: {x.GetMessage()}";
                    }));
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

        private static IEnumerable<MetadataReference> GetMetadataReferences(params string[] references)
        {
            ScriptMetadataResolver resolver = ScriptMetadataResolver.Default;
            foreach (string reference in references)
            {
                foreach(PortableExecutableReference resolved in resolver.ResolveReference(reference, null, MetadataReferenceProperties.Assembly))
                {
                    yield return resolved;
                }
            }
        }
    }
}
