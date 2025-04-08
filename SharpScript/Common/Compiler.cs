using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.CSharp.OutputVisitor;
using ICSharpCode.Decompiler.CSharp.Syntax;
using ICSharpCode.Decompiler.Disassembler;
using ICSharpCode.Decompiler.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.Extensions.Logging;
using Mobius.ILasm.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text;
using System.Threading.Tasks;
using CSharpLanguageVersion = Microsoft.CodeAnalysis.CSharp.LanguageVersion;
using LanguageVersion = ICSharpCode.Decompiler.CSharp.LanguageVersion;
using RoslynDiagnostic = Microsoft.CodeAnalysis.Diagnostic;
using VisualBasicLanguageVersion = Microsoft.CodeAnalysis.VisualBasic.LanguageVersion;

namespace SharpScript.Common
{
    public class Compiler(ILoggerFactory factory)
    {
        public static LanguageType[] LanguageTypes { get; } = Enum.GetValues<LanguageType>();
        public static OutputType[] OutputTypes { get; } = Enum.GetValues<OutputType>();

        private readonly ILogger<Compiler> logger = factory.CreateLogger<Compiler>();

        public CompilateOptions Options { get; set; } = new();

        private async ValueTask<(CompilationResults streams, List<Diagnostic> diagnostics)> CompilateAsync(string code)
        {
            List<Diagnostic> results = [];
            try
            {
                await Task.Yield();
                bool isExe = Options.OutputType == OutputType.Run;
                CompilationResults streams = Options.LanguageType switch
                {
                    LanguageType.CSharp or LanguageType.VisualBasic => await RoslynCompilateAsync(code, results, isExe).ConfigureAwait(false),
                    LanguageType.IL => await ILCompilateAsync(code, results, isExe),
                    _ => throw new Exception("Invalid language type.")
                };
                return (streams, results);
            }
            catch (AggregateException aex) when (aex.InnerExceptions?.Count > 1)
            {
                results.Add(new Diagnostic(aex));
            }
            catch (AggregateException aex)
            {
                results.Add(new Diagnostic(aex.InnerException));
            }
            catch (Exception ex)
            {
                results.Add(new Diagnostic(ex));
            }
            finally
            {
                GC.Collect();
            }
            return (null, results);
        }

        public async ValueTask<List<Diagnostic>> GetDiagnosticsAsync(string code)
        {
            List<Diagnostic> results = [];
            try
            {
                await Task.Yield();
                bool isConsole = Options.OutputType == OutputType.Run;
                results = Options.LanguageType switch
                {
                    LanguageType.CSharp or LanguageType.VisualBasic => await GetRoslynDiagnosticsAsync(code, results, isConsole).ConfigureAwait(false),
                    LanguageType.IL => await GetILDiagnosticsAsync(code, results, isConsole),
                    _ => throw new Exception("Invalid language type.")
                };
                return results;
            }
            catch (AggregateException aex) when (aex.InnerExceptions?.Count > 1)
            {
                results.Add(new Diagnostic(aex));
            }
            catch (AggregateException aex)
            {
                results.Add(new Diagnostic(aex.InnerException));
            }
            catch (Exception ex)
            {
                results.Add(new Diagnostic(ex));
            }
            finally
            {
                GC.Collect();
            }
            return results;
        }

        public ValueTask<IEnumerable<CompletionItem>> GetCompletionsAsync(string code, int position)
        {
            if (Options.InputOptions is RoslynOptions options)
            {
                bool isConsole = Options.OutputType == OutputType.Run;
                return new RoslynCodeSession(code, options, isConsole, factory.CreateLogger<RoslynCodeSession>()).GetCompletionsAsync(position);
            }
            return ValueTask.FromResult<IEnumerable<CompletionItem>>([]);
        }

        private ValueTask<CompilationResults> RoslynCompilateAsync(string code, ICollection<Diagnostic> results, bool isConsole) =>
            new RoslynCodeSession(code, Options.InputOptions as RoslynOptions, isConsole, factory.CreateLogger<RoslynCodeSession>()).Compile(results);

        private ValueTask<T> GetRoslynDiagnosticsAsync<T>(string code, T results, bool isConsole) where T : ICollection<Diagnostic> =>
            new RoslynCodeSession(code, Options.InputOptions as RoslynOptions, isConsole, factory.CreateLogger<RoslynCodeSession>()).GetDiagnosticsAsync(results);

