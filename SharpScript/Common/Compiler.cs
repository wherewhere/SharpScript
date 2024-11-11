using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.CSharp.OutputVisitor;
using ICSharpCode.Decompiler.CSharp.Syntax;
using ICSharpCode.Decompiler.Disassembler;
using ICSharpCode.Decompiler.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.VisualBasic;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text;
using System.Threading.Tasks;
using CSharpLanguageVersion = Microsoft.CodeAnalysis.CSharp.LanguageVersion;
using CSharpSyntaxFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using LanguageVersion = ICSharpCode.Decompiler.CSharp.LanguageVersion;
using SyntaxTree = Microsoft.CodeAnalysis.SyntaxTree;
using VisualBasicLanguageVersion = Microsoft.CodeAnalysis.VisualBasic.LanguageVersion;
using VisualBasicSyntaxFactory = Microsoft.CodeAnalysis.VisualBasic.SyntaxFactory;

namespace SharpScript.Common
{
    public class Compiler
    {
        private static List<MetadataReference> references;

        public static LanguageType[] LanguageTypes { get; } = Enum.GetValues<LanguageType>();
        public static OutputType[] OutputTypes { get; } = Enum.GetValues<OutputType>();

        public CompilateOptions Options { get; set; } = new();

        public List<string> Diagnostics { get; set; }

        public bool IsDecompile { get; set; } = false;

        public string Decompiled { get; set; }

        public static async ValueTask InitAsync(string baseUrl)
        {
            if (references?.Count is not > 0)
            {
                references = await GetMetadataReferencesAsync(
                    baseUrl,
                    "System.Runtime",
                    "System.Private.CoreLib",
                    "System.Console",
                    "System.Text.RegularExpressions",
                    "System.Linq",
                    "System.Linq.Expressions",
                    "System.Net.Primitives",
                    "System.Net.Http",
                    "System.Private.Uri",
                    "System.ComponentModel.Primitives",
                    "System.Collections.Concurrent",
                    "System.Collections.NonGeneric",
                    "Microsoft.CSharp",
                    "Microsoft.VisualBasic.Core",
                    "System.Net.WebClient").ConfigureAwait(false);
            }
        }

        private async Task<Streams> CompilateAsync(string code)
        {
            List<string> results = [];
            try
            {
                Diagnostics = ["Compilating..."];
                await Task.Yield();
                bool isExe = Options.OutputType == OutputType.Run;
                return Options.LanguageType switch
                {
                    LanguageType.CSharp or LanguageType.VisualBasic => RoslynCompilate(code, Options, results, isExe),
                    LanguageType.IL => ILCompilate(code, results, isExe),
                    _ => throw new Exception("Invalid language type.")
                };
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
                //SettingsHelper.Set(SettingsHelper.CachedCode, code);
                Diagnostics = results;
                GC.Collect();
            }
            return null;
        }

        private static Streams RoslynCompilate(string code, CompilateOptions options, ICollection<string> results, bool isExe)
        {
            MemoryStream assemblyStream = new();
            MemoryStream symbolStream = new();
        start:
            Compilation compilation = options.InputOptions switch
            {
                CSharpInputOptions csharp => GetRoslynCompilate(code, csharp, isExe),
                VisualBasicInputOptions vb => GetRoslynCompilate(code, vb, isExe),
                _ => throw new Exception("Invalid language type.")
            };
            EmitResult emitResult = compilation.Emit(assemblyStream, symbolStream);
            if (emitResult.Success)
            {
                assemblyStream.Seek(0, SeekOrigin.Begin);
                symbolStream.Seek(0, SeekOrigin.Begin);
                return new Streams(assemblyStream, symbolStream);
            }
            else
            {
                if (!isExe && options.LanguageType == LanguageType.CSharp
                    && emitResult.Diagnostics.Any(x => x.Id == "CS8805" && x.Severity == DiagnosticSeverity.Error))
                {
                    isExe = true;
                    goto start;
                }
                results.AddRange(emitResult.Diagnostics.Select(x =>
                {
                    FileLinePositionSpan line = x.Location.GetLineSpan();
                    return $"{(string.IsNullOrEmpty(line.Path) ? "Current" : string.Empty)}{x.Location.GetLineSpan()}: {x.Severity} {x.Id}: {x.GetMessage()}";
                }));
                return null;
            }
        }

