using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Reflection.Metadata;
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
                    bool isConsole = outputType == OutputType.Run;
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

        public LanguageType LanguageType
        {
            get;
            set
            {
                if (field != value)
                {
                    InputOptions = value switch
                    {
                        LanguageType.CSharp => new CSharpInputOptions(),
                        LanguageType.VisualBasic => new VisualBasicInputOptions(),
                        LanguageType.IL => new ILInputOptions(),
                        _ => throw new Exception("Invalid language type."),
                    };
                    field = value;
                    UpdateCodeSession(outputType == OutputType.Run);
                }
            }
        } = LanguageType.CSharp;

        public InputOptions InputOptions { get; set; } = new CSharpInputOptions();

        public string InputLanguageVersion
        {
            get => ((IInputOptions)InputOptions).LanguageVersion?.ToString();
            set
            {
                if (((IInputOptions)InputOptions).LanguageVersion?.GetType() is Type @enum)
                {
                    ((IInputOptions)InputOptions).LanguageVersion = (Enum)Enum.Parse(@enum, value, true);
                    UpdateCodeSession(outputType == OutputType.Run);
                }
            }
        }

        public SourceCodeKind SourceCodeKind
        {
            get => RoslynOptions.SourceCodeKind;
            set
            {
                if (RoslynOptions.SourceCodeKind != value)
                {
                    RoslynOptions.SourceCodeKind = value;
                    UpdateCodeSession(outputType == OutputType.Run);
                }
            }
        }

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
                    if (isConsole ^ (outputType == OutputType.Run))
                    {
                        UpdateCodeSession(isConsole);
                    }
                    outputType = value;
                }
            }
        }

        public OutputOptions OutputOptions { get; set; } = new RunOutputOptions();

        public string OutputLanguageVersion
        {
            get => ((IOutputOptions)OutputOptions).LanguageVersion?.ToString();
            set
            {
                if (((IOutputOptions)OutputOptions).LanguageVersion?.GetType() is Type @enum)
                {
                    ((IOutputOptions)OutputOptions).LanguageVersion = (Enum)Enum.Parse(@enum, value, true);
                }
            }
        }

        public void UpdateCodeSession() => UpdateCodeSession(OutputType == OutputType.Run);

        private void UpdateCodeSession(bool isConsole)
        {
            if (_codeSession != null)
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
        }

        private async ValueTask<(CompilationResults streams, List<Diagnostic> diagnostics)> CompilateAsync(string code, CancellationToken cancellationToken = default)
        {
            List<Diagnostic> results = [];
            try
            {
                CompilationResults streams = await CodeSession.SetSourceTextAsync(code, cancellationToken).AsTask().ContinueWith(x => x.Result.CompileAsync(results, cancellationToken).AsTask(), TaskScheduler.Default).Unwrap().ConfigureAwait(false);
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
                results = await CodeSession.SetSourceTextAsync(code, cancellationToken).AsTask().ContinueWith(x => x.Result.GetDiagnosticsAsync(results, cancellationToken).AsTask(), TaskScheduler.Default).Unwrap().ConfigureAwait(false);
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

        public Task<IEnumerable<RoslynCompletionItem>> GetCompletionsAsync(string code, int position, CancellationToken cancellationToken = default)
        {
            return InputOptions is RoslynOptions
                ? CodeSession.SetSourceTextAsync(code, cancellationToken).AsTask().ContinueWith(x => x.Result.GetCompletionsAsync(position, cancellationToken).AsTask(), TaskScheduler.Default).Unwrap()
                : Task.FromResult<IEnumerable<RoslynCompletionItem>>([]);
        }

        public Task<InfoTipItem> GetInfoTipAsync(string code, int position, CancellationToken cancellationToken = default)
        {
            return InputOptions is RoslynOptions
                ? CodeSession.SetSourceTextAsync(code, cancellationToken).AsTask().ContinueWith(x => x.Result.GetInfoTipAsync(position, cancellationToken).AsTask(), TaskScheduler.Default).Unwrap()
                : Task.FromResult<InfoTipItem>(default);
        }

        public Task<AstNodeItem> GetAstAsync(string code, CancellationToken cancellationToken = default)
        {
            return InputOptions is RoslynOptions
                ? CodeSession.SetSourceTextAsync(code, cancellationToken).AsTask().ContinueWith(x => x.Result.GetAstAsync(cancellationToken).AsTask(), TaskScheduler.Default).Unwrap()
                : Task.FromResult<AstNodeItem>(default);
        }

        private ValueTask<string> DecompileAsync(CompilationResults streams, CancellationToken cancellationToken = default) => OutputOptions switch
        {
            CSharpOutputOptions csharp => Decompiler.CSharpDecompileAsync(streams, csharp, cancellationToken),
            ILOutputOptions => Decompiler.ILDecompileAsync(streams),
            _ => throw new Exception("Invalid output type.")
        };

        [StackTraceHidden]
        private static async ValueTask<List<string>> ExecuteAsync(CompilationResults streams)
        {
            List<string> results = [];
            try
            {
                AssemblyLoadContext context = new("ExecutorContext", isCollectible: true);
                try
                {
                    MemoryStream assemblyStream = streams.AssemblyStream;
                    assemblyStream.Position = 0;
                    Assembly assembly = context.LoadFromStream(assemblyStream);
                    if (assembly.EntryPoint is MethodInfo main)
                    {
                        main = GetEntryPoint(main);
                        string[][] args = main.GetParameters().Length > 0 ? [[]] : null;
                        TextWriter temp = Console.Out;
                        StringBuilder output = new();
                        await using StringWriter writer = new(output);
                        object @return;
                        try
                        {
                            Console.SetOut(writer);
                            @return = main.Invoke(null, args);
                            switch (@return)
                            {
                                case Task<int> taskInt:
                                    @return = await taskInt.ConfigureAwait(false);
                                    break;
                                case Task task:
                                    await task.ConfigureAwait(false);
                                    @return = 0;
                                    break;
                                case ValueTask<int> valueTaskInt:
                                    @return = await valueTaskInt.ConfigureAwait(false);
                                    break;
                                case ValueTask valueTask:
                                    await valueTask.ConfigureAwait(false);
                                    @return = 0;
                                    break;
                            }
                        }
                        finally
                        {
                            Console.SetOut(temp);
                            results.Add(output.ToString());
                        }
                        results.Add($"Exits with code {@return ?? 0}.");
                        static MethodInfo GetEntryPoint(MethodInfo main)
                        {
                            try
                            {
                                byte[] bytes = main.GetMethodBody()?.GetILAsByteArray();
                                MethodBase method = bytes switch
                                {
                                    [
                                        (byte)ILOpCode.Ldarg_0,
                                        (byte)ILOpCode.Call, _, _, _, _,
                                        (byte)ILOpCode.Callvirt, _, _, _, _,
                                        (byte)ILOpCode.Stloc_0,
                                        (byte)ILOpCode.Ldloca_s, 0,
                                        (byte)ILOpCode.Call, _, _, _, _,
                                        (byte)ILOpCode.Ret
                                    ] => main.Module.ResolveMethod(BitConverter.ToInt32(bytes.AsSpan(2, 4))),
                                    [
                                        (byte)ILOpCode.Call, _, _, _, _,
                                        (byte)ILOpCode.Callvirt, _, _, _, _,
                                        (byte)ILOpCode.Stloc_0,
                                        (byte)ILOpCode.Ldloca_s, 0,
                                        (byte)ILOpCode.Call, _, _, _, _,
                                        (byte)ILOpCode.Ret
                                    ] => main.Module.ResolveMethod(BitConverter.ToInt32(bytes.AsSpan(1, 4))),
                                    _ => null,
                                };
                                if (method is MethodInfo { ReturnType: Type type } info && (type == typeof(Task) || type.IsSubclassOf(typeof(Task))))
                                {
                                    return info;
                                }
                            }
                            catch { }
                            return main;
                        }
                    }
                }
                finally
                {
                    context.Unload();
                    streams.Dispose();
                }
            }
            catch (TargetInvocationException ex) when (ex.InnerException is Exception e)
            {
                results.Add($"\x1B[1;31m{e}\x1B[0m");
            }
            catch (Exception ex)
            {
                results.Add($"\x1B[1;31m{ex}\x1B[0m");
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
                            or OutputType.VisualBasic
                            or OutputType.IL:
                            string results = await DecompileAsync(assemblyStream, cancellationToken).ConfigureAwait(false);
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

        public async ValueTask<MemoryStream> GetAssemblyAsync(string code, CancellationToken cancellationToken = default)
        {
            try
            {
                (CompilationResults results, _) = await CompilateAsync(code, cancellationToken).ConfigureAwait(false);
                if (results is { AssemblyStream: MemoryStream assemblyStream })
                {
                    results.Position = 0;
                    MemoryStream result = results.AssemblyStream;
                    await using (ZipArchive archive = new(result, ZipArchiveMode.Create, leaveOpen: true) { Comment = "SharpScript Assembly" })
                    {
                        ZipArchiveEntry assemblyEntry = archive.CreateEntry($"{results.AssemblyName}.dll", CompressionLevel.Fastest);
                        await using (Stream entryStream = assemblyEntry.Open())
                        {
                            await assemblyStream.CopyToAsync(entryStream, cancellationToken).ConfigureAwait(false);
                        }
                        if (results.SymbolStream is MemoryStream symbol)
                        {
                            ZipArchiveEntry symbolEntry = archive.CreateEntry($"{results.AssemblyName}.pdb", CompressionLevel.Fastest);
                            await using Stream entryStream = symbolEntry.Open();
                            await symbol.CopyToAsync(entryStream, cancellationToken).ConfigureAwait(false);
                        }
                        if (results.DocumentationStream is MemoryStream document)
                        {
                            ZipArchiveEntry documentEntry = archive.CreateEntry($"{results.AssemblyName}.xml", CompressionLevel.Fastest);
                            await using Stream entryStream = documentEntry.Open();
                            await document.CopyToAsync(entryStream, cancellationToken).ConfigureAwait(false);
                        }
                    }
                    result.Seek(0, SeekOrigin.Begin);
                    return result;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Compilate or {type} assembly failed. {message} (0x{hResult:X})", OutputType == OutputType.Run ? "execute" : "decompile", ex.GetMessage(), ex.HResult);
            }
            return null;
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
        CSharp = 0b0011,
        VisualBasic = 0b0111,
        IL = 0b0001,
        Run = 0b1000
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
        public static SourceCodeKind SourceCodeKind { get; set; } = SourceCodeKind.Regular;

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
                    CSharpLanguageVersion version = csharp.LanguageVersion;
                    NullableContextOptions nullable = version >= CSharpLanguageVersion.CSharp8
                        ? NullableContextOptions.Enable
                        : NullableContextOptions.Disable;
                    compilation = new CSharpCompilationOptions(
                        isConsole ? OutputKind.ConsoleApplication : OutputKind.DynamicallyLinkedLibrary,
                        optimizationLevel: OptimizationLevel.Release,
                        allowUnsafe: true,
                        nullableContextOptions: nullable);
                    parse = new CSharpParseOptions(
                        version,
                        DocumentationMode.Parse,
                        SourceCodeKind);
                    break;
                case VisualBasicInputOptions vb:
                    compilation = new VisualBasicCompilationOptions(
                        isConsole ? OutputKind.ConsoleApplication : OutputKind.DynamicallyLinkedLibrary);
                    parse = new VisualBasicParseOptions(
                        vb.LanguageVersion,
                        DocumentationMode.Parse,
                        SourceCodeKind);
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
