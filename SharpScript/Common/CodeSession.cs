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
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Net.Http;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using CSharpLanguageVersion = Microsoft.CodeAnalysis.CSharp.LanguageVersion;
using RoslynCompletionChange = Microsoft.CodeAnalysis.Completion.CompletionChange;
using RoslynDiagnostic = Microsoft.CodeAnalysis.Diagnostic;

namespace SharpScript.Common
{
    public sealed partial class RoslynCodeSession : ICodeSession<ICodeSession>
    {
        private static readonly SourceText EmptySourceText = SourceText.From(string.Empty);
        private static IDictionary<string, string> _fingerprinting;
        private static string _baseUrl;

        private readonly Dictionary<string, List<CodeFixProvider>> _providers;
        private readonly ImmutableArray<DiagnosticAnalyzer> _analyzers;
        private readonly RoslynOptions _options;
        private readonly string _language;
        private readonly bool _isConsole;
        private readonly AsyncLocker _addonLocker = new();
        private readonly string _comment;
        private bool _outOfDate;
        private MetadataReferenceCollection _addon = [];
        private Dictionary<string, string> _features = [];

        internal readonly ILogger<RoslynCodeSession> _logger;

        public static MetadataReferenceCollection References { get; private set; } = [];

        public AdhocWorkspace Workspace { get; private set; }

        public string AssemblyName => _currentDocument.Project.AssemblyName;

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

        public QuickInfoService QuickInfoService
        {
            get
            {
                EnsureUpToDate();
                field ??= QuickInfoService.GetService(CurrentDocument);

                if (field == null)
                {
                    _logger.LogWarning("Could not find quick info service for document '{name}'.", CurrentDocument.Name);
                }

                return field;
            }
        }
        