        private static Compilation GetRoslynCompilate(string code, CSharpInputOptions options, bool isExe)
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
                        isExe ? OutputKind.ConsoleApplication : OutputKind.DynamicallyLinkedLibrary,
                        allowUnsafe: true,
                        concurrentBuild: false));
            return compilation;
        }

        private static Compilation GetRoslynCompilate(string code, VisualBasicInputOptions options, bool isExe)
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
                    new VisualBasicCompilationOptions(
                        isExe ? OutputKind.ConsoleApplication : OutputKind.DynamicallyLinkedLibrary,
                        concurrentBuild: false));
            return compilation;
        }

        private static Streams ILCompilate(string code, ICollection<string> results, bool isExe)
        {
            //Logger logger = new(results);
            //Driver driver = new(logger, isExe ? Driver.Target.Exe : Driver.Target.Dll, false, false, false);

            //try
            //{
            //    MemoryStream assemblyStream = new();
            //    if (driver.Assemble([code], assemblyStream))
            //    {
            //        assemblyStream.Seek(0, SeekOrigin.Begin);
            //        return new Streams(assemblyStream, null);
            //    }
            //}
            //catch (Exception ex) when (ex.GetType().Name.StartsWith("yy"))
            //{
            //    return null;
            //}

            return null;
        }

        private async Task DecompileAsync(Streams streams)
        {
            Diagnostics = ["Decompiling..."];
            Decompiled = Options.OutputOptions switch
            {
                CSharpOutputOptions csharp => await CSharpDecompileAsync(streams, csharp).ConfigureAwait(false),
                ILOutputOptions => await ILDecompileAsync(streams).ConfigureAwait(false),
                _ => throw new Exception("Invalid output type.")
            };
            IsDecompile = true;
        }

        private static async Task<string> CSharpDecompileAsync(Streams streams, CSharpOutputOptions options)
        {
            using PEFile assemblyFile = new("", streams.AssemblyStream);
            PortablePdbDebugInfoProvider debugInfo = null;
            try
            {
                //try { debugInfo = streams.SymbolStream != null ? new PortablePdbDebugInfoProvider(streams.SymbolStream) : null; }
                //catch { }

                CSharpDecompiler decompiler =
                    new(assemblyFile,
                        new PreCachedAssemblyResolver(references),
                        new DecompilerSettings(options.LanguageVersion))
                    {
                        DebugInfoProvider = debugInfo
                    };
                ICSharpCode.Decompiler.CSharp.Syntax.SyntaxTree syntaxTree = decompiler.DecompileWholeModuleAsSingleFile();

                SortTree(syntaxTree);

                StringBuilder code = new();
                await using StringWriter codeWriter = new(code);
                new ExtendedCSharpOutputVisitor(codeWriter, CreateFormattingOptions())
                    .VisitSyntaxTree(syntaxTree);
                return code.ToString();
            }
            finally
            {
                debugInfo?.Dispose();
            }
        }

        private static void SortTree(ICSharpCode.Decompiler.CSharp.Syntax.SyntaxTree root)
        {
            // Note: the sorting logic cannot be reused, but should match IL and Jit ASM ordering
            AstNode firstMovedNode = null;
            foreach (AstNode node in root.Children)
            {
                if (node == firstMovedNode) { break; }
                if (node is NamespaceDeclaration @namespace && IsNonUserCode(@namespace))
                {
                    node.Remove();
                    root.AddChildWithExistingRole(node);
                    firstMovedNode ??= node;
                }
            }
        }

        private static bool IsNonUserCode(NamespaceDeclaration @namespace) =>
            // Note: the logic cannot be reused, but should match IL and Jit ASM
            @namespace.Members.Any(member => member is not TypeDeclaration type || !IsCompilerGenerated(type));

        private static bool IsCompilerGenerated(TypeDeclaration type) =>
            type.Attributes.Any(section => section.Attributes.Any(attribute => attribute.Type is SimpleType { Identifier: nameof(CompilerGeneratedAttribute) or "CompilerGenerated" }));

        private static CSharpFormattingOptions CreateFormattingOptions()
        {
            CSharpFormattingOptions options = FormattingOptionsFactory.CreateAllman();
            options.IndentationString = "    ";
            options.MinimumBlankLinesBetweenTypes = 1;
            return options;
        }

        private static async Task<string> ILDecompileAsync(Streams streams)
        {
            using PEFile assemblyFile = new("", streams.AssemblyStream);
            PortablePdbDebugInfoProvider debugInfo = null;
            try
            {
                //try { debugInfo = streams.SymbolStream != null ? new PortablePdbDebugInfoProvider(streams.SymbolStream) : null; }
                //catch { }

                StringBuilder code = new();
                await using StringWriter codeWriter = new(code);

                PlainTextOutput output = new(codeWriter) { IndentationString = "    " };
                ReflectionDisassembler disassembler = new(output, default)
                {
                    DebugInfo = debugInfo,
                    ShowSequencePoints = true
                };

                disassembler.WriteAssemblyHeader(assemblyFile);
                output.WriteLine(); // empty line

                MetadataReader metadata = assemblyFile.Metadata;
                DecompileTypes(assemblyFile, output, disassembler, metadata);
                return code.ToString();
            }
            finally
            {
                debugInfo?.Dispose();
            }
        }

        private static void DecompileTypes(PEFile assemblyFile, PlainTextOutput output, ReflectionDisassembler disassembler, MetadataReader metadata)
        {
            const int MaxNonUserTypeHandles = 10;
            TypeDefinitionHandle[] nonUserTypeHandlesLease = default;
            int nonUserTypeHandlesCount = -1;

            // user code (first)                
            foreach (TypeDefinitionHandle typeHandle in metadata.TypeDefinitions)
            {
                TypeDefinition type = metadata.GetTypeDefinition(typeHandle);
                if (!type.GetDeclaringType().IsNil)
                {
                    continue; // not a top-level type
                }

                if (IsNonUserCode(metadata, type) && nonUserTypeHandlesCount < MaxNonUserTypeHandles)
                {
                    if (nonUserTypeHandlesCount == -1)
                    {
                        nonUserTypeHandlesLease = new TypeDefinitionHandle[MaxNonUserTypeHandles];
                        nonUserTypeHandlesCount = 0;
                    }

                    nonUserTypeHandlesLease[nonUserTypeHandlesCount] = typeHandle;
                    nonUserTypeHandlesCount += 1;
                    continue;
                }

                disassembler.DisassembleType(assemblyFile, typeHandle);
                output.WriteLine();
            }

            // non-user code (second)
            if (nonUserTypeHandlesCount > 0)
            {
                foreach (TypeDefinitionHandle typeHandle in nonUserTypeHandlesLease[..nonUserTypeHandlesCount])
                {
                    disassembler.DisassembleType(assemblyFile, typeHandle);
                    output.WriteLine();
                }
            }
        }

        private static bool IsNonUserCode(MetadataReader metadata, TypeDefinition type) =>
            // Note: the logic cannot be reused, but should match C# and Jit ASM
            !type.NamespaceDefinition.IsNil && type.IsCompilerGenerated(metadata);

        private async Task ExecuteAsync(Streams streams)
        {
            bool finished = false;
            List<string> results = [];
            StringBuilder output = new();
            try
            {
                Diagnostics = ["Executing..."];
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
            try
            {
                IsDecompile = false;
                if (await CompilateAsync(code).ConfigureAwait(false) is Streams assemblyStream)
                {
                    switch (Options.OutputType)
                    {
                        case OutputType.CSharp
                            or OutputType.IL:
                            await DecompileAsync(assemblyStream).ConfigureAwait(false);
                            break;
                        case OutputType.Run:
                            await ExecuteAsync(assemblyStream).ConfigureAwait(false);
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
            }
        }

        private static async ValueTask<List<MetadataReference>> GetMetadataReferencesAsync(string baseUrl, params string[] assemblies)
        {
            List<MetadataReference> references = [];
            using HttpClient client = new() { BaseAddress = new Uri(baseUrl) };
            foreach (string assembly in assemblies)
            {
                using Stream stream = await client.GetStreamAsync($"{assembly}.dll").ConfigureAwait(false);
                references.Add(MetadataReference.CreateFromStream(stream));
            }
            return references;
        }

        //private class Logger(ICollection<string> results) : Mobius.ILasm.interfaces.ILogger
        //{
        //    public void Info(string message) => results.Add($"{nameof(Info)}: {message}");

        //    public void Warning(string message) => results.Add($"{nameof(Warning)}: {message}");

        //    public void Error(string message) => results.Add($"{nameof(Error)}: {message}");

        //    public void Warning(Mono.ILASM.Location location, string message) => results.Add($"Current {location}: {nameof(Warning)}: {message}");

        //    public void Error(Mono.ILASM.Location location, string message) => results.Add($"Current {location}: {nameof(Error)}: {message}");
        //}

        private partial record Streams(MemoryStream AssemblyStream, MemoryStream SymbolStream) : IDisposable
        {
            public void Dispose()
            {
                AssemblyStream?.Dispose();
                SymbolStream?.Dispose();
                GC.SuppressFinalize(this);
            }
        }

        private class ExtendedCSharpOutputVisitor(TextWriter textWriter, CSharpFormattingOptions formattingPolicy) : CSharpOutputVisitor(textWriter, formattingPolicy)
        {
            public override void VisitTypeDeclaration(TypeDeclaration typeDeclaration)
            {
                base.VisitTypeDeclaration(typeDeclaration);
                if (typeDeclaration.NextSibling is NamespaceDeclaration or TypeDeclaration)
                { NewLine(); }
            }

            public override void VisitNamespaceDeclaration(NamespaceDeclaration namespaceDeclaration)
            {
                base.VisitNamespaceDeclaration(namespaceDeclaration);
                if (namespaceDeclaration.NextSibling is NamespaceDeclaration or TypeDeclaration)
                { NewLine(); }
            }

            public override void VisitAttributeSection(AttributeSection attributeSection)
            {
                base.VisitAttributeSection(attributeSection);
                if (attributeSection is { AttributeTarget: "assembly" or "module", NextSibling: not AttributeSection { AttributeTarget: "assembly" or "module" } })
                { NewLine(); }
            }
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
        Run
    }

    public partial class CompilateOptions()
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
        Array LanguageVersions => null;
        Enum LanguageVersion { get => null; set { } }
    }

    public abstract partial class InputOptions : IInputOptions;

    public sealed partial class CSharpInputOptions : InputOptions, IInputOptions
    {
        Array IInputOptions.LanguageVersions => Enum.GetValues<CSharpLanguageVersion>();
        Enum IInputOptions.LanguageVersion
        {
            get => LanguageVersion;
            set => LanguageVersion = (CSharpLanguageVersion)(value ?? CSharpLanguageVersion.Preview);
        }

        public CSharpLanguageVersion LanguageVersion { get; set; } = CSharpLanguageVersion.Preview;
    }

    public sealed partial class VisualBasicInputOptions : InputOptions, IInputOptions
    {
        Array IInputOptions.LanguageVersions => Enum.GetValues<VisualBasicLanguageVersion>();
        Enum IInputOptions.LanguageVersion
        {
            get => LanguageVersion;
            set => LanguageVersion = (VisualBasicLanguageVersion)(value ?? VisualBasicLanguageVersion.Latest);
        }

        public VisualBasicLanguageVersion LanguageVersion { get; set; } = VisualBasicLanguageVersion.Latest;
    }

    public sealed partial class ILInputOptions : InputOptions;

    public interface IOutputOptions
    {
        bool IsCSharp => false;
        Enum LanguageVersion { get => default; set { } }
    }

    public abstract partial class OutputOptions : IOutputOptions;

    public sealed partial class CSharpOutputOptions : OutputOptions, IOutputOptions
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

    public sealed partial class ILOutputOptions : OutputOptions;

    public sealed partial class RunOutputOptions : OutputOptions;
}
