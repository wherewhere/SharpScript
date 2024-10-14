using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.VisualBasic;
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
using System.Text;
using System.Threading.Tasks;
using Windows.UI.Core;
using CSharpLanguageVersion = Microsoft.CodeAnalysis.CSharp.LanguageVersion;
using CSharpSyntaxFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using VisualBasicLanguageVersion = Microsoft.CodeAnalysis.VisualBasic.LanguageVersion;
using VisualBasicSyntaxFactory = Microsoft.CodeAnalysis.VisualBasic.SyntaxFactory;

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
                "Microsoft.VisualBasic.Core.dll",
                "System.Net.WebClient.dll")
            .ToArray();

        public static LanguageType[] LanguageTypes { get; } = Enum.GetValues<LanguageType>();
        public static OutputType[] OutputTypes { get; } = Enum.GetValues<OutputType>();

        public CoreDispatcher Dispatcher { get; } = dispatcher;

        private CompilateOptions options = new(dispatcher);
        public CompilateOptions Options
        {
            get => options;
            set => SetProperty(ref options, value);
        }

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

        public async Task<MemoryStream> CompilateAsync(string code)
        {
            List<string> results = [];
            try
            {
                Diagnostics = ["Compilating..."];
                await ThreadSwitcher.ResumeBackgroundAsync();
                MemoryStream assemblyStream = new();
                Compilation compilation = Options.InputOptions switch
                {
                    CSharpInputOptions csharp => GetCSharpCompilate(code, csharp),
                    VisualBasicInputOptions vb => GetCSharpCompilate(code, vb),
                    _ => throw new Exception("Invalid language type.")
                };
                EmitResult emitResult = compilation.Emit(assemblyStream);
                if (emitResult.Success)
                {
                    return assemblyStream;
                }
                else
                {
                    results.AddRange(emitResult.Diagnostics.Select(x =>
                    {
                        FileLinePositionSpan line = x.Location.GetLineSpan();
                        return $"{(string.IsNullOrEmpty(line.Path) ? "Current" : string.Empty)}{x.Location.GetLineSpan()}: {x.Severity} {x.Id}: {x.GetMessage()}";
                    }));
                    return null;
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
            finally
            {
                SettingsHelper.Set(SettingsHelper.CachedCode, code);
                Diagnostics = results;
                GC.Collect();
            }
            return null;
        }

        private static Compilation GetCSharpCompilate(string code, CSharpInputOptions options)
        {
            SyntaxTree syntaxTree =
                CSharpSyntaxFactory.ParseSyntaxTree(
                    code,
                    new CSharpParseOptions(
                        options.LanguageVersion,
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
            return compilation;
        }

        private static Compilation GetCSharpCompilate(string code, VisualBasicInputOptions options)
        {
            SyntaxTree syntaxTree =
                VisualBasicSyntaxFactory.ParseSyntaxTree(
                    code,
                    new VisualBasicParseOptions(
                        options.LanguageVersion,
                        DocumentationMode.Parse,
                        SourceCodeKind.Regular),
                    null);
            Compilation compilation =
                VisualBasicCompilation.Create(
                    "SharpScript",
                    [syntaxTree],
                    references,
                    new VisualBasicCompilationOptions(OutputKind.ConsoleApplication));
            return compilation;
        }

        public async Task ExecuteAsync(MemoryStream assemblyStream)
        {
            bool finished = false;
            List<string> results = [];
            StringBuilder output = new();
            try
            {
                Diagnostics = ["Executing..."];
                await ThreadSwitcher.ResumeBackgroundAsync();
                AssemblyLoadContext context = new("ExecutorContext", isCollectible: true);
                try
                {
                    assemblyStream.Position = 0;
                    Assembly assembly = context.LoadFromStream(assemblyStream);
                    if (assembly.EntryPoint is MethodInfo main)
                    {
                        string[][] args = main.GetParameters().Length > 0 ? [Array.Empty<string>()] : null;
                        TextWriter temp = Console.Out;
                        await using StringWriter writer = new(output);
                        Console.SetOut(writer);
                        object @return = main.Invoke(null, args);
                        Console.SetOut(temp);
                        results.Add(output.ToString());
                        finished = true;
                        results.Add($"Exits with code {@return ?? 0}.");
                    }
                }
                finally
                {
                    context.Unload();
                    assemblyStream.Dispose();
                }
            }
            catch (Exception ex)
            {
                if (!finished)
                {
                    results.Add(output.ToString());
                }
                results.Add(ex.ToString());
            }
            finally
            {
                Diagnostics = results;
                GC.Collect();
            }
        }

        public async Task ProcessAsync(string code)
        {
            if (await CompilateAsync(code).ConfigureAwait(false) is MemoryStream assemblyStream)
            {
                await ExecuteAsync(assemblyStream).ConfigureAwait(false);
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

    public enum LanguageType
    {
        CSharp,
        VisualBasic
    }

    public enum OutputType
    {
        Run,
        CSharp,
        IL,
        JIT
    }

    public partial class CompilateOptions(CoreDispatcher dispatcher) : INotifyPropertyChanged
    {
        public CoreDispatcher Dispatcher { get; } = dispatcher;

        private LanguageType languageType = LanguageType.CSharp;
        public LanguageType LanguageType
        {
            get => languageType;
            set
            {
                if (languageType != value)
                {
                    switch (value)
                    {
                        case LanguageType.CSharp:
                            InputOptions = new CSharpInputOptions(Dispatcher);
                            break;
                        case LanguageType.VisualBasic:
                            InputOptions = new VisualBasicInputOptions(Dispatcher);
                            break;
                    }
                    languageType = value;
                    RaisePropertyChangedEvent();
                    RaisePropertyChangedEvent(nameof(LanguageName));
                }
            }
        }

        public string LanguageName => LanguageType switch
        {
            LanguageType.CSharp => "csharp",
            LanguageType.VisualBasic => "vb",
            _ => throw new Exception("Invalid language type.")
        };

        private InputOptions inputOptions = new CSharpInputOptions(dispatcher);
        public InputOptions InputOptions
        {
            get => inputOptions;
            set => SetProperty(ref inputOptions, value);
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
    }

    public abstract partial class InputOptions(CoreDispatcher dispatcher) : INotifyPropertyChanged
    {
        public CoreDispatcher Dispatcher { get; } = dispatcher;

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
    }

    public partial class CSharpInputOptions(CoreDispatcher dispatcher) : InputOptions(dispatcher)
    {
        private CSharpLanguageVersion languageVersion = CSharpLanguageVersion.Preview;
        public CSharpLanguageVersion LanguageVersion
        {
            get => languageVersion;
            set => SetProperty(ref languageVersion, value);
        }
    }

    public partial class VisualBasicInputOptions(CoreDispatcher dispatcher) : InputOptions(dispatcher)
    {
        private VisualBasicLanguageVersion languageVersion = VisualBasicLanguageVersion.Latest;
        public VisualBasicLanguageVersion LanguageVersion
        {
            get => languageVersion;
            set => SetProperty(ref languageVersion, value);
        }
    }
}