        [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(RuntimeFeature))]
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
                .AddProject(projectId, "SharpScript.Project.CodeSession", nameof(SharpScript), _language)
                .AddMetadataReferences(projectId, References)
                .WithProjectCompilationOptions(projectId, compilation)
                .WithProjectParseOptions(projectId, parse)
                .AddDocument(docId, "SharpScript.CodeSession.Document", SourceText);
            _ = Workspace.TryApplyChanges(solution);
            Workspace.OpenDocument(docId);
            _currentDocument = Workspace.CurrentSolution.GetDocument(docId);
            string assemblyName;
            switch (_language)
            {
                case LanguageNames.CSharp:
                    _comment = "//";
                    assemblyName = "Microsoft.CodeAnalysis.CSharp.Features";
                    break;
                case LanguageNames.VisualBasic:
                    _comment = "' ";
                    assemblyName = "Microsoft.CodeAnalysis.VisualBasic.Features";
                    break;
                default:
                    throw new NotSupportedException($"Language '{_language}' is not supported.");
            }
            GetAnalyzers(assemblyName, _language, out IEnumerable<DiagnosticAnalyzer> analyzers, out _providers);
            _analyzers = [.. analyzers];
        }

        public static async ValueTask InitAsync(string baseUrl, IDictionary<string, string> fingerprinting, ILogger<RoslynCodeSession> logger)
        {
            _baseUrl = baseUrl;
            _fingerprinting = fingerprinting;
            if (References is not { Count: > 0 })
            {
                References = await GetMetadataReferencesAsync(
                    logger,
                    fingerprinting,
                    "Microsoft.CSharp",
                    "Microsoft.VisualBasic.Core",
                    "System.ComponentModel.Primitives",
                    "System.Console",
                    "System.Linq",
                    "System.Linq.Expressions",
                    "System.Net.Http",
                    "System.Net.Primitives",
                    "System.Private.CoreLib",
                    "System.Private.Uri",
                    "System.Runtime",
                    "System.Text.Json",
                    "System.Text.RegularExpressions").ConfigureAwait(false);
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

        public async ValueTask<ICodeSession> SetSourceTextAsync(string code, CancellationToken cancellationToken = default)
        {
            SourceCode = await PerprocessCodeAsync(code, cancellationToken).ConfigureAwait(false);
            return this;
        }

        public async ValueTask<IReadOnlyList<TextChange>> RollbackWorkspaceChangesAsync(CancellationToken cancellationToken = default)
        {
            Project oldProject = _currentDocument.Project;
            Project newProject = Workspace.CurrentSolution.GetProject(oldProject.Id)!;
            if (newProject == oldProject)
            {
                return [];
            }

            SourceText newText = await newProject.GetDocument(_currentDocument.Id).GetTextAsync(cancellationToken).ConfigureAwait(false);
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
                List<CodeAction> actions = await GetCodeActionsAsync(diagnostic, cancellationToken).ConfigureAwait(false);
                results.Add(new Diagnostic(diagnostic, [.. actions.Select(x => new RoslynCodeAction(x, this))]));
            }
            return results;
        }

        private async ValueTask<List<CodeAction>> GetCodeActionsAsync(RoslynDiagnostic diagnostic, CancellationToken cancellationToken = default)
        {
            List<CodeAction> codeActions = [];
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

        public async ValueTask<IEnumerable<RoslynCompletionItem>> GetCompletionsAsync(int position, CancellationToken cancellationToken = default)
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

            IReadOnlyList<CompletionItem> filteredItems = typedText.Length != 0
                ? CompletionService.FilterItems(CurrentDocument, [.. completions.ItemsList], typedText)
                : completions.ItemsList;

            return filteredItems.Select(x => new RoslynCompletionItem(x, this));
        }

        public Task<ImmutableArray<TaggedText>> GetCompletionDescriptionAsync(CompletionItem item, CancellationToken cancellationToken = default) =>
            _completionService.GetDescriptionAsync(_currentDocument, item, cancellationToken).ContinueWith(x => x.Result.TaggedParts, TaskScheduler.Default);

        public Task<CompletionChange> GetCompletionChangeAsync(CompletionItem item, CancellationToken cancellationToken = default) =>
            _completionService.GetChangeAsync(_currentDocument, item, cancellationToken: cancellationToken).ContinueWith(x => new CompletionChange(x.Result), TaskScheduler.Default);

        public async ValueTask<InfoTipItem> GetInfoTipAsync(int position, CancellationToken cancellationToken = default)
        {
            QuickInfoItem info = await QuickInfoService.GetQuickInfoAsync(CurrentDocument, position, cancellationToken).ConfigureAwait(false);
            return info is null or { Sections.IsEmpty: true } ? default : new InfoTipItem(info);
        }

        public async ValueTask<CompilationResults> CompileAsync(ICollection<Diagnostic> results, CancellationToken cancellationToken = default)
        {
            MemoryStream assemblyStream = new();
            MemoryStream symbolStream = new();
            MemoryStream documentationStream = new();
            Compilation compilation = await CurrentDocument.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
            EmitResult emitResult = compilation.Emit(assemblyStream, symbolStream, documentationStream, cancellationToken: cancellationToken);
            if (emitResult.Success)
            {
                _ = assemblyStream.Seek(0, SeekOrigin.Begin);
                _ = symbolStream.Seek(0, SeekOrigin.Begin);
                _ = documentationStream.Seek(0, SeekOrigin.Begin);
                return new CompilationResults(AssemblyName, assemblyStream, symbolStream, documentationStream, References + _addon);
            }
            else
            {
                if (!_isConsole && _options is CSharpInputOptions { LanguageVersion: >= CSharpLanguageVersion.CSharp9 }
                    && emitResult.Diagnostics.Any(x => x is { Id: "CS8805", Severity: DiagnosticSeverity.Error }))
                {
                    if (_currentDocument.Project.CompilationOptions is CompilationOptions options)
                    {
                        Solution solution = Workspace.CurrentSolution.WithProjectCompilationOptions(_currentDocument.Project.Id, options.WithOutputKind(OutputKind.ConsoleApplication));
                        Document currentDocument = solution.GetDocument(_currentDocument.Id);
                        await assemblyStream.DisposeAsync().ConfigureAwait(false);
                        await symbolStream.DisposeAsync().ConfigureAwait(false);
                        await documentationStream.DisposeAsync().ConfigureAwait(false);
                        assemblyStream = new();
                        symbolStream = new();
                        documentationStream = new();
                        compilation = await currentDocument.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
                        emitResult = compilation.Emit(assemblyStream, symbolStream, documentationStream, cancellationToken: cancellationToken);
                        if (emitResult.Success)
                        {
                            _ = assemblyStream.Seek(0, SeekOrigin.Begin);
                            _ = symbolStream.Seek(0, SeekOrigin.Begin);
                            _ = documentationStream.Seek(0, SeekOrigin.Begin);
                            return new CompilationResults(AssemblyName, assemblyStream, symbolStream, documentationStream, References + _addon);
                        }
                    }
                }
                results.AddRange(emitResult.Diagnostics.Select(x => new Diagnostic(x)));
                return null;
            }
        }

        private static async ValueTask<MetadataReferenceCollection> GetMetadataReferencesAsync(ILogger<RoslynCodeSession> logger, IDictionary<string, string> fingerprinting, params string[] assemblies)
        {
            MetadataReferenceCollection references = [];
            using HttpClient client = new() { BaseAddress = new Uri(_baseUrl) };
            foreach (string assembly in assemblies)
            {
                try
                {
                    string fileName = $"{assembly}.wasm";
                    if (fingerprinting.FirstOrDefault(x => x.Value.Equals(fileName, StringComparison.OrdinalIgnoreCase)) is { Key.Length: > 0 } result)
                    {
                        byte[] stream = await client.GetByteArrayAsync(result.Key).ConfigureAwait(false);
                        await using MemoryStream array = await WebcilConverterUtil.ConvertFromWebcilAsync(stream).ConfigureAwait(false);
                        references.Add(array, documentation: await CreateDocumentation(client, assembly, logger).ConfigureAwait(false), filePath: assembly);
                        static async ValueTask<XmlDocumentationProvider> CreateDocumentation(HttpClient client, string assembly, ILogger<RoslynCodeSession> logger)
                        {
                        start:
                            try
                            {
                                byte[] bytes = await client.GetByteArrayAsync($"{assembly}.xml").ConfigureAwait(false);
                                if (bytes?.Length > 0)
                                {
                                    return XmlDocumentationProvider.CreateFromBytes(bytes);
                                }
                            }
                            catch (Exception ex)
                            {
                                switch (assembly)
                                {
                                    case "System.Private.CoreLib":
                                    case "System.Private.Uri":
                                        assembly = "System.Runtime";
                                        logger.LogTrace("The documentation for '{assembly}' was not found. Fallback to 'System.Runtime'.", assembly);
                                        goto start;
                                    default:
                                        const string baseUrl = "https://wherewhere.github.io/SharpScript/_framework/";
                                        if (_baseUrl != baseUrl)
                                        {
                                            try
                                            {
                                                using HttpClient _client = new() { BaseAddress = new Uri(baseUrl) };
                                                byte[] bytes = await _client.GetByteArrayAsync($"{assembly}.xml").ConfigureAwait(false);
                                                if (bytes?.Length > 0)
                                                {
                                                    return XmlDocumentationProvider.CreateFromBytes(bytes);
                                                }
                                            }
                                            catch (Exception e)
                                            {
                                                logger.LogWarning(e, "The documentation for '{assembly}' was not found.", assembly);
                                            }
                                        }
                                        logger.LogWarning(ex, "The documentation for '{assembly}' was not found.", assembly);
                                        break;
                                }
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

        public async ValueTask<AstNodeItem> GetAstAsync(CancellationToken cancellationToken = default)
        {
            Document document = CurrentDocument;
            SyntaxNode syntaxRoot = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            if (syntaxRoot != null)
            {
                SemanticModel semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
                if (semanticModel != null)
                {
                    return new AstNodeItem(syntaxRoot, semanticModel);
                }
            }
            return null;
        }

        private async ValueTask<string> PerprocessCodeAsync(string code, CancellationToken cancellationToken = default)
        {
            if (code.StartsWith('#'))
            {
                using StringReader reader = new(code);
                MetadataReferenceCollection references = [];
                Dictionary<string, string> features = [];
                string assemblyName = null;
                HttpClient client = null;
                StringBuilder builder = new();
                using (_ = await _addonLocker.WaitAsync())
                {
                    try
                    {
                        while (reader.Peek() > 0)
                        {
                            string line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                            if (line.StartsWith("#r ", StringComparison.OrdinalIgnoreCase))
                            {
                                ReadOnlySpan<char> temp = line.AsSpan()[3..];
                                _ = builder.AppendLine($"{_comment} {temp}");
                                string path = temp.Trim([' ', '\'', '"']).ToString();
                                await AddReferenceAsync(path, references, cancellationToken).ConfigureAwait(false);
                            }
                            else if (line.StartsWith("#feature ", StringComparison.OrdinalIgnoreCase))
                            {
                                string feature = line[9..];
                                _ = builder.AppendLine($"{_comment}eature {feature}");
                                if (feature.Split('=', StringSplitOptions.RemoveEmptyEntries) is [string key, string value])
                                {
                                    features[key.Trim([' ', '\'', '"'])] = value.Trim([' ', '\'', '"']);
                                }
                            }
                            else if (line.StartsWith("#assembly ", StringComparison.OrdinalIgnoreCase))
                            {
                                string assembly = line[10..];
                                _ = builder.AppendLine($"{_comment}ssembly {assembly}");
                                assemblyName = assembly.Trim([' ', '\'', '"']);
                            }
                            else
                            {
                                _ = builder.AppendLine(line);
                                break;
                            }
                        }
                    }
                    finally
                    {
                        client?.Dispose();
                        if (assemblyName != null && assemblyName != AssemblyName)
                        {
                            Workspace = new AdhocWorkspace();
                            ProjectId projectId = ProjectId.CreateNewId();
                            DocumentId docId = DocumentId.CreateNewId(projectId, "SharpScript.CodeSession");
                            Project oldProject = _currentDocument.Project;
                            Solution solution = Workspace.CurrentSolution
                                .AddProject(projectId, oldProject.Name, assemblyName, oldProject.Language)
                                .AddMetadataReferences(projectId, oldProject.MetadataReferences)
                                .WithProjectCompilationOptions(projectId, oldProject.CompilationOptions)
                                .WithProjectParseOptions(projectId, oldProject.ParseOptions)
                                .AddDocument(docId, "SharpScript.CodeSession.Document", SourceText);
                            _ = Workspace.TryApplyChanges(solution);
                            Workspace.OpenDocument(docId);
                            _currentDocument = Workspace.CurrentSolution.GetDocument(docId);
                        }
                        if (!references.SequenceEqual(_addon))
                        {
                            _addon = references;
                            Solution solution = Workspace.CurrentSolution.WithProjectMetadataReferences(_currentDocument.Project.Id, References.Concat(references));
                            _ = Workspace.TryApplyChanges(solution);
                            _currentDocument = Workspace.CurrentSolution.GetDocument(_currentDocument.Id);
                        }
                        if (!features.SequenceEqual(_features))
                        {
                            _features = features;
                            if (_currentDocument.Project.ParseOptions is ParseOptions options)
                            {
                                Solution solution = Workspace.CurrentSolution.WithProjectParseOptions(_currentDocument.Project.Id, options.WithFeatures(features));
                                _ = Workspace.TryApplyChanges(solution);
                                _currentDocument = Workspace.CurrentSolution.GetDocument(_currentDocument.Id);
                            }
                        }
                    }
                }
                code = builder.Append(await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false)).ToString();
            }
            else
            {
                if (_addon.Count > 0)
                {
                    _addon = [];
                    Solution solution = Workspace.CurrentSolution.WithProjectMetadataReferences(_currentDocument.Project.Id, References);
                    _ = Workspace.TryApplyChanges(solution);
                    _currentDocument = Workspace.CurrentSolution.GetDocument(_currentDocument.Id);
                }
                if (_features.Count > 0)
                {
                    _features = [];
                    if (_currentDocument.Project.ParseOptions is ParseOptions options)
                    {
                        Solution solution = Workspace.CurrentSolution.WithProjectParseOptions(_currentDocument.Project.Id, options.WithFeatures(_features));
                        _ = Workspace.TryApplyChanges(solution);
                        _currentDocument = Workspace.CurrentSolution.GetDocument(_currentDocument.Id);
                    }
                }
            }
            return code;
        }

        private async ValueTask AddReferenceAsync(string path, MetadataReferenceCollection references, CancellationToken cancellationToken = default)
        {
            if (!string.IsNullOrEmpty(path) && !References.Any(x => x.Display.Equals(path, StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    int index = _addon.FindIndex(x => x.Display.Equals(path, StringComparison.OrdinalIgnoreCase));
                    if (index != -1)
                    {
                        references.Add(_addon[index]);
                    }
                    else
                    {
                        string fileName = $"{path}.wasm";
                        using HttpClient client = new() { BaseAddress = new Uri(_baseUrl) };
                        if (_fingerprinting.FirstOrDefault(x => x.Value.Equals(fileName, StringComparison.OrdinalIgnoreCase)) is { Key.Length: > 0 } result)
                        {
                            byte[] stream = await client.GetByteArrayAsync(result.Key, cancellationToken).ConfigureAwait(false);
                            await using MemoryStream array = await WebcilConverterUtil.ConvertFromWebcilAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
                            references.Add(array, documentation: await CreateDocumentationAsync(client, path, _logger, cancellationToken).ConfigureAwait(false), filePath: path);
                            static async ValueTask<XmlDocumentationProvider> CreateDocumentationAsync(HttpClient client, string assembly, ILogger<RoslynCodeSession> logger, CancellationToken cancellationToken = default)
                            {
                                try
                                {
                                    byte[] bytes = await client.GetByteArrayAsync($"{assembly}.xml", cancellationToken);
                                    if (bytes?.Length > 0)
                                    {
                                        return XmlDocumentationProvider.CreateFromBytes(bytes);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    const string baseUrl = "https://wherewhere.github.io/SharpScript/_framework/";
                                    if (_baseUrl != baseUrl)
                                    {
                                        try
                                        {
                                            using HttpClient _client = new() { BaseAddress = new Uri(baseUrl) };
                                            byte[] bytes = await _client.GetByteArrayAsync($"{assembly}.xml", cancellationToken).ConfigureAwait(false);
                                            if (bytes?.Length > 0)
                                            {
                                                return XmlDocumentationProvider.CreateFromBytes(bytes);
                                            }
                                        }
                                        catch (Exception e)
                                        {
                                            logger.LogWarning(e, "The documentation for '{assembly}' was not found.", assembly);
                                        }
                                    }
                                    logger.LogWarning(ex, "The documentation for '{assembly}' was not found.", assembly);
                                }
                                return null;
                            }
                        }
                        else
                        {
                            _logger.LogWarning("The assembly '{path}' was not found.", path);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "The reference for '{path}' was not found.", path);
                }
            }
        }

        public partial class AsyncLocker : IDisposable
        {
            public SemaphoreSlim SlimLocker { get; } = new(1, 1);

            public async Task<AsyncLocker> WaitAsync()
            {
                await SlimLocker.WaitAsync();
                return this;
            }

            protected virtual void Dispose(bool disposing)
            {
                if (disposing)
                {
                    SlimLocker.Release();
                }
            }

            public void Dispose()
            {
                Dispose(disposing: true);
                GC.SuppressFinalize(this);
            }
        }
    }

    public sealed class ILCodeSession(string code, bool isConsole) : ICodeSession<ICodeSession>
    {
        private string _code = code;

        public ValueTask<CompilationResults> CompileAsync(ICollection<Diagnostic> results, CancellationToken cancellationToken = default)
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
                    return ValueTask.FromResult(new CompilationResults(nameof(SharpScript), assemblyStream, null));
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

        public ValueTask<ICodeSession> SetSourceTextAsync(string code, CancellationToken cancellationToken = default)
        {
            _code = code;
            return ValueTask.FromResult<ICodeSession>(this);
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
        ValueTask<IEnumerable<RoslynCompletionItem>> GetCompletionsAsync(int position, CancellationToken cancellationToken = default) => ValueTask.FromResult<IEnumerable<RoslynCompletionItem>>([]);
        ValueTask<InfoTipItem> GetInfoTipAsync(int position, CancellationToken cancellationToken = default) => ValueTask.FromResult<InfoTipItem>(default);
        ValueTask<AstNodeItem> GetAstAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult<AstNodeItem>(default);
        ValueTask<ICodeSession> SetSourceTextAsync(string code, CancellationToken cancellationToken = default);
        ValueTask<CompilationResults> CompileAsync(ICollection<Diagnostic> results, CancellationToken cancellationToken = default);
    }

    public interface ICodeSession<TSelf> : ICodeSession where TSelf : ICodeSession
    {
        new ValueTask<TSelf> SetSourceTextAsync(string code, CancellationToken cancellationToken = default);
        async ValueTask<ICodeSession> ICodeSession.SetSourceTextAsync(string code, CancellationToken cancellationToken) => await SetSourceTextAsync(code, cancellationToken).ConfigureAwait(false);
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
        public ICodeAction[] Actions { get; } = [];

        public Diagnostic(Exception exception) : this(DiagnosticSeverity.Error, exception.Message) { }

        public Diagnostic(RoslynDiagnostic diagnostic, params RoslynCodeAction[] actions) : this(diagnostic.Severity, diagnostic.GetMessage())
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

    public interface ICodeAction : IDisposable
    {
        string Title { get; }
        DotNetObjectReference<ICodeAction> Action { get; }
        [JSInvokable]
        Task<IReadOnlyList<TextChange>> InvokeAsync();
        void IDisposable.Dispose() { Action?.Dispose(); GC.SuppressFinalize(this); }
    }

    public sealed class RoslynCodeAction(CodeAction action, RoslynCodeSession session) : ICodeAction
    {
        public string Title => action.Title;
        public DotNetObjectReference<ICodeAction> Action => DotNetObjectReference.Create<ICodeAction>(this);

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

    public sealed record CompilationResults(string AssemblyName, MemoryStream AssemblyStream, MemoryStream SymbolStream = null, MemoryStream DocumentationStream = null) : IDisposable
    {
        public MetadataReferenceCollection References { get; init; }

        public long Position
        {
            set
            {
                AssemblyStream?.Position = value;
                SymbolStream?.Position = value;
                DocumentationStream?.Position = value;
            }
        }

        public CompilationResults(string AssemblyName, MemoryStream AssemblyStream, MemoryStream SymbolStream, MemoryStream DocumentationStream, MetadataReferenceCollection references) : this(AssemblyName, AssemblyStream, SymbolStream, DocumentationStream) => References = references;

        public void Seek(long offset, SeekOrigin loc)
        {
            AssemblyStream?.Seek(offset, loc);
            SymbolStream?.Seek(offset, loc);
            DocumentationStream?.Seek(offset, loc);
        }

        public void Dispose()
        {
            AssemblyStream?.Dispose();
            SymbolStream?.Dispose();
            DocumentationStream?.Dispose();
            GC.SuppressFinalize(this);
        }
    }

    public record struct CompletionChange(ImmutableArray<TextChange> TextChanges, int? NewPosition)
    {
        public CompletionChange(RoslynCompletionChange change) : this(change.TextChanges, change.NewPosition) { }
    }

    public interface ICompletionItem : IDisposable
    {
        string DisplayText { get; }
        string FilterText { get; }
        string SortText { get; }
        string InlineDescription { get; }
        ImmutableArray<string> Tags { get; }
        TextSpan Span { get; }
        DotNetObjectReference<ICompletionItem> Self { get; }
        [JSInvokable]
        Task<ImmutableArray<TaggedText>> GetDescriptionAsync();
        [JSInvokable]
        Task<CompletionChange> GetChangeAsync();
        void IDisposable.Dispose() { Self?.Dispose(); GC.SuppressFinalize(this); }
    }

    public sealed class RoslynCompletionItem(CompletionItem item, RoslynCodeSession session) : ICompletionItem
    {
        public string DisplayText => item.DisplayText;
        public string FilterText => item.FilterText;
        public string SortText => item.SortText;
        public string InlineDescription => item.InlineDescription;
        public ImmutableArray<string> Tags => item.Tags;
        public TextSpan Span => item.Span;
        public DotNetObjectReference<ICompletionItem> Self => DotNetObjectReference.Create<ICompletionItem>(this);
        [JSInvokable]
        public Task<ImmutableArray<TaggedText>> GetDescriptionAsync() => session.GetCompletionDescriptionAsync(item);
        [JSInvokable]
        public Task<CompletionChange> GetChangeAsync() => session.GetCompletionChangeAsync(item);
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

    [JsonConverter(typeof(JsonConverter))]
    public abstract class AstItemBase
    {
        public abstract string Type { get; }

        public class JsonConverter : JsonConverter<AstItemBase>
        {
            public override AstItemBase Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                JsonElement element = JsonDocument.ParseValue(ref reader).RootElement;
                if (element.TryGetProperty("type", out JsonElement typeElement) && typeElement.ValueKind == JsonValueKind.String)
                {
                    switch (typeElement.GetString())
                    {
                        case "trivia":
                            return element.Deserialize<AstTriviaItem>(options);
                        case "token":
                            return element.Deserialize<AstTokenItem>(options);
                        case "node":
                            return element.Deserialize<AstNodeItem>(options);
                        case "operation":
                            return element.Deserialize<AstOperationItem>(options);
                        case "value":
                            return element.Deserialize<AstValueItem>(options);
                    }
                }
                return element.Deserialize(typeToConvert, options) as AstItemBase;
            }

            public override void Write(Utf8JsonWriter writer, AstItemBase value, JsonSerializerOptions options)
            {
                switch (value)
                {
                    case AstTriviaItem trivia:
                        JsonSerializer.Serialize(writer, trivia, options);
                        break;
                    case AstTokenItem token:
                        JsonSerializer.Serialize(writer, token, options);
                        break;
                    case AstNodeItem node:
                        JsonSerializer.Serialize(writer, node, options);
                        break;
                    case AstOperationItem operation:
                        JsonSerializer.Serialize(writer, operation, options);
                        break;
                    case AstValueItem val:
                        JsonSerializer.Serialize(writer, val, options);
                        break;
                    default:
                        JsonSerializer.Serialize(writer, value, options);
                        break;
                }
            }
        }
    }

    public class AstNodeItem() : AstItemBase
    {
        private static readonly ConcurrentDictionary<Type, Lazy<Func<SyntaxNode, SyntaxNode, string>>> _compiledSyntaxNodeGetParentPropertyName = [];
        private static readonly ConcurrentDictionary<Type, Lazy<Func<SyntaxToken, SyntaxNode, string>>> _compiledSyntaxTokenGetParentPropertyName = [];

        protected static readonly Dictionary<int, string> _kindNames =
            Enum.GetValues<Microsoft.CodeAnalysis.CSharp.SyntaxKind>().OfType<Enum>()
                .Concat(Enum.GetValues<Microsoft.CodeAnalysis.VisualBasic.SyntaxKind>().OfType<Enum>())
                .Select(e => (name: e.ToString("G"), value: ((IConvertible)e).ToInt32(null)))
                .Distinct()
                .ToDictionary(t => t.value, t => t.name);

        public override string Type => "node";
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string Property { get; protected init; }
        public string Kind { get; protected init; }
        public TextSpan Span { get; protected init; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public IReadOnlyList<AstItemBase> Children { get; protected init; }

        public AstNodeItem(SyntaxNode node, SemanticModel model, string specialParentPropertyName = null) : this()
        {
            Kind = _kindNames[node.RawKind];
            Span = node.Span;

            string parentPropertyName = specialParentPropertyName ?? GetParentPropertyName(node);
            if (parentPropertyName != null)
            {
                Property = parentPropertyName;
            }

            List<AstItemBase> children = [];
            IOperation operation = model.GetOperation(node);
            if (operation != null)
            {
                children.Add(new AstOperationItem(operation));
            }
            foreach (SyntaxNodeOrToken child in node.ChildNodesAndTokens())
            {
                children.Add(child.IsNode ? new AstNodeItem(child.AsNode(), model) : new AstTokenItem(child.AsToken(), model));
            }
            Children = children;
        }

        public static string GetParentPropertyName(SyntaxToken token) => GetParentPropertyName(token, token.Parent, _compiledSyntaxTokenGetParentPropertyName);

        public static string GetParentPropertyName(SyntaxNode node) => GetParentPropertyName(node, node.Parent, _compiledSyntaxNodeGetParentPropertyName);

        private static string GetParentPropertyName<T>(T value, SyntaxNode parent, ConcurrentDictionary<Type, Lazy<Func<T, SyntaxNode, string>>> compiledCache)
        {
            if (parent == null)
            { return null; }
            Func<T, SyntaxNode, string> compiled = compiledCache.GetOrAdd(
                parent.GetType(),
                t => new Lazy<Func<T, SyntaxNode, string>>(() => SlowCompileGetParentPropertyName<T>(t), LazyThreadSafetyMode.ExecutionAndPublication)).Value;
            return compiled(value, parent);
        }

        private static Func<T, SyntaxNode, string> SlowCompileGetParentPropertyName<T>(Type parentSyntaxType)
        {
            ParameterExpression value = Expression.Parameter(typeof(T));
            ParameterExpression parent = Expression.Parameter(typeof(SyntaxNode), "parent");
            ParameterExpression parentTyped = Expression.Variable(parentSyntaxType);

            LabelTarget end = Expression.Label(typeof(string));
            List<Expression> statements = [Expression.Assign(parentTyped, Expression.Convert(parent, parentSyntaxType))];
            foreach (PropertyInfo property in parentSyntaxType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.Name == nameof(SyntaxNode.Parent))
                { continue; }
                if (!IsSameAsOrSubclassOf(property.PropertyType.GetTypeInfo()))
                { continue; }
                static bool IsSameAsOrSubclassOf(Type type) => type == typeof(T) || type.IsSubclassOf(typeof(T));
                BinaryExpression propertyEqualsNode = Expression.Equal(Expression.Property(parentTyped, property), value);
                statements.Add(Expression.IfThen(propertyEqualsNode, Expression.Return(end, Expression.Constant(property.Name))));
            }
            statements.Add(Expression.Label(end, Expression.Constant(null, typeof(string))));
            return Expression
                .Lambda<Func<T, SyntaxNode, string>>(Expression.Block([parentTyped], statements), value, parent)
                .Compile();
        }
    }

    public sealed class AstOperationItem(IOperation operation) : AstItemBase
    {
        private Action<IOperation, Dictionary<string, string>> _cache;

        public override string Type => "operation";
        public string Property => "Operation";
        public string Kind => operation.Kind.ToString();
        public Dictionary<string, string> Properties
        {
            get
            {
                Dictionary<string, string> writer = [];
                Action<IOperation, Dictionary<string, string>> serialize = _cache ??= SlowCompileSerializeProperties(operation.GetType());
                serialize(operation, writer);
                return writer;
            }
        }

        private static Action<IOperation, Dictionary<string, string>> SlowCompileSerializeProperties(Type operationType)
        {
            ParameterExpression operation = Expression.Parameter(typeof(IOperation), "operation");
            ParameterExpression writer = Expression.Parameter(typeof(Dictionary<string, string>), "writer");
            IEnumerable<Expression> statements = operationType
                .GetProperties()
                .Where(p => !SlowShouldSkip(p))
                .OrderBy(p => p.Name)
                .Select(p => SlowExpressSerializeProperty(operation, p, writer));

            return Expression.Lambda<Action<IOperation, Dictionary<string, string>>>(
                Expression.Block(statements),
                operation, writer).Compile();
        }

        private static Expression SlowExpressSerializeProperty(ParameterExpression operation, PropertyInfo property, ParameterExpression writer)
        {
            // MemberInfo.DeclaringType is null only on global module methods.
            MemberExpression propertyValue = Expression.Property(Expression.Convert(operation, property.DeclaringType!), property);
            Type propertyType = property.PropertyType;

            if (propertyType.IsGenericType && propertyType.GetGenericTypeDefinition() == typeof(Optional<>))
            {
                return Expression.Condition(
                    Expression.Property(propertyValue, nameof(Optional<>.HasValue)),
                    SlowExpressWriteNameAndValue(
                        property.Name,
                        Expression.Property(propertyValue, nameof(Optional<>.Value)),
                        writer),
                    Expression.Empty());
            }

            if (propertyType == typeof(bool))
            {
                return Expression.Condition(
                    propertyValue,
                    SlowExpressWriteNameAndValue(property.Name, Expression.Constant(true), writer),
                    Expression.Empty());
            }

            return SlowHandleNulls(
                propertyValue,
                SlowExpressWriteNameAndValue(property.Name, propertyValue, writer),
                Expression.Empty());
        }

        private static MethodCallExpression SlowExpressWriteNameAndValue(string name, Expression value, ParameterExpression writer)
        {
            Expression valueToWrite = SlowGetValueToWrite(value);
            return Expression.Call(writer, nameof(Dictionary<,>.Add), typeArguments: null,
                Expression.Constant(name),
                valueToWrite);
        }

        private static readonly MethodInfo ObjectToString = typeof(object).GetMethod(nameof(ToString));
        private static readonly Expression Skipped = Expression.Constant("<skipped>");
        private static Expression SlowGetValueToWrite(Expression value)
        {
            System.Reflection.TypeInfo type = value.Type.GetTypeInfo();
            if (type.IsAssignableTo(typeof(IEnumerable)) || type.IsAssignableTo(typeof(SyntaxNode)))
            { return Skipped; }

            if (value.Type != typeof(string))
            {
                return SlowHandleNulls(
                    value,
                    Expression.Call(value, ObjectToString),
                    Expression.Constant(null, typeof(string)));
            }

            return value;
        }

        private static Expression SlowHandleNulls(Expression value, Expression ifNotNull, Expression ifNull)
        {
            if (value.Type.IsValueType)
            { return ifNotNull; }

            return Expression.Condition(
                Expression.ReferenceNotEqual(value, Expression.Constant(null, value.Type)),
                ifNotNull,
                ifNull);
        }

        private static bool SlowShouldSkip(PropertyInfo property) =>
            property.Name == nameof(IOperation.Language)
                || property.Name == nameof(IOperation.Kind)
                || property.Name == nameof(IOperation.Parent)
#pragma warning disable CS0618 // Type or member is obsolete
                || property.Name == nameof(IOperation.Children)
#pragma warning restore CS0618 // Type or member is obsolete
                || property.Name == nameof(IOperation.ChildOperations)
                || property.Name == nameof(IOperation.Syntax)
                || property.PropertyType.IsAssignableTo(typeof(IOperation))
                || property.PropertyType.IsAssignableTo(typeof(IEnumerable<IOperation>));
    }

    public class AstTokenItem() : AstNodeItem
    {
        public override string Type => "token";
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string Value { get; protected init; }

        public AstTokenItem(SyntaxToken token, SemanticModel model) : this()
        {
            Kind = _kindNames[token.RawKind];
            Span = token.FullSpan;

            string parentPropertyName = GetParentPropertyName(token);
            if (parentPropertyName != null)
            {
                Property = parentPropertyName;
            }

            if (token.HasLeadingTrivia || token.HasTrailingTrivia)
            {
                Value = token.ValueText;
                List<AstItemBase> children = [];
                foreach (SyntaxTrivia trivia in token.LeadingTrivia)
                {
                    children.Add(new AstTriviaItem(trivia, model));
                }
                children.Add(new AstValueItem(token.ValueText, token.Span));
                foreach (SyntaxTrivia trivia in token.TrailingTrivia)
                {
                    children.Add(new AstTriviaItem(trivia, model));
                }
                Children = children;
            }
            else
            {
                Value = token.ToString();
            }
        }
    }

    public sealed class AstTriviaItem : AstTokenItem
    {
        public override string Type => "trivia";

        public AstTriviaItem(SyntaxTrivia trivia, SemanticModel model) : base()
        {
            Kind = _kindNames[trivia.RawKind];
            Span = trivia.Span;

            if (trivia.HasStructure)
            {
                Children = [new AstNodeItem(trivia.GetStructure(), model, "Structure")];
            }
            else
            {
                Value = trivia.ToString();
            }
        }
    }

    public sealed class AstValueItem(string value, TextSpan span) : AstItemBase
    {
        public override string Type => "value";
        public TextSpan Span => span;
        public string Value => value;
    }
}
