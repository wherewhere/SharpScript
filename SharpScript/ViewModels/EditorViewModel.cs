using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.CodeAnalysis.VisualBasic;
using Mobius.ILasm.Core;
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
                return Options.LanguageType switch
                {
                    LanguageType.CSharp or LanguageType.VisualBasic => RoslynCompilate(code, Options, results),
                    LanguageType.IL => ILCompilate(code, results),
                    _ => throw new Exception("Invalid language type.")
                };
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

        private static MemoryStream RoslynCompilate(string code, CompilateOptions options, ICollection<string> results)
        {
            MemoryStream assemblyStream = new();
            Compilation compilation = options.InputOptions switch
            {
                CSharpInputOptions csharp => GetRoslynCompilate(code, csharp),
                VisualBasicInputOptions vb => GetRoslynCompilate(code, vb),
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

        private static Compilation GetRoslynCompilate(string code, CSharpInputOptions options)
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

        private static Compilation GetRoslynCompilate(string code, VisualBasicInputOptions options)
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

        private static MemoryStream ILCompilate(string code, ICollection<string> results)
        {
            Logger logger = new(results);
            Driver driver = new(logger, Driver.Target.Exe, false, false, false);

            try
            {
                MemoryStream assemblyStream = new();
                if (driver.Assemble([code], assemblyStream))
                {
                    return assemblyStream;
                }
            }
            catch (Exception ex) when (ex.GetType().Name.StartsWith("yy"))
            {
                return null;
            }

            return null;
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
                    assemblyStream.Seek(0, SeekOrigin.Begin);
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

        private class Logger(ICollection<string> results) : Mobius.ILasm.interfaces.ILogger
        {
            public void Info(string message) => results.Add($"{nameof(Info)}: {message}");

            public void Warning(string message) => results.Add($"{nameof(Warning)}: {message}");

            public void Error(string message) => results.Add($"{nameof(Error)}: {message}");

            public void Warning(Mono.ILASM.Location location, string message) => results.Add($"Current {location}: {nameof(Warning)}: {message}");

            public void Error(Mono.ILASM.Location location, string message) => results.Add($"Current {location}: {nameof(Error)}: {message}");
        }
    }

    public enum LanguageType
    {
        CSharp = 0b011,
        VisualBasic = 0b111,
        IL = 0b001
    }

    public enum OutputType
    {
        CSharp,
        IL,
        JIT,
        Run
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
                    InputOptions = value switch
                    {
                        LanguageType.CSharp => new CSharpInputOptions(Dispatcher),
                        LanguageType.VisualBasic => new VisualBasicInputOptions(Dispatcher),
                        LanguageType.IL => new ILInputOptions(Dispatcher),
                        _ => throw new Exception("Invalid language type."),
                    };
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
            LanguageType.IL => "il",
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

    public partial class ILInputOptions(CoreDispatcher dispatcher) : InputOptions(dispatcher);
}
