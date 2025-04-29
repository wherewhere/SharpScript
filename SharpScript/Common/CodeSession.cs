using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.QuickInfo;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using Mobius.ILasm.Core;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using CSharpLanguageVersion = Microsoft.CodeAnalysis.CSharp.LanguageVersion;
using RoslynCodeAction = Microsoft.CodeAnalysis.CodeActions.CodeAction;
using RoslynCompletionItem = Microsoft.CodeAnalysis.Completion.CompletionItem;
using RoslynDiagnostic = Microsoft.CodeAnalysis.Diagnostic;

namespace SharpScript.Common
{
    public sealed partial class RoslynCodeSession : ICodeSession<RoslynCodeSession>
    {
        private static readonly SourceText EmptySourceText = SourceText.From(string.Empty);
        private static string _baseUrl;

        private readonly Dictionary<string, List<CodeFixProvider>> _providers;
        private readonly ImmutableArray<DiagnosticAnalyzer> _analyzers;
        private readonly RoslynOptions _options;
        private readonly string _language;
        private readonly bool _isConsole;
        private bool _outOfDate;

        internal readonly ILogger<RoslynCodeSession> _logger;

        public static List<MetadataReference> References { get; private set; } = [];

        private RoslynCodeSession _consoleVersion;
        private RoslynCodeSession ConsoleVersion
        {
            get
            {
                if (_isConsole) { return this; }
                _consoleVersion ??= WithIsConsole(true);
                return _consoleVersion;
            }
        }

        public AdhocWorkspace Workspace { get; }

        private string _code;
        public string SourceCode
        {
            get => _code;
            set
            {
                if (_code != value)
                {
                    _code = value;
                    SourceText = SourceText.From(value, Encoding.Default);
                    _outOfDate = true;
                    EnsureUpToDate();
                }
            }
        }

        public SourceText SourceText { get; private set; }

        private Document _currentDocument;
        public Document CurrentDocument
        {
            get
            {
                EnsureUpToDate();
                return _currentDocument;
            }
        }

        private CompletionService _completionService;
        public CompletionService CompletionService
        {
            get
            {
                EnsureUpToDate();
                _completionService ??= CompletionService.GetService(CurrentDocument);

                if (_completionService == null)
                {
                    _logger.LogWarning("Could not find completion service for document '{name}'.", CurrentDocument.Name);
                }

                return _completionService;
            }
        }

        private QuickInfoService _quickInfoService;
        public QuickInfoService QuickInfoService
        {
            get
            {
                EnsureUpToDate();
                _quickInfoService ??= QuickInfoService.GetService(CurrentDocument);

                if (_quickInfoService == null)
                {
                    _logger.LogWarning("Could not find quick info service for document '{name}'.", CurrentDocument.Name);
                }

                return _quickInfoService;
            }
        }

        public RoslynCodeSession(string code, RoslynOptions options, bool isConsole, ILogger<RoslynCodeSession> logger = null)
        {
            _options = options;
            _isConsole = isConsole;
            _logger = logger ?? NullLogger<RoslynCodeSession>.Instance;
            _code = code ?? string.Empty;
            SourceText = code == null ? EmptySourceText : SourceText.From(code, Encoding.Default);
            Workspace = new AdhocWorkspace();
            ProjectId projectId = ProjectId.CreateNewId();
            DocumentId docId = DocumentId.CreateNewId(projectId, "SharpScript.CodeSession");
            options.GetOptions(isConsole, out CompilationOptions compilation, out ParseOptions parse);
            _language = compilation.Language;
            Solution solution = Workspace.CurrentSolution
                .AddProject(projectId, "SharpScript.Project.CodeSession", "SharpScript", _language)
                .AddMetadataReferences(projectId, References)
                .WithProjectCompilationOptions(projectId, compilation)
                .WithProjectParseOptions(projectId, parse)
                .AddDocument(docId, "SharpScript.CodeSession.Document", SourceText);
            _ = Workspace.TryApplyChanges(solution);
            Workspace.OpenDocument(docId);
            _currentDocument = Workspace.CurrentSolution.GetDocument(docId);
            GetAnalyzers(_language switch
            {
                LanguageNames.CSharp => "Microsoft.CodeAnalysis.CSharp.Features",
                LanguageNames.VisualBasic => "Microsoft.CodeAnalysis.VisualBasic.Features",
                _ => throw new NotSupportedException($"Language '{_language}' is not supported.")
            }, _language, out IEnumerable<DiagnosticAnalyzer> analyzers, out _providers);
            _analyzers = [.. analyzers];
        }