        private static ValueTask<CompilationResults> ILCompilateAsync(string code, ICollection<Diagnostic> results, bool isConsole) =>
            new ILCodeSession(code, isConsole).Compile(results);

        private static ValueTask<T> GetILDiagnosticsAsync<T>(string code, T results, bool isConsole) where T : ICollection<Diagnostic> =>
            new ILCodeSession(code, isConsole).GetDiagnosticsAsync(results);

        private async ValueTask<string> DecompileAsync(CompilationResults streams) => Options.OutputOptions switch
        {
            CSharpOutputOptions csharp => await Decompiler.CSharpDecompileAsync(streams, csharp).ConfigureAwait(false),
            ILOutputOptions => await Decompiler.ILDecompileAsync(streams).ConfigureAwait(false),
            _ => throw new Exception("Invalid output type.")
        };

        private static async ValueTask<List<string>> ExecuteAsync(CompilationResults streams)
        {
            bool finished = false;
            List<string> results = [];
            StringBuilder output = new();
            try
            {
                await Task.Yield();
                AssemblyLoadContext context = new("ExecutorContext", isCollectible: true);
                try
                {
                    MemoryStream assemblyStream = streams.AssemblyStream;
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
                    streams.Dispose();
                }
            }
            catch (Exception ex)
            {
                if (!finished)
                {
                    results.Add(output.ToString());
                }
                results.Add(ex.Message);
            }
            finally
            {
                GC.Collect();
            }
            return results;
        }

        public async ValueTask<CompileResult> ProcessAsync(string code)
        {
            try
            {
                (CompilationResults assemblyStream, List<Diagnostic> diagnostics) = await CompilateAsync(code).ConfigureAwait(false);
                if (assemblyStream != null)
                {
                    switch (Options.OutputType)
                    {
                        case OutputType.CSharp
                            or OutputType.IL:
                            string results = await DecompileAsync(assemblyStream).ConfigureAwait(false);
                            return new CompileResult(diagnostics, results);
                        case OutputType.Run:
                            List<string> outputs = await ExecuteAsync(assemblyStream).ConfigureAwait(false);
                            return new CompileResult(diagnostics, null, outputs);
                    }
                }
                return new CompileResult(diagnostics, null);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Compilate or {type} assembly failed. {message} (0x{hResult:X})", Options?.OutputType == OutputType.Run ? "execute" : "decompile", ex.GetMessage(), ex.HResult);
            }
            return new CompileResult([], null);
        }
    }

