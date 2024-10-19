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
using Microsoft.VisualBasic;
using Mobius.ILasm.Core;
using SharpScript.Common;
using SharpScript.Helpers;
using System;
using System.Buffers;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.UI.Core;
using CSharpLanguageVersion = Microsoft.CodeAnalysis.CSharp.LanguageVersion;
using CSharpSyntaxFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using Enumerable = System.Linq.Enumerable;
using Expression = System.Linq.Expressions.Expression;
using LanguageVersion = ICSharpCode.Decompiler.CSharp.LanguageVersion;
using SyntaxTree = Microsoft.CodeAnalysis.SyntaxTree;
using VisualBasicLanguageVersion = Microsoft.CodeAnalysis.VisualBasic.LanguageVersion;
using VisualBasicSyntaxFactory = Microsoft.CodeAnalysis.VisualBasic.SyntaxFactory;

namespace SharpScript.ViewModels
{
    public partial class EditorViewModel(CoreDispatcher dispatcher) : INotifyPropertyChanged
    {
        private static readonly MetadataReference[] references =
            GetMetadataReferences(
                typeof(object).Assembly,
                typeof(Console).Assembly,
                typeof(Regex).Assembly,
                typeof(Enumerable).Assembly,
                typeof(Expression).Assembly,
                typeof(EndPoint).Assembly,
                typeof(HttpClient).Assembly,
                typeof(Uri).Assembly,
                typeof(Component).Assembly,
                typeof(Partitioner).Assembly,
                typeof(CollectionBase).Assembly,
                typeof(Binder).Assembly,
                typeof(VBMath).Assembly,
                typeof(WebClient).Assembly)
            .ToArray();

        public static LanguageType[] LanguageTypes { get; } = Enum.GetValues<LanguageType>();
        public static OutputType[] OutputTypes { get; } = Enum.GetValues<OutputType>();

        public CoreDispatcher Dispatcher { get; } = dispatcher;

        public CompilateOptions Options
        {
            get => field;
            set => SetProperty(ref field, value);
        } = new(dispatcher);

        public List<string> Diagnostics
        {
            get => field;
            set => SetProperty(ref field, value);
        }

        public bool IsDecompile
        {
            get => field;
            set => SetProperty(ref field, value);
        }

        public string Decompiled
        {
            get => field;
            set => SetProperty(ref field, value);
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

        private async Task<Streams> CompilateAsync(string code)
        {
            List<string> results = [];
            try
            {
                Diagnostics = ["Compilating..."];
                await ThreadSwitcher.ResumeBackgroundAsync();
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
                SettingsHelper.Set(SettingsHelper.CachedCode, code);
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
            IList<MetadataReference> references = AddReferences(ref code);
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
                        allowUnsafe: true));
            return compilation;
        }