        public static async ValueTask InitAsync(string baseUrl, ILogger<RoslynCodeSession> logger)
        {
            _baseUrl = baseUrl;
            if (References?.Count is not > 0)
            {
                References = await GetMetadataReferencesAsync(
                    logger,
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

        private void EnsureUpToDate()
        {
            if (!_outOfDate) { return; }
            Document document = _currentDocument.WithText(SourceText);
            _ = Workspace.TryApplyChanges(document.Project.Solution);
            _currentDocument = Workspace.CurrentSolution.GetDocument(_currentDocument.Id);
            _outOfDate = false;
        }

        public RoslynCodeSession SetSourceText(string code)
        {
            SourceCode = code;
            return this;
        }

        public async ValueTask<IReadOnlyList<TextChange>> RollbackWorkspaceChangesAsync()
        {
            Project oldProject = _currentDocument.Project;
            Project newProject = Workspace.CurrentSolution.GetProject(oldProject.Id)!;
            if (newProject == oldProject)
            {
                return [];
            }

            SourceText newText = await newProject.GetDocument(_currentDocument.Id).GetTextAsync().ConfigureAwait(false);
            Workspace.TryApplyChanges(oldProject.Solution);
            _currentDocument = Workspace.CurrentSolution.GetDocument(_currentDocument.Id);

            return newText.GetTextChanges(SourceText);
        }

        public async ValueTask<T> GetDiagnosticsAsync<T>(T results, CancellationToken cancellationToken = default) where T : ICollection<Diagnostic>
        {
            Compilation compilation = await CurrentDocument.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
            ImmutableArray<RoslynDiagnostic> diagnostics = await compilation.WithAnalyzers(_analyzers).GetAllDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
            IEnumerable<RoslynDiagnostic> filtered = !_isConsole && _options is CSharpInputOptions { LanguageVersion: >= CSharpLanguageVersion.CSharp9 } ? diagnostics.Where(x => x is not { Id: "CS8805", Severity: DiagnosticSeverity.Error }) : diagnostics;
            foreach (RoslynDiagnostic diagnostic in filtered)
            {
                List<RoslynCodeAction> actions = await GetCodeActionsAsync(diagnostic, cancellationToken).ConfigureAwait(false);
                results.Add(new Diagnostic(diagnostic, [.. actions.Select(x => new CodeAction(x, this))]));
            }
            return results;
        }

        private async ValueTask<List<RoslynCodeAction>> GetCodeActionsAsync(RoslynDiagnostic diagnostic, CancellationToken cancellationToken = default)
        {
            List<RoslynCodeAction> codeActions = [];
            CodeFixContext context = new(CurrentDocument, diagnostic, (x, _) => codeActions.Add(x), cancellationToken);
            if (_providers.TryGetValue(diagnostic.Id, out List<CodeFixProvider> providers))
            {
                for (int i = providers.Count; --i >= 0;)
                {
                    CodeFixProvider provider = providers[i];
                    try
                    {
                        await provider.RegisterCodeFixesAsync(context).ConfigureAwait(false);
                    }
                    catch (TypeInitializationException ex)
                    {
                        _logger.LogError(ex, "Not supports provider '{provider}' for diagnostic '{diagnosticId}'.", provider.GetType().Name, diagnostic.Id);
                        _ = providers.Remove(provider);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error while registering code fixes for provider '{provider}' with diagnostic '{diagnosticId}'.", provider.GetType().Name, diagnostic.Id);
                    }
                }
            }
            return codeActions;
        }

        private bool ShouldTriggerCompletions(int position) => ShouldTriggerCompletions(position, '\0', CharacterOperation.None);
        private bool ShouldTriggerCompletions(int position, char @char, CharacterOperation kind = CharacterOperation.Inserted)
        {
            CompletionTrigger None(CharacterOperation operation)
            {
                _logger.LogWarning("Unexpected character operation '{operation}'. Using '{enum}.{member}' instead.", operation, nameof(CharacterOperation), nameof(CharacterOperation.None));
                return CompletionTrigger.Invoke;
            }

            CompletionTrigger trigger = kind switch
            {
                CharacterOperation.None => CompletionTrigger.Invoke,
                CharacterOperation.Inserted => CompletionTrigger.CreateInsertionTrigger(@char),
                CharacterOperation.Deleted => CompletionTrigger.CreateDeletionTrigger(@char),
                _ => None(kind)
            };
            return ShouldTriggerCompletions(position, trigger);
        }

        private bool ShouldTriggerCompletions(int position, CompletionTrigger completionTrigger)
        {
            CompletionService service = CompletionService;
            return service == null || service.ShouldTriggerCompletion(SourceText, position, completionTrigger);
        }

        public async ValueTask<IEnumerable<CompletionItem>> GetCompletionsAsync(int position, CancellationToken cancellationToken = default)
        {
            if (CompletionService is not CompletionService service)
            {
                return [];
            }

            if (!ShouldTriggerCompletions(position))
            {
                _logger.LogDebug("ShouldTriggerCompletionsAsync false, skipping.");
                return [];
            }

            CompletionList completions = await service.GetCompletionsAsync(CurrentDocument, position, cancellationToken: cancellationToken).ConfigureAwait(false);
            TextSpan typedSpan = CompletionService.GetDefaultCompletionListSpan(SourceText, position);
            string typedText = SourceText.GetSubText(typedSpan).ToString();

            IReadOnlyList<RoslynCompletionItem> filteredItems = typedText.Length != 0
                ? CompletionService.FilterItems(CurrentDocument, [.. completions.ItemsList], typedText)
                : completions.ItemsList;

            return filteredItems.Select(x => new CompletionItem(x, this));
        }

        public Task<ImmutableArray<TaggedText>> GetCompletionDescriptionAsync(RoslynCompletionItem item, CancellationToken cancellationToken = default) =>
            _completionService.GetDescriptionAsync(_currentDocument, item, cancellationToken).ContinueWith(x => x.Result.TaggedParts);

        public async ValueTask<InfoTipItem> GetInfoTipAsync(int position, CancellationToken cancellationToken = default)
        {
            QuickInfoItem info = await QuickInfoService.GetQuickInfoAsync(CurrentDocument, position, cancellationToken).ConfigureAwait(false);
            if (info is null or { Sections.IsEmpty: true }) { return default; }
            return new InfoTipItem(info);
        }

        public async ValueTask<CompilationResults> Compile(ICollection<Diagnostic> results, CancellationToken cancellationToken = default)
        {
            MemoryStream assemblyStream = new();
            MemoryStream symbolStream = new();
            Compilation compilation = await CurrentDocument.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
            EmitResult emitResult = compilation.Emit(assemblyStream, symbolStream, cancellationToken: cancellationToken);
            if (emitResult.Success)
            {
                _ = assemblyStream.Seek(0, SeekOrigin.Begin);
                _ = symbolStream.Seek(0, SeekOrigin.Begin);
                return new CompilationResults(assemblyStream, symbolStream);
            }
            else
            {
                if (!_isConsole && _options is CSharpInputOptions { LanguageVersion: >= CSharpLanguageVersion.CSharp9 }
                    && emitResult.Diagnostics.Any(x => x is { Id: "CS8805", Severity: DiagnosticSeverity.Error }))
                {
                    return await ConsoleVersion.Compile(results, cancellationToken).ConfigureAwait(false);
                }
                results.AddRange(emitResult.Diagnostics.Select(x => new Diagnostic(x)));
                return null;
            }
        }

        public RoslynCodeSession WithIsConsole(bool isConsole) => new(_code, _options, isConsole, _logger);

        private static async ValueTask<List<MetadataReference>> GetMetadataReferencesAsync(ILogger<RoslynCodeSession> logger, params string[] assemblies)
        {
            List<MetadataReference> references = [];
            using HttpClient client = new() { BaseAddress = new Uri(_baseUrl) };
            BlazorBoot boot = await client.GetFromJsonAsync("blazor.boot.json", SourceGenerationContext.Default.BlazorBoot).ConfigureAwait(false);
            foreach (string assembly in assemblies)
            {
                try
                {
                    string fileName = $"{assembly}.wasm";
                    if (boot.Resources.FingerPrinting.FirstOrDefault(x => x.Value.Equals(fileName, StringComparison.OrdinalIgnoreCase)) is { Key.Length: > 0 } result)
                    {
                        using Stream stream = await client.GetStreamAsync(result.Key).ConfigureAwait(false);
                        byte[] array = await WebcilConverterUtil.ConvertFromWebcilAsync(stream).ConfigureAwait(false);
                        references.Add(MetadataReference.CreateFromImage(array, documentation: await CreateDocumentation().ConfigureAwait(false)));
                        async ValueTask<XmlDocumentationProvider> CreateDocumentation()
                        {
                            try
                            {
                                byte[] bytes = await client.GetByteArrayAsync($"{assembly}.xml");
                                if (bytes?.Length > 0)
                                {
                                    return XmlDocumentationProvider.CreateFromBytes(bytes);
                                }
                            }
                            catch (Exception ex)
                            {
                                logger.LogTrace(ex, "The documentation for '{assembly}' was not found.", assembly);
                            }
                            return null;
                        }
                    }
                    else
                    {
                        logger.LogWarning("The assembly '{assembly}' was not found.", assembly);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "The reference for '{assembly}' was not found.", assembly);
                }
            }
            return references;
        }

        private static void GetAnalyzers(string assemblyName, string language, out IEnumerable<DiagnosticAnalyzer> analyzers, out Dictionary<string, List<CodeFixProvider>> providers)
        {
            Type[] types = Assembly.Load(new AssemblyName(assemblyName)).GetTypes();

            analyzers = types.Where(x => x.IsSubclassOf(typeof(DiagnosticAnalyzer)) && x is { IsAbstract: false } && x.GetCustomAttributes(typeof(DiagnosticAnalyzerAttribute), true).OfType<DiagnosticAnalyzerAttribute>().Any(x => x.Languages.Contains(language)))
                             .Select(Activator.CreateInstance)
                             .OfType<DiagnosticAnalyzer>();

            IEnumerable<CodeFixProvider> codeFixProvider =
                types.Where(x => x.IsSubclassOf(typeof(CodeFixProvider)) && x is { IsAbstract: false } && x.IsDefined(typeof(ExportCodeFixProviderAttribute)))
                     .Select(Activator.CreateInstance)
                     .OfType<CodeFixProvider>();

            providers = [];
            foreach (CodeFixProvider provider in codeFixProvider)
            {
                foreach (string id in provider.FixableDiagnosticIds)
                {
                    if (!providers.TryGetValue(id, out List<CodeFixProvider> list))
                    {
                        list = [];
                        providers.Add(id, list);
                    }
                    list.Add(provider);
                }
            }
        }

        //private static async ValueTask<(IList<MetadataReference> references, string code)> AddReferencesAsync(string code)
        //{
        //    if (code.StartsWith("#r ", StringComparison.OrdinalIgnoreCase))
        //    {
        //        using StringReader reader = new(code);
        //        List<MetadataReference> references = [.. CodeSession.references];
        //        HttpClient client = null;
        //        try
        //        {
        //            while (reader.Peek() > 0)
        //            {
        //                string line = reader.ReadLine();
        //                if (line.StartsWith("#r ", StringComparison.OrdinalIgnoreCase))
        //                {
        //                    string path = line[3..].Trim(' ', '\'', '"');
        //                    client ??= new() { BaseAddress = new(baseUrl) };
        //                    using Stream stream = await client.GetStreamAsync($"{path}.wasm").ConfigureAwait(false);
        //                    references.Add(MetadataReference.CreateFromImage(WebcilConverterUtil.ConvertFromWebcil(stream)));
        //                }
        //                else
        //                {
        //                    code = line;
        //                    break;
        //                }
        //            }
        //        }
        //        finally
        //        {
        //            client?.Dispose();
        //        }
        //        code += reader.ReadToEnd();
        //        return (references, code);
        //    }
        //    return (references, code);
        //}

        private sealed class BlazorBoot
        {
            [JsonPropertyName("resources")]
            public Resources Resources { get; init; }
        }

        private sealed class Resources
        {
            [JsonPropertyName("fingerprinting")]
            public Dictionary<string, string> FingerPrinting { get; init; }
        }

        [JsonSerializable(typeof(BlazorBoot))]
        private sealed partial class SourceGenerationContext : JsonSerializerContext;
    }

    public sealed class ILCodeSession(string code, bool isConsole) : ICodeSession<ILCodeSession>
    {
        private string _code = code;

        public ValueTask<CompilationResults> Compile(ICollection<Diagnostic> results, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Logger logger = new(results);
            Driver driver = new(logger, isConsole ? Driver.Target.Exe : Driver.Target.Dll, false, false, false);
            try
            {
                MemoryStream assemblyStream = new();
                if (driver.Assemble([_code], assemblyStream))
                {
                    _ = assemblyStream.Seek(0, SeekOrigin.Begin);
                    return ValueTask.FromResult(new CompilationResults(assemblyStream, null));
                }
            }
            catch (Exception ex) when (ex.GetType().Name.StartsWith("yy"))
            {
                return ValueTask.FromResult<CompilationResults>(null);
            }
            return ValueTask.FromResult<CompilationResults>(null);
        }

        public ValueTask<T> GetDiagnosticsAsync<T>(T results, CancellationToken cancellationToken = default) where T : ICollection<Diagnostic>
        {
            cancellationToken.ThrowIfCancellationRequested();
            Logger logger = new(results);
            Driver driver = new(logger, isConsole ? Driver.Target.Exe : Driver.Target.Dll, false, false, false);

            try
            {
                using MemoryStream assemblyStream = new();
                _ = driver.Assemble([_code], assemblyStream);
                return ValueTask.FromResult(results);
            }
            catch (Exception ex) when (ex.GetType().Name.StartsWith("yy"))
            {
                return ValueTask.FromResult(results);
            }
        }

        public ILCodeSession SetSourceText(string code)
        {
            _code = code;
            return this;
        }

        private class Logger(ICollection<Diagnostic> results) : Mobius.ILasm.interfaces.ILogger
        {
            public void Info(string message) => results.Add(new Diagnostic(DiagnosticSeverity.Info, message));

            public void Warning(string message) => results.Add(new Diagnostic(DiagnosticSeverity.Warning, message));

            public void Error(string message) => results.Add(new Diagnostic(DiagnosticSeverity.Error, message));

            public void Warning(Mono.ILASM.Location location, string message) => results.Add(new Diagnostic(location, DiagnosticSeverity.Warning, message));

            public void Error(Mono.ILASM.Location location, string message) => results.Add(new Diagnostic(location, DiagnosticSeverity.Error, message));
        }
    }

    public interface ICodeSession
    {
        ValueTask<T> GetDiagnosticsAsync<T>(T results, CancellationToken cancellationToken = default) where T : ICollection<Diagnostic>;
        ValueTask<IEnumerable<CompletionItem>> GetCompletionsAsync(int position, CancellationToken cancellationToken = default) => ValueTask.FromResult<IEnumerable<CompletionItem>>([]);
        ValueTask<InfoTipItem> GetInfoTipAsync(int position, CancellationToken cancellationToken = default) => ValueTask.FromResult<InfoTipItem>(default);
        ICodeSession SetSourceText(string code);
        ValueTask<CompilationResults> Compile(ICollection<Diagnostic> results, CancellationToken cancellationToken = default);
    }

    public interface ICodeSession<out TSelf> : ICodeSession where TSelf : ICodeSession
    {
        new TSelf SetSourceText(string code);
        ICodeSession ICodeSession.SetSourceText(string code) => SetSourceText(code);
    }

    public enum CharacterOperation
    {
        None = 0,
        Inserted = 1,
        Deleted = 2
    }

    public sealed class Diagnostic(DiagnosticSeverity severity, string message)
    {
        public string ID { get; }
        public LinePositionSpan Location { get; }
        public string Message => message;
        public string Severity => severity.ToString();
        public CodeAction[] Actions { get; } = [];

        public Diagnostic(Exception exception) : this(DiagnosticSeverity.Error, exception.Message) { }

        public Diagnostic(RoslynDiagnostic diagnostic, params CodeAction[] actions) : this(diagnostic.Severity, diagnostic.GetMessage())
        {
            ID = diagnostic.Id;
            Location = diagnostic.Location.GetLineSpan().Span;
            Actions = actions;
        }

        public Diagnostic(Mono.ILASM.Location location, DiagnosticSeverity severity, string message) : this(severity, message)
        {
            LinePosition position = new(location.line - 1, location.column);
            Location = new LinePositionSpan(position, position);
        }
    }

    public class CodeAction(RoslynCodeAction action, RoslynCodeSession session)
    {
        public string Title => action.Title;
        public DotNetObjectReference<CodeAction> Action => DotNetObjectReference.Create(this);

        [JSInvokable]
        public async Task<IReadOnlyList<TextChange>> InvokeAsync()
        {
            try
            {
                ImmutableArray<CodeActionOperation> operations = await action.GetOperationsAsync(default).ConfigureAwait(false);
                foreach (CodeActionOperation operation in operations)
                {
                    operation.Apply(session.Workspace, default);
                }
                return await session.RollbackWorkspaceChangesAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                session._logger.LogError(ex, "Error while applying code action '{title}'.", action.Title);
                return null;
            }
        }
    }

    public record CompilationResults(MemoryStream AssemblyStream, MemoryStream SymbolStream) : IDisposable
    {
        public void Dispose()
        {
            AssemblyStream?.Dispose();
            SymbolStream?.Dispose();
            GC.SuppressFinalize(this);
        }
    }

    public record struct CompletionItem(string DisplayText, string FilterText, string SortText, string InlineDescription, ImmutableArray<string> Tags, TextSpan Span)
    {
        public DotNetObjectReference<IGetCompletionDescription> Description { get; init; } = null;

        public CompletionItem(RoslynCompletionItem item, RoslynCodeSession session) : this(item.DisplayText, item.FilterText, item.SortText, item.InlineDescription, item.Tags, item.Span)
        {
            Description = DotNetObjectReference.Create<IGetCompletionDescription>(new RoslynGetCompletionDescription(item, session));
        }
    }

    public interface IGetCompletionDescription
    {
        [JSInvokable]
        Task<ImmutableArray<TaggedText>> GetDescriptionAsync();
    }

    public sealed class RoslynGetCompletionDescription(RoslynCompletionItem item, RoslynCodeSession session) : IGetCompletionDescription
    {
        [JSInvokable]
        public Task<ImmutableArray<TaggedText>> GetDescriptionAsync() => session.GetCompletionDescriptionAsync(item);
    }

    public record struct InfoTipItem(ImmutableArray<string> Tags, TextSpan Span, params InfoTipSection[] Sections)
    {
        public InfoTipItem(QuickInfoItem item) : this(item.Tags, item.Span, [.. item.Sections.Select(x => new InfoTipSection(x))]) { }
    }

    public record struct InfoTipSection(string Kind, params InfoTipTaggedText[] Parts)
    {
        public InfoTipSection(QuickInfoSection section) : this(section.Kind, [.. section.TaggedParts.Select(x => new InfoTipTaggedText(x))]) { }
    }

    public record struct InfoTipTaggedText(string Tag, string Text)
    {
        public InfoTipTaggedText(TaggedText text) : this(text.Tag, text.Text) { }
    }
}