    public record struct CompileResult(List<Diagnostic> Diagnostics, string Decompiled, params List<string> Outputs);

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
        Run
    }

    public sealed class Diagnostic(DiagnosticSeverity severity, string message)
    {
        public string ID { get; }
        public LinePositionSpan Location { get; }
        public string Message => message;
        public string Severity => severity.ToString();

        public Diagnostic(Exception exception) : this(DiagnosticSeverity.Error, exception.Message) { }

        public Diagnostic(RoslynDiagnostic diagnostic) : this(diagnostic.Severity, diagnostic.GetMessage())
        {
            ID = diagnostic.Id;
            Location = diagnostic.Location.GetLineSpan().Span;
        }

        public Diagnostic(Mono.ILASM.Location location, DiagnosticSeverity severity, string message) : this (severity, message)
        {
            var position = new LinePosition(location.line - 1, location.column);
            Location = new LinePositionSpan(position, position);
        }
    }

    public class CompilateOptions()
    {
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
                        LanguageType.CSharp => new CSharpInputOptions(),
                        LanguageType.VisualBasic => new VisualBasicInputOptions(),
                        LanguageType.IL => new ILInputOptions(),
                        _ => throw new Exception("Invalid language type."),
                    };
                    languageType = value;
                }
            }
        }

        public string LanguageName => LanguageType switch
        {
            LanguageType.CSharp => "csharp",
            LanguageType.VisualBasic => "vb",
            LanguageType.IL => "csharp",
            _ => throw new Exception("Invalid language type.")
        };

        public InputOptions InputOptions { get; set; } = new CSharpInputOptions();

        private OutputType outputType = OutputType.Run;
        public OutputType OutputType
        {
            get => outputType;
            set
            {
                if (outputType != value)
                {
                    OutputOptions = value switch
                    {
                        OutputType.CSharp => new CSharpOutputOptions(),
                        OutputType.IL => new ILOutputOptions(),
                        OutputType.Run => new RunOutputOptions(),
                        _ => throw new Exception("Invalid output type."),
                    };
                    outputType = value;
                }
            }
        }

        public OutputOptions OutputOptions { get; set; } = new RunOutputOptions();
    }

    public interface IInputOptions
    {
        string LanguageName => null;
        Array LanguageVersions => null;
        Enum LanguageVersion { get => null; set { } }
    }

    public abstract class InputOptions : IInputOptions;

    public abstract class RoslynOptions : InputOptions, IInputOptions
    {
        public virtual string LanguageName => this switch
        {
            CSharpInputOptions => LanguageNames.CSharp,
            VisualBasicInputOptions => LanguageNames.VisualBasic,
            _ => throw new Exception("Invalid language type.")
        };

        public void GetOptions(bool isConsole, out CompilationOptions compilation, out ParseOptions parse)
        {
            switch (this)
            {
                case CSharpInputOptions csharp:
                    compilation = new CSharpCompilationOptions(
                        isConsole ? OutputKind.ConsoleApplication : OutputKind.DynamicallyLinkedLibrary,
                        allowUnsafe: true,
                        concurrentBuild: false);
                    parse = new CSharpParseOptions(
                        csharp.LanguageVersion,
                        DocumentationMode.Parse,
                        SourceCodeKind.Regular);
                    break;
                case VisualBasicInputOptions vb:
                    compilation = new VisualBasicCompilationOptions(
                        isConsole ? OutputKind.ConsoleApplication : OutputKind.DynamicallyLinkedLibrary,
                        concurrentBuild: false);
                    parse = new VisualBasicParseOptions(
                        vb.LanguageVersion,
                        DocumentationMode.Parse,
                        SourceCodeKind.Regular);
                    break;
                default:
                    throw new Exception("Invalid language type.");
            }
        }
    }

    public sealed class CSharpInputOptions : RoslynOptions, IInputOptions
    {
        Array IInputOptions.LanguageVersions => Enum.GetValues<CSharpLanguageVersion>();
        Enum IInputOptions.LanguageVersion
        {
            get => LanguageVersion;
            set => LanguageVersion = (CSharpLanguageVersion)(value ?? CSharpLanguageVersion.Preview);
        }

        public override string LanguageName => LanguageNames.CSharp;
        public CSharpLanguageVersion LanguageVersion { get; set; } = CSharpLanguageVersion.Preview;
    }

    public sealed class VisualBasicInputOptions : RoslynOptions, IInputOptions
    {
        Array IInputOptions.LanguageVersions => Enum.GetValues<VisualBasicLanguageVersion>();
        Enum IInputOptions.LanguageVersion
        {
            get => LanguageVersion;
            set => LanguageVersion = (VisualBasicLanguageVersion)(value ?? VisualBasicLanguageVersion.Latest);
        }

        public override string LanguageName => LanguageNames.VisualBasic;
        public VisualBasicLanguageVersion LanguageVersion { get; set; } = VisualBasicLanguageVersion.Latest;
    }

    public sealed class ILInputOptions : InputOptions;

    public interface IOutputOptions
    {
        bool IsCSharp => false;
        Enum LanguageVersion { get => default; set { } }
    }

    public abstract class OutputOptions : IOutputOptions;

    public sealed class CSharpOutputOptions : OutputOptions, IOutputOptions
    {
        public static List<LanguageVersion> LanguageVersions
        {
            get
            {
                List<LanguageVersion> list = [.. Enum.GetValues<LanguageVersion>()];
                list.Remove(LanguageVersion.Preview);
                return list;
            }
        }

        bool IOutputOptions.IsCSharp => true;
        Enum IOutputOptions.LanguageVersion
        {
            get => LanguageVersion;
            set => LanguageVersion = (LanguageVersion)(value ?? LanguageVersion.CSharp1);
        }

        public LanguageVersion LanguageVersion { get; set; } = LanguageVersion.CSharp1;
    }

    public sealed class ILOutputOptions : OutputOptions;

    public sealed class RunOutputOptions : OutputOptions;
}