        private static Compilation GetRoslynCompilate(string code, VisualBasicInputOptions options, bool isExe)
        {
            IList<MetadataReference> references = AddReferences(ref code);
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
                        isExe ? OutputKind.ConsoleApplication : OutputKind.DynamicallyLinkedLibrary));
            return compilation;
        }

        private static IList<MetadataReference> AddReferences(ref string code)
        {
            if (code.StartsWith("#r ", StringComparison.OrdinalIgnoreCase))
            {
                using StringReader reader = new(code);
                List<MetadataReference> references = [.. EditorViewModel.references];
                while (reader.Peek() > 0)
                {
                    string line = reader.ReadLine();
                    if (line.StartsWith("#r ", StringComparison.OrdinalIgnoreCase))
                    {
                        string path = line[3..].Trim(' ', '\'', '"');
                        Assembly assembly = Assembly.Load(path);
                        if (TryCreateMetadataReference(assembly, out PortableExecutableReference reference))
                        {
                            references.Add(reference);
                        }
                    }
                    else
                    {
                        code = line;
                        break;
                    }
                }
                code += reader.ReadToEnd();
                return references;
            }
            return references;
        }

        private static Streams ILCompilate(string code, ICollection<string> results, bool isExe)
        {
            Logger logger = new(results);
            Driver driver = new(logger, isExe ? Driver.Target.Exe : Driver.Target.Dll, false, false, false);

            try
            {
                MemoryStream assemblyStream = new();
                if (driver.Assemble([code], assemblyStream))
                {
                    assemblyStream.Seek(0, SeekOrigin.Begin);
                    return new Streams(assemblyStream, null);
                }
            }
            catch (Exception ex) when (ex.GetType().Name.StartsWith("yy"))
            {
                return null;
            }

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
                await ThreadSwitcher.ResumeBackgroundAsync();
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
                SettingsHelper.LogManager.GetLogger(nameof(EditorViewModel)).Error(ex.ExceptionToMessage(), ex);
            }
        }

        private static IEnumerable<MetadataReference> GetMetadataReferences(params Assembly[] assemblies)
        {
            foreach (Assembly assembly in assemblies)
            {
                if (TryCreateMetadataReference(assembly, out PortableExecutableReference reference))
                {
                    yield return reference;
                }
            }
        }

        private static unsafe bool TryCreateMetadataReference(Assembly assembly, out PortableExecutableReference reference)
        {
            if (assembly.TryGetRawMetadata(out byte* metadata, out int length))
            {
                ModuleMetadata moduleMetadata = ModuleMetadata.CreateFromMetadata((nint)metadata, length);
                AssemblyMetadata assemblyMetadata = AssemblyMetadata.Create(moduleMetadata);
                reference = assemblyMetadata.GetReference();
                return true;
            }
            reference = null;
            return false;
        }

        public static bool BoolNegationConverter(bool value) => !value;

        public static bool CollectionToBoolConverter(ICollection value) => value?.Count > 0;

        private class Logger(ICollection<string> results) : Mobius.ILasm.interfaces.ILogger
        {
            public void Info(string message) => results.Add($"{nameof(Info)}: {message}");

            public void Warning(string message) => results.Add($"{nameof(Warning)}: {message}");

            public void Error(string message) => results.Add($"{nameof(Error)}: {message}");

            public void Warning(Mono.ILASM.Location location, string message) => results.Add($"Current {location}: {nameof(Warning)}: {message}");

            public void Error(Mono.ILASM.Location location, string message) => results.Add($"Current {location}: {nameof(Error)}: {message}");
        }

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

    public partial class CompilateOptions(CoreDispatcher dispatcher) : INotifyPropertyChanged
    {
        public CoreDispatcher Dispatcher { get; } = dispatcher;

        public LanguageType LanguageType
        {
            get => field;
            set
            {
                bool isChanged;
                if (isChanged = field != value || InputOptions == null)
                {
                    InputOptions = value switch
                    {
                        LanguageType.CSharp => new CSharpInputOptions(Dispatcher),
                        LanguageType.VisualBasic => new VisualBasicInputOptions(Dispatcher),
                        LanguageType.IL => new ILInputOptions(Dispatcher),
                        _ => throw new Exception("Invalid language type."),
                    };
                    if (isChanged)
                    {
                        field = value;
                        RaisePropertyChangedEvent(
                            nameof(LanguageType),
                            nameof(LanguageName));
                    }
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

        public InputOptions InputOptions
        {
            get => field;
            private set => SetProperty(ref field, value);
        }

        public OutputType OutputType
        {
            get => field;
            set
            {
                bool isChanged;
                if (isChanged = field != value || OutputOptions == null)
                {
                    OutputOptions = value switch
                    {
                        OutputType.CSharp => new CSharpOutputOptions(Dispatcher),
                        OutputType.IL => new ILOutputOptions(Dispatcher),
                        OutputType.Run => new RunOutputOptions(Dispatcher),
                        _ => throw new Exception("Invalid output type."),
                    };
                    if (isChanged)
                    {
                        field = value;
                        RaisePropertyChangedEvent();
                    }
                }
            }
        }

        public OutputOptions OutputOptions
        {
            get => field;
            private set => SetProperty(ref field, value);
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

        protected async void RaisePropertyChangedEvent(params string[] names)
        {
            if (names?.Length > 0 && PropertyChanged != null)
            {
                await Dispatcher.ResumeForegroundAsync();
                foreach (string name in names)
                {
                    PropertyChanged.Invoke(this, new PropertyChangedEventArgs(name));
                }
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

    public interface IInputOptions : INotifyPropertyChanged
    {
        Array LanguageVersions => null;
        Enum LanguageVersion { get => null; set { } }
    }

    public abstract partial class InputOptions(CoreDispatcher dispatcher) : IInputOptions
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

    public sealed partial class CSharpInputOptions(CoreDispatcher dispatcher) : InputOptions(dispatcher), IInputOptions
    {
        Array IInputOptions.LanguageVersions => Enum.GetValues<CSharpLanguageVersion>();
        Enum IInputOptions.LanguageVersion
        {
            get => languageVersion;
            set => SetProperty(ref languageVersion, (CSharpLanguageVersion)(value ?? CSharpLanguageVersion.Preview));
        }

        private CSharpLanguageVersion languageVersion = CSharpLanguageVersion.Preview;
        public CSharpLanguageVersion LanguageVersion
        {
            get => languageVersion;
            set => SetProperty(ref languageVersion, value);
        }
    }

    public sealed partial class VisualBasicInputOptions(CoreDispatcher dispatcher) : InputOptions(dispatcher), IInputOptions
    {
        Array IInputOptions.LanguageVersions => Enum.GetValues<VisualBasicLanguageVersion>();
        Enum IInputOptions.LanguageVersion
        {
            get => languageVersion;
            set => SetProperty(ref languageVersion, (VisualBasicLanguageVersion)(value ?? VisualBasicLanguageVersion.Latest));
        }

        private VisualBasicLanguageVersion languageVersion = VisualBasicLanguageVersion.Latest;
        public VisualBasicLanguageVersion LanguageVersion
        {
            get => languageVersion;
            set => SetProperty(ref languageVersion, value);
        }
    }

    public sealed partial class ILInputOptions(CoreDispatcher dispatcher) : InputOptions(dispatcher);

    public interface IOutputOptions : INotifyPropertyChanged
    {
        bool IsCSharp => false;
        Enum LanguageVersion { get => default; set { } }
    }

    public abstract partial class OutputOptions(CoreDispatcher dispatcher) : IOutputOptions
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

    public sealed partial class CSharpOutputOptions(CoreDispatcher dispatcher) : OutputOptions(dispatcher), IOutputOptions
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
            get => languageVersion;
            set => SetProperty(ref languageVersion, (LanguageVersion)(value ?? LanguageVersion.CSharp1));
        }

        private LanguageVersion languageVersion = LanguageVersion.CSharp1;
        public LanguageVersion LanguageVersion
        {
            get => languageVersion;
            set => SetProperty(ref languageVersion, value);
        }
    }

    public sealed partial class ILOutputOptions(CoreDispatcher dispatcher) : OutputOptions(dispatcher);

    public sealed partial class RunOutputOptions(CoreDispatcher dispatcher) : OutputOptions(dispatcher);
}
