using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CSharpLanguageVersion = Microsoft.CodeAnalysis.CSharp.LanguageVersion;
using LanguageVersion = ICSharpCode.Decompiler.CSharp.LanguageVersion;
using VisualBasicLanguageVersion = Microsoft.CodeAnalysis.VisualBasic.LanguageVersion;

namespace SharpScript.Common
{
    public class Compiler(ILoggerFactory factory)
    {
        public static LanguageType[] LanguageTypes { get; } = Enum.GetValues<LanguageType>();
        public static OutputType[] OutputTypes { get; } = Enum.GetValues<OutputType>();

        private readonly ILogger<Compiler> _logger = factory.CreateLogger<Compiler>();

        private ICodeSession<ICodeSession> _codeSession;
        public ICodeSession<ICodeSession> CodeSession
        {
            get
            {
                if (_codeSession == null)
                {
                    bool isConsole = OutputType == OutputType.Run;
                    switch (InputOptions)
                    {
                        case RoslynOptions options:
                            _codeSession = new RoslynCodeSession(string.Empty, options, isConsole, factory.CreateLogger<RoslynCodeSession>());
                            break;
                        case ILInputOptions:
                            _codeSession = new ILCodeSession(string.Empty, isConsole);
                            break;
                    }
                }
                return _codeSession;
            }
        }

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
                    UpdateCodeSession(outputType == OutputType.Run);
                }
            }
        }

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
                    bool isConsole = value == OutputType.Run;
                    if (isConsole ^ (value == OutputType.Run))
                    {
                        UpdateCodeSession(isConsole);
                    }
                    outputType = value;
                }
            }
        }

        public OutputOptions OutputOptions { get; set; } = new RunOutputOptions();

        private void UpdateCodeSession(bool isConsole)
        {
            switch (InputOptions)
            {
                case RoslynOptions options:
                    _codeSession = new RoslynCodeSession(string.Empty, options, isConsole, factory.CreateLogger<RoslynCodeSession>());
                    break;
                case ILInputOptions:
                    _codeSession = new ILCodeSession(string.Empty, isConsole);
                    break;
            }
        }

        private async ValueTask<(CompilationResults streams, List<Diagnostic> diagnostics)> CompilateAsync(string code, CancellationToken cancellationToken = default)
        {
            List<Diagnostic> results = [];
            try
            {
                CompilationResults streams = await CodeSession.SetSourceText(code).Compile(results, cancellationToken).ConfigureAwait(false);
                return (streams, results);
            }
            catch (AggregateException aex) when (aex.InnerExceptions?.Count > 1)
            {
                results.Add(new Diagnostic(aex));
                _logger.LogError(aex, "Compilate failed. {message} (0x{hResult:X})", aex.GetMessage(), aex.HResult);
            }
            catch (AggregateException aex) when (aex.InnerException is Exception ex)
            {
                results.Add(new Diagnostic(ex));
                _logger.LogError(ex, "Compilate failed. {message} (0x{hResult:X})", ex.GetMessage(), ex.HResult);
            }
            catch (Exception ex)
            {
                results.Add(new Diagnostic(ex));
                _logger.LogError(ex, "Compilate failed. {message} (0x{hResult:X})", ex.GetMessage(), ex.HResult);
            }
            finally
            {
                GC.Collect();
            }
            return (null, results);
        }

        public async ValueTask<List<Diagnostic>> GetDiagnosticsAsync(string code, CancellationToken cancellationToken = default)
        {
            List<Diagnostic> results = [];
            try
            {
                bool isConsole = OutputType == OutputType.Run;
                results = await CodeSession.SetSourceText(code).GetDiagnosticsAsync(results, cancellationToken).ConfigureAwait(false);
                return results;
            }
            catch (AggregateException aex) when (aex.InnerExceptions?.Count > 1)
            {
                results.Add(new Diagnostic(aex));
                _logger.LogError(aex, "Get diagnostics failed. {message} (0x{hResult:X})", aex.GetMessage(), aex.HResult);
            }
            catch (AggregateException aex) when (aex.InnerException is Exception ex)
            {
                results.Add(new Diagnostic(ex.InnerException));
                _logger.LogError(ex, "Get diagnostics failed. {message} (0x{hResult:X})", ex.GetMessage(), ex.HResult);
            }
            catch (Exception ex)
            {
                results.Add(new Diagnostic(ex));
                _logger.LogError(ex, "Get diagnostics failed. {message} (0x{hResult:X})", ex.GetMessage(), ex.HResult);
            }
            return results;
        }

        public ValueTask<IEnumerable<CompletionItem>> GetCompletionsAsync(string code, int position, CancellationToken cancellationToken = default)
        {
            return InputOptions is RoslynOptions
                ? CodeSession.SetSourceText(code).GetCompletionsAsync(position, cancellationToken)
                : ValueTask.FromResult<IEnumerable<CompletionItem>>([]);
        }

        private ValueTask<string> DecompileAsync(CompilationResults streams) => OutputOptions switch
        {
            CSharpOutputOptions csharp => Decompiler.CSharpDecompileAsync(streams, csharp),
            ILOutputOptions => Decompiler.ILDecompileAsync(streams),
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
                        string[][] args = main.GetParameters().Length > 0 ? [[]] : null;
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

        public async ValueTask<CompileResult> ProcessAsync(string code, CancellationToken cancellationToken = default)
        {
            try
            {
                (CompilationResults assemblyStream, List<Diagnostic> diagnostics) = await CompilateAsync(code, cancellationToken).ConfigureAwait(false);
                if (assemblyStream != null)
                {
                    switch (OutputType)
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
                _logger.LogError(ex, "Compilate or {type} assembly failed. {message} (0x{hResult:X})", OutputType == OutputType.Run ? "execute" : "decompile", ex.GetMessage(), ex.HResult);
            }
            return new CompileResult([], null);
        }
    }

    public record struct CompileResult(List<Diagnostic> Diagnostics, string Decompiled, params List<string> Outputs);

    [Flags]
    public enum LanguageType
    {
        CSharp = 0b011,
        VisualBasic = 0b111,
        IL = 0b001
    }

    [Flags]
    public enum OutputType
    {
        CSharp = 0b011,
        IL = 0b001,
        Run = 0b100
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
                _ = list.Remove(LanguageVersion.Preview);
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
