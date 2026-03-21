using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using SharpScript.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Runtime.Loader;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Diagnostic = SharpScript.Models.Diagnostic;

namespace SharpScript.Common
{
    public class Compiler(ILoggerFactory factory)
    {
        private static readonly SourceText EmptySourceText = SourceText.From(string.Empty);

        public static LanguageType[] LanguageTypes { get; } = Enum.GetValues<LanguageType>();
        public static OutputType[] OutputTypes { get; } = Enum.GetValues<OutputType>();

        private readonly ILogger<Compiler> _logger = factory.CreateLogger<Compiler>();

        private ICodeSession? _codeSession;
        public ICodeSession CodeSession
        {
            get
            {
                if (_codeSession == null)
                {
                    bool isConsole = outputType == OutputType.Run;
                    switch (InputOptions)
                    {
                        case RoslynOptions options:
                            _codeSession = new RoslynCodeSession(EmptySourceText, options, isConsole, factory.CreateLogger<RoslynCodeSession>());
                            break;
                        case ILInputOptions:
                            _codeSession = new ILCodeSession(EmptySourceText, isConsole);
                            break;
                    }
                }
                return _codeSession!;
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

        public string? InputLanguageVersion
        {
            get => ((IInputOptions)InputOptions).LanguageVersion?.ToString();
            set
            {
                if (!string.IsNullOrWhiteSpace(value) && ((IInputOptions)InputOptions).LanguageVersion?.GetType() is Type @enum)
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

        public string? OutputLanguageVersion
        {
            get => ((IOutputOptions)OutputOptions).LanguageVersion?.ToString();
            set
            {
                if (!string.IsNullOrWhiteSpace(value) && ((IOutputOptions)OutputOptions).LanguageVersion?.GetType() is Type @enum)
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
                        _codeSession = new RoslynCodeSession(_codeSession.SourceCode ?? EmptySourceText, options, isConsole, factory.CreateLogger<RoslynCodeSession>());
                        break;
                    case ILInputOptions:
                        _codeSession = new ILCodeSession(_codeSession.SourceCode ?? EmptySourceText, isConsole);
                        break;
                }
            }
        }

        public void ResetCode(string code) => CodeSession.ResetCode(code);

        public void ApplyChanges(params TextChanges[] changes) => CodeSession.ApplyChanges(changes);

        public ValueTask<IList<TextChange>?> FormatCodeAsync(CancellationToken cancellationToken = default) => CodeSession.FormatCodeAsync(cancellationToken);

        private async ValueTask<(CompilationResults? streams, List<Diagnostic> diagnostics)> CompilateAsync(CancellationToken cancellationToken = default)
        {
            List<Diagnostic> results = [];
            try
            {
                CompilationResults? streams = await CodeSession.CompileAsync(results, cancellationToken).ConfigureAwait(false);
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

        public async ValueTask<List<Diagnostic>> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
        {
            List<Diagnostic> results = [];
            try
            {
                bool isConsole = OutputType == OutputType.Run;
                results = await CodeSession.GetDiagnosticsAsync(results, cancellationToken).ConfigureAwait(false);
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

        public ValueTask<IEnumerable<RoslynCompletionItem>> GetCompletionsAsync(int position, CancellationToken cancellationToken = default) => CodeSession.GetCompletionsAsync(position, cancellationToken);

        public ValueTask<InfoTipItem> GetInfoTipAsync(int position, CancellationToken cancellationToken = default) => CodeSession.GetInfoTipAsync(position, cancellationToken);

        public ValueTask<AstNodeItem?> GetAstAsync(CancellationToken cancellationToken = default) => CodeSession.GetAstAsync(cancellationToken);

        private async ValueTask<string> DecompileAsync(CompilationResults streams, CancellationToken cancellationToken = default)
        {
            try
            {
                return OutputOptions switch
                {
                    CSharpOutputOptions csharp => await Decompiler.CSharpDecompileAsync(streams, csharp, cancellationToken).ConfigureAwait(false),
                    ILOutputOptions => await Decompiler.ILDecompileAsync(streams).ConfigureAwait(false),
                    _ => throw new Exception("Invalid output type.")
                };
            }
            finally
            {
                await streams.DisposeAsync().ConfigureAwait(false);
            }
        }

        [StackTraceHidden]
        private static async ValueTask<List<string>> ExecuteAsync(CompilationResults streams)
        {
            List<string> results = [];
            try
            {
                SynchronizationContext? previousContext = SynchronizationContext.Current;
                SynchronizationContext.SetSynchronizationContext(null);
                AssemblyLoadContext context = new("ExecutorContext", isCollectible: true);
                try
                {
                    MemoryStream assemblyStream = streams.AssemblyStream;
                    assemblyStream.Position = 0;
                    Assembly assembly = context.LoadFromStream(assemblyStream);
                    if (assembly.EntryPoint is MethodInfo main)
                    {
                        main = GetEntryPoint(main);
                        string[][]? args = main.GetParameters().Length > 0 ? [[]] : null;
                        TextWriter temp = Console.Out;
                        StringBuilder output = new();
                        await using StringWriter writer = new(output);
                        object? @return;
                        try
                        {
                            Console.SetOut(writer);
                            @return = main.Invoke(null, args);
                            switch (@return)
                            {
                                case Task<int> taskInt:
                                    @return = await taskInt.ConfigureAwait(false);
                                    break;
                                case Task<object> taskObject:
                                    @return = await taskObject.ConfigureAwait(false);
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
                                byte[]? bytes = main.GetMethodBody()?.GetILAsByteArray();
                                MethodBase? method = bytes switch
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
                                    [
                                        (byte)ILOpCode.Newobj, _, _, _, _,
                                        (byte)ILOpCode.Callvirt, _, _, _, _,
                                        (byte)ILOpCode.Callvirt, _, _, _, _,
                                        (byte)ILOpCode.Stloc_0,
                                        (byte)ILOpCode.Ldloca_s, 0,
                                        (byte)ILOpCode.Call, _, _, _, _,
                                        (byte)ILOpCode.Pop,
                                        (byte)ILOpCode.Ret
                                    ] or [
                                        (byte)ILOpCode.Newobj, _, _, _, _,
                                        (byte)ILOpCode.Callvirt, _, _, _, _,
                                        (byte)ILOpCode.Callvirt, _, _, _, _,
                                        (byte)ILOpCode.Stloc_0,
                                        (byte)ILOpCode.Ldloca_s, 0,
                                        (byte)ILOpCode.Call, _, _, _, _,
                                        (byte)ILOpCode.Ret
                                    ] => CreateScriptMain(main, bytes),
                                    [
                                        (byte)ILOpCode.Ldarg_0,
                                        (byte)ILOpCode.Call, _, _, _, _,
                                        (byte)ILOpCode.Call, _, _, _, _,
                                        (byte)ILOpCode.Ret
                                    ] => main.Module.ResolveMethod(BitConverter.ToInt32(bytes.AsSpan(2, 4))),
                                    [
                                        (byte)ILOpCode.Call, _, _, _, _,
                                        (byte)ILOpCode.Call, _, _, _, _,
                                        (byte)ILOpCode.Ret
                                    ] => main.Module.ResolveMethod(BitConverter.ToInt32(bytes.AsSpan(1, 4))),
                                    _ => null,
                                };
                                static DynamicMethod? CreateScriptMain(MethodInfo main, byte[] bytes)
                                {
                                    ConstructorInfo? constructor = main.Module.ResolveMethod(BitConverter.ToInt32(bytes.AsSpan(1, 4))) as ConstructorInfo;
                                    if (constructor == null) { return null; }
                                    MethodInfo? initialize = main.Module.ResolveMethod(BitConverter.ToInt32(bytes.AsSpan(6, 4))) as MethodInfo;
                                    if (initialize == null) { return null; }
                                    DynamicMethod method = new(main.Name, initialize.ReturnType, [], main.Module);
                                    ILGenerator generator = method.GetILGenerator();
                                    generator.Emit(OpCodes.Newobj, constructor);
                                    generator.Emit(OpCodes.Callvirt, initialize);
                                    generator.Emit(OpCodes.Ret);
                                    return method;
                                }
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
                    SynchronizationContext.SetSynchronizationContext(previousContext);
                    await streams.DisposeAsync().ConfigureAwait(false);
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

        public async ValueTask<CompileResult> ProcessAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                (CompilationResults? assemblyStream, List<Diagnostic> diagnostics) = await CompilateAsync(cancellationToken).ConfigureAwait(false);
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

        public async ValueTask<MemoryStream?> GetAssemblyAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                (CompilationResults? results, _) = await CompilateAsync(cancellationToken).ConfigureAwait(false);
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

    public record struct CompileResult(List<Diagnostic> Diagnostics, string? Decompiled, params List<string> Outputs);
}
