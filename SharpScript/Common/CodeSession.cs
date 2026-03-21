using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.QuickInfo;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Mobius.ILasm.Core;
using SharpScript.Models;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CompletionChange = SharpScript.Models.CompletionChange;
using CSharpLanguageVersion = Microsoft.CodeAnalysis.CSharp.LanguageVersion;
using Diagnostic = SharpScript.Models.Diagnostic;
using RoslynDiagnostic = Microsoft.CodeAnalysis.Diagnostic;

namespace SharpScript.Common
{
    public sealed partial class RoslynCodeSession : ICodeSession
    {
        private static IDictionary<string, string>? _fingerprinting;
        private static string? _baseUrl;

        private readonly Dictionary<string, List<CodeFixProvider>> _providers;
        private readonly ImmutableArray<DiagnosticAnalyzer> _analyzers;
        private readonly GeneratorDriver? _generator;
        private readonly RoslynOptions _options;
        private readonly string _language;
        private readonly bool _isConsole;
        private readonly AsyncLocker _addonLocker = new();
        private readonly string _comment;
        private bool _outOfDate;
        private bool _sourceUpdated;
        private MetadataReferenceCollection _addon = [];
        private Dictionary<string, string> _features = [];
        private readonly HashSet<CodeFixProvider> _notWorkFixer = [];

        internal readonly ILogger<RoslynCodeSession> _logger;

        public static MetadataReferenceCollection References { get; private set; } = [];

        public AdhocWorkspace Workspace { get; private set; }

        public string AssemblyName => _currentDocument.Project.AssemblyName;

        public SourceText SourceCode
        {
            get;
            private set
            {
                if (field != value)
                {
                    field = value;
                    _sourceUpdated = true;
                }
            }
        }

        public SourceText SourceText
        {
            get;
            private set
            {
                if (field != value)
                {
                    field = value;
                    _outOfDate = true;
                }
            }
        } = SourceText.From(string.Empty);

        private Document _currentDocument;
        public Document CurrentDocument
        {
            get
            {
                EnsureUpToDate();
                return _currentDocument;
            }
        }

        public CompletionService? CompletionService
        {
            get
            {
                EnsureUpToDate();
                field ??= CompletionService.GetService(_currentDocument);

                if (field == null)
                {
                    _logger.LogWarning("Could not find completion service for document '{name}'.", _currentDocument.Name);
                }

                return field;
            }
        }

        public QuickInfoService? QuickInfoService
        {
            get
            {
                EnsureUpToDate();
                field ??= QuickInfoService.GetService(_currentDocument);

                if (field == null)
                {
                    _logger.LogWarning("Could not find quick info service for document '{name}'.", _currentDocument.Name);
                }

                return field;
            }
        }

        [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(RuntimeFeature))]
        public RoslynCodeSession(SourceText code, RoslynOptions options, bool isConsole, ILogger<RoslynCodeSession>? logger = null)
        {
            _options = options;
            _isConsole = isConsole;
            _logger = logger ?? NullLogger<RoslynCodeSession>.Instance;
            SourceCode = code;
            Workspace = new AdhocWorkspace();
            ProjectId projectId = ProjectId.CreateNewId();
            DocumentId docId = DocumentId.CreateNewId(projectId, "SharpScript.CodeSession");
            options.GetOptions(isConsole, out CompilationOptions compilation, out ParseOptions parse);
            _language = compilation.Language;
            Solution solution = Workspace.CurrentSolution
                .AddProject(projectId, "SharpScript.Project.CodeSession", "SharpScript.Playground", _language)
                .AddMetadataReferences(projectId, References)
                .WithProjectCompilationOptions(projectId, compilation)
                .WithProjectParseOptions(projectId, parse)
                .AddDocument(docId, "SharpScript.CodeSession.Document", code);
            _ = Workspace.TryApplyChanges(solution);
            Workspace.OpenDocument(docId);
            _currentDocument = Workspace.CurrentSolution.GetDocument(docId)!;
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
            if (_language == LanguageNames.CSharp)
            {
                _generator = GetCSharpGenerator(
                    "Microsoft.Interop.ComInterfaceGenerator",
                    "Microsoft.Interop.JavaScript.JSImportGenerator",
                    "Microsoft.Interop.LibraryImportGenerator",
                    "Microsoft.Interop.SourceGeneration",
                    "System.Text.Json.SourceGeneration",
                    "System.Text.RegularExpressions.Generator").WithUpdatedParseOptions(parse);
            }
        }

        [MemberNotNull(nameof(_baseUrl), nameof(_fingerprinting))]
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
                    "System.Runtime.InteropServices.JavaScript",
                    "System.Text.Json",
                    "System.Text.RegularExpressions").ConfigureAwait(false);
            }
        }

        private void EnsureUpToDate()
        {
            if (!_outOfDate) { return; }
            Document document = _currentDocument.WithText(SourceText);
            _ = Workspace.TryApplyChanges(document.Project.Solution);
            _currentDocument = Workspace.CurrentSolution.GetDocument(_currentDocument.Id)!;
            _outOfDate = false;
        }

        private async ValueTask EnsureUpToDateAsync(CancellationToken cancellationToken = default)
        {
            if (!_sourceUpdated) { return; }
            await PreprocessCodeAsync(cancellationToken).ConfigureAwait(false);
            _sourceUpdated = true;
        }

        public void ResetCode(string code) => SourceCode = SourceText.From(code, Encoding.Default);

        public void ApplyChanges(params TextChanges[] changes)
        {
            foreach (TextChanges change in changes)
            {
                SourceCode = SourceCode.WithChanges(change);
            }
        }

        public async ValueTask<IList<TextChange>?> FormatCodeAsync(CancellationToken cancellationToken = default)
        {
            await EnsureUpToDateAsync(cancellationToken).ConfigureAwait(false);
            SyntaxNode? node = await CurrentDocument.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            if (node != null)
            {
                return Formatter.GetFormattedTextChanges(node, Workspace, cancellationToken: cancellationToken);
            }
            return null;
        }

        public async ValueTask<IReadOnlyList<TextChange>> RollbackWorkspaceChangesAsync(CancellationToken cancellationToken = default)
        {
            Project oldProject = _currentDocument.Project;
            Project newProject = Workspace.CurrentSolution.GetProject(oldProject.Id)!;
            if (newProject == oldProject)
            {
                return [];
            }

            SourceText newText = await newProject.GetDocument(_currentDocument.Id)!.GetTextAsync(cancellationToken).ConfigureAwait(false);
            Workspace.TryApplyChanges(oldProject.Solution);
            _currentDocument = Workspace.CurrentSolution.GetDocument(_currentDocument.Id)!;

            return newText.GetTextChanges(SourceText);
        }

        public async ValueTask<T> GetDiagnosticsAsync<T>(T results, CancellationToken cancellationToken = default) where T : ICollection<Diagnostic>
        {
            await EnsureUpToDateAsync(cancellationToken).ConfigureAwait(false);
            Compilation? compilation = await CurrentDocument.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
            ImmutableArray<RoslynDiagnostic> diagnostics = await compilation!.WithGeneratorDriver(_generator, cancellationToken).WithAnalyzers(_analyzers).GetAllDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
            IEnumerable<RoslynDiagnostic> filtered = !_isConsole && _options is CSharpInputOptions { LanguageVersion: >= CSharpLanguageVersion.CSharp9 } ? diagnostics.Where(x => x is not { Id: "CS8805", Severity: DiagnosticSeverity.Error }) : diagnostics;
            Diagnostic[] array = await Task.WhenAll(filtered.Select(async diagnostic =>
            {
                List<CodeAction> actions = await GetCodeActionsAsync(diagnostic, cancellationToken).ConfigureAwait(false);
                return new Diagnostic(diagnostic, [.. actions.Select(x => new RoslynCodeAction(x, this))]);
            })).ConfigureAwait(false);
            results.AddRange(array);
            return results;
        }

        private async ValueTask<List<CodeAction>> GetCodeActionsAsync(RoslynDiagnostic diagnostic, CancellationToken cancellationToken = default)
        {
            List<CodeAction> codeActions = [];
            CodeFixContext context = new(_currentDocument, diagnostic, (x, _) => codeActions.Add(x), cancellationToken);
            if (_providers.TryGetValue(diagnostic.Id, out List<CodeFixProvider>? providers))
            {
                for (int i = providers.Count; --i >= 0;)
                {
                    CodeFixProvider provider = providers[i];
                    try
                    {
                        if (_notWorkFixer.Contains(provider))
                        {
                            _ = providers.Remove(provider);
                        }
                        else
                        {
                            await provider.RegisterCodeFixesAsync(context).ConfigureAwait(false);
                        }
                    }
                    catch (TypeInitializationException ex)
                    {
                        _logger.LogError(ex, "Not supports provider '{provider}' for diagnostic '{diagnosticId}'.", provider.GetType().Name, diagnostic.Id);
                        _notWorkFixer.Add(provider);
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

        private bool ShouldTriggerCompletions(CompletionService service, int position) => ShouldTriggerCompletions(service, position, '\0', CharacterOperation.None);
        private bool ShouldTriggerCompletions(CompletionService service, int position, char @char, CharacterOperation kind = CharacterOperation.Inserted)
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
            return ShouldTriggerCompletions(service, position, trigger);
        }

        private bool ShouldTriggerCompletions(CompletionService service, int position, CompletionTrigger completionTrigger) =>
            service == null || service.ShouldTriggerCompletion(SourceText, position, completionTrigger);

        public async ValueTask<IEnumerable<RoslynCompletionItem>> GetCompletionsAsync(int position, CancellationToken cancellationToken = default)
        {
            await EnsureUpToDateAsync(cancellationToken).ConfigureAwait(false);

            if (CompletionService is not CompletionService service)
            {
                return [];
            }

            if (!ShouldTriggerCompletions(service, position))
            {
                _logger.LogDebug("ShouldTriggerCompletionsAsync false, skipping.");
                return [];
            }

            CompletionList completions = await service.GetCompletionsAsync(_currentDocument, position, cancellationToken: cancellationToken).ConfigureAwait(false);
            TextSpan typedSpan = service.GetDefaultCompletionListSpan(SourceText, position);
            string typedText = SourceText.GetSubText(typedSpan).ToString();

            IReadOnlyList<CompletionItem> filteredItems = typedText.Length != 0
                ? service.FilterItems(_currentDocument, [.. completions.ItemsList], typedText)
                : completions.ItemsList;

            return filteredItems.Select(x => new RoslynCompletionItem(x, this));
        }

        public Task<ImmutableArray<TaggedText>> GetCompletionDescriptionAsync(CompletionItem item, CancellationToken cancellationToken = default) =>
            CompletionService!.GetDescriptionAsync(_currentDocument, item, cancellationToken).ContinueWith(x => x.Result!.TaggedParts, TaskScheduler.Default);

        public Task<CompletionChange> GetCompletionChangeAsync(CompletionItem item, CancellationToken cancellationToken = default) =>
            CompletionService!.GetChangeAsync(_currentDocument, item, cancellationToken: cancellationToken).ContinueWith(x => new CompletionChange(x.Result), TaskScheduler.Default);

        public async ValueTask<InfoTipItem> GetInfoTipAsync(int position, CancellationToken cancellationToken = default)
        {
            await EnsureUpToDateAsync(cancellationToken).ConfigureAwait(false);
            QuickInfoItem? info = await QuickInfoService!.GetQuickInfoAsync(_currentDocument, position, cancellationToken).ConfigureAwait(false);
            return info is null or { Sections.IsEmpty: true } ? default : new InfoTipItem(info);
        }

        public async ValueTask<CompilationResults?> CompileAsync(ICollection<Diagnostic> results, CancellationToken cancellationToken = default)
        {
            MemoryStream assemblyStream = new();
            MemoryStream symbolStream = new();
            MemoryStream documentationStream = new();
            await EnsureUpToDateAsync(cancellationToken).ConfigureAwait(false);
            Compilation? compilation = await CurrentDocument.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
            EmitResult emitResult = compilation!.WithGeneratorDriver(_generator, cancellationToken).Emit(assemblyStream, symbolStream, documentationStream, cancellationToken: cancellationToken);
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
                        Document? currentDocument = solution.GetDocument(_currentDocument.Id);
                        await Task.WhenAll(assemblyStream.DisposeAsync().AsTask(), symbolStream.DisposeAsync().AsTask(), documentationStream.DisposeAsync().AsTask()).ConfigureAwait(false);
                        assemblyStream = new();
                        symbolStream = new();
                        documentationStream = new();
                        compilation = await currentDocument!.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
                        emitResult = compilation!.WithGeneratorDriver(_generator, cancellationToken).Emit(assemblyStream, symbolStream, documentationStream, cancellationToken: cancellationToken);
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
            using HttpClient client = new() { BaseAddress = new Uri(_baseUrl!) };
            await Task.WhenAll(assemblies.Select(async assembly =>
            {
                try
                {
                    string fileName = $"{assembly}.wasm";
                    if (fingerprinting.FirstOrDefault(x => x.Value.Equals(fileName, StringComparison.OrdinalIgnoreCase)) is { Key.Length: > 0 } result)
                    {
                        Task<XmlDocumentationProvider?> documentTask = CreateDocumentationAsync(client, assembly, logger);
                        byte[] stream = await client.GetByteArrayAsync(result.Key).ConfigureAwait(false);
                        await using MemoryStream array = await WebcilConverterUtil.ConvertFromWebcilAsync(stream).ConfigureAwait(false);
                        references.Add(array, documentation: await documentTask.ConfigureAwait(false), filePath: assembly);
                        static async Task<XmlDocumentationProvider?> CreateDocumentationAsync(HttpClient client, string assembly, ILogger<RoslynCodeSession> logger)
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
            })).ConfigureAwait(false);
            return references;
        }

        private static void GetAnalyzers(string assemblyName, string language, out IEnumerable<DiagnosticAnalyzer> analyzers, out Dictionary<string, List<CodeFixProvider>> providers)
        {
            Type[] types = language == LanguageNames.CSharp ?
                [.. new[] { assemblyName, "Microsoft.Interop.ComInterfaceGenerator", "Microsoft.Interop.LibraryImportGenerator", "System.Text.RegularExpressions.Generator" }.Select(x => Assembly.Load(new AssemblyName(x)).GetTypes()).SelectMany(x => x)] :
                Assembly.Load(new AssemblyName(assemblyName)).GetTypes();

            analyzers = types.Where(x => x is { IsAbstract: false } && x.IsSubclassOf(typeof(DiagnosticAnalyzer)) && x.GetCustomAttributes<DiagnosticAnalyzerAttribute>(true).Any(x => x.Languages.Contains(language)))
                             .Select(Activator.CreateInstance)
                             .OfType<DiagnosticAnalyzer>();

            IEnumerable<CodeFixProvider> codeFixProvider =
                types.Where(x => x is { IsAbstract: false } && x.IsSubclassOf(typeof(CodeFixProvider)) && x.IsDefined(typeof(ExportCodeFixProviderAttribute)))
                     .Select(Activator.CreateInstance)
                     .OfType<CodeFixProvider>();

            providers = [];
            foreach (CodeFixProvider provider in codeFixProvider)
            {
                foreach (string id in provider.FixableDiagnosticIds)
                {
                    if (!providers.TryGetValue(id, out List<CodeFixProvider>? list))
                    {
                        list = [];
                        providers.Add(id, list);
                    }
                    list.Add(provider);
                }
            }
        }

        private static GeneratorDriver GetCSharpGenerator(params string[] assemblies)
        {
            IEnumerable<Type> types = assemblies.Select(x => Assembly.Load(new AssemblyName(x)).GetTypes()).SelectMany(x => x);

            IEnumerable<IIncrementalGenerator> incrementalGenerators =
                types.Where(x => x is { IsAbstract: false } && x.IsAssignableTo(typeof(IIncrementalGenerator)) && x.GetCustomAttributes<GeneratorAttribute>(true).Any())
                     .Select(Activator.CreateInstance)
                     .OfType<IIncrementalGenerator>();

            CSharpGeneratorDriver generator = CSharpGeneratorDriver.Create(incrementalGenerators.ToArray());

            IEnumerable<ISourceGenerator> generators =
                types.Where(x => x is { IsAbstract: false } && x.IsAssignableTo(typeof(ISourceGenerator)) && x.GetCustomAttributes<GeneratorAttribute>(true).Any())
                     .Select(Activator.CreateInstance)
                     .OfType<ISourceGenerator>();

            return generator.AddGenerators([.. generators]);
        }

        public async ValueTask<AstNodeItem?> GetAstAsync(CancellationToken cancellationToken = default)
        {
            await EnsureUpToDateAsync(cancellationToken).ConfigureAwait(false);
            Document document = CurrentDocument;
            SyntaxNode? syntaxRoot = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            if (syntaxRoot != null)
            {
                SemanticModel? semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
                if (semanticModel != null)
                {
                    return new AstNodeItem(syntaxRoot, semanticModel);
                }
            }
            return null;
        }

        private async ValueTask PreprocessCodeAsync(CancellationToken cancellationToken = default)
        {
            if (SourceCode[0] == '#')
            {
                MetadataReferenceCollection references = [];
                Dictionary<string, string> features = [];
                string? assemblyName = null;
                HttpClient? client = null;
                TextLineCollection lines = SourceCode.Lines;
                List<TextChange> changes = new(lines.Count);
                using (_ = await _addonLocker.WaitAsync())
                {
                    try
                    {
                        foreach (TextLine text in lines)
                        {
                            string line = text.ToString() ?? string.Empty;
                            if (line.StartsWith("#r ", StringComparison.OrdinalIgnoreCase))
                            {
                                ReadOnlySpan<char> temp = line.AsSpan()[3..];
                                string path = temp.Trim([' ', '\'', '"']).ToString();
                                changes.Add(new TextChange(new TextSpan(text.Start, 2), _comment));
                                await AddReferenceAsync(path, references, cancellationToken).ConfigureAwait(false);
                            }
                            else if (line.StartsWith("#feature ", StringComparison.OrdinalIgnoreCase))
                            {
                                string feature = line[9..];
                                changes.Add(new TextChange(new TextSpan(text.Start, 2), _comment));
                                if (feature.Split('=', StringSplitOptions.RemoveEmptyEntries) is [string key, string value])
                                {
                                    features[key.Trim([' ', '\'', '"'])] = value.Trim([' ', '\'', '"']);
                                }
                            }
                            else if (line.StartsWith("#assembly ", StringComparison.OrdinalIgnoreCase))
                            {
                                string assembly = line[10..];
                                changes.Add(new TextChange(new TextSpan(text.Start, 2), _comment));
                                assemblyName = assembly.Trim([' ', '\'', '"']);
                            }
                            else
                            {
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
                                .WithProjectCompilationOptions(projectId, oldProject.CompilationOptions!)
                                .WithProjectParseOptions(projectId, oldProject.ParseOptions!)
                                .AddDocument(docId, "SharpScript.CodeSession.Document", SourceText);
                            _ = Workspace.TryApplyChanges(solution);
                            Workspace.OpenDocument(docId);
                            _currentDocument = Workspace.CurrentSolution.GetDocument(docId)!;
                        }
                        if (!references.SequenceEqual(_addon))
                        {
                            _addon = references;
                            Solution solution = Workspace.CurrentSolution.WithProjectMetadataReferences(_currentDocument.Project.Id, References.Concat(references));
                            _ = Workspace.TryApplyChanges(solution);
                            _currentDocument = Workspace.CurrentSolution.GetDocument(_currentDocument.Id)!;
                        }
                        if (!features.SequenceEqual(_features))
                        {
                            _features = features;
                            if (_currentDocument.Project.ParseOptions is ParseOptions options)
                            {
                                Solution solution = Workspace.CurrentSolution.WithProjectParseOptions(_currentDocument.Project.Id, options.WithFeatures(features));
                                _ = Workspace.TryApplyChanges(solution);
                                _currentDocument = Workspace.CurrentSolution.GetDocument(_currentDocument.Id)!;
                            }
                        }
                    }
                }
                if (changes.Count > 0)
                {
                    SourceText = SourceCode.WithChanges(changes);
                    return;
                }
            }
            else
            {
                if (_addon.Count > 0)
                {
                    _addon = [];
                    Solution solution = Workspace.CurrentSolution.WithProjectMetadataReferences(_currentDocument.Project.Id, References);
                    _ = Workspace.TryApplyChanges(solution);
                    _currentDocument = Workspace.CurrentSolution.GetDocument(_currentDocument.Id)!;
                }
                if (_features.Count > 0)
                {
                    _features = [];
                    if (_currentDocument.Project.ParseOptions is ParseOptions options)
                    {
                        Solution solution = Workspace.CurrentSolution.WithProjectParseOptions(_currentDocument.Project.Id, options.WithFeatures(_features));
                        _ = Workspace.TryApplyChanges(solution);
                        _currentDocument = Workspace.CurrentSolution.GetDocument(_currentDocument.Id)!;
                    }
                }
            }
            SourceText = SourceCode;
        }

        private async ValueTask AddReferenceAsync(string path, MetadataReferenceCollection references, CancellationToken cancellationToken = default)
        {
            if (!string.IsNullOrEmpty(path) && !References.Any(x => path.Equals(x.Display, StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    int index = _addon.FindIndex(x => path.Equals(x.Display, StringComparison.OrdinalIgnoreCase));
                    if (index != -1)
                    {
                        references.Add(_addon[index]);
                    }
                    else
                    {
                        string fileName = $"{path}.wasm";
                        using HttpClient client = new() { BaseAddress = new Uri(_baseUrl!) };
                        if (_fingerprinting!.FirstOrDefault(x => x.Value.Equals(fileName, StringComparison.OrdinalIgnoreCase)) is { Key.Length: > 0 } result)
                        {
                            Task<XmlDocumentationProvider?> documentTask = CreateDocumentationAsync(client, path, _logger, cancellationToken);
                            byte[] stream = await client.GetByteArrayAsync(result.Key, cancellationToken).ConfigureAwait(false);
                            await using MemoryStream array = await WebcilConverterUtil.ConvertFromWebcilAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
                            references.Add(array, documentation: await documentTask.ConfigureAwait(false), filePath: path);
                            static async Task<XmlDocumentationProvider?> CreateDocumentationAsync(HttpClient client, string assembly, ILogger<RoslynCodeSession> logger, CancellationToken cancellationToken = default)
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

    public sealed class ILCodeSession(SourceText code, bool isConsole) : ICodeSession
    {
        SourceText ICodeSession.SourceCode => code;

        public void ResetCode(string _code) => code = SourceText.From(_code, Encoding.Default);

        public void ApplyChanges(params TextChanges[] changes)
        {
            foreach (TextChanges change in changes)
            {
                code = code.WithChanges(change);
            }
        }

        public ValueTask<CompilationResults?> CompileAsync(ICollection<Diagnostic> results, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Logger logger = new(results);
            Driver driver = new(logger, isConsole ? Driver.Target.Exe : Driver.Target.Dll, false, false, false);
            try
            {
                MemoryStream assemblyStream = new();
                if (driver.Assemble([code.ToString()], assemblyStream))
                {
                    _ = assemblyStream.Seek(0, SeekOrigin.Begin);
                    return ValueTask.FromResult<CompilationResults?>(new CompilationResults("SharpScript.Playground", assemblyStream, null));
                }
            }
            catch (Exception ex) when (ex.GetType().Name.StartsWith("yy"))
            {
                return ValueTask.FromResult<CompilationResults?>(null);
            }
            return ValueTask.FromResult<CompilationResults?>(null);
        }

        public ValueTask<T> GetDiagnosticsAsync<T>(T results, CancellationToken cancellationToken = default) where T : ICollection<Diagnostic>
        {
            cancellationToken.ThrowIfCancellationRequested();
            Logger logger = new(results);
            Driver driver = new(logger, isConsole ? Driver.Target.Exe : Driver.Target.Dll, false, false, false);

            try
            {
                using MemoryStream assemblyStream = new();
                _ = driver.Assemble([code.ToString()], assemblyStream);
                return ValueTask.FromResult(results);
            }
            catch (Exception ex) when (ex.GetType().Name.StartsWith("yy"))
            {
                return ValueTask.FromResult(results);
            }
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
        SourceText SourceCode { get; }
        void ResetCode(string code);
        void ApplyChanges(params TextChanges[] changes);
        ValueTask<IList<TextChange>?> FormatCodeAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult<IList<TextChange>?>([]);
        ValueTask<T> GetDiagnosticsAsync<T>(T results, CancellationToken cancellationToken = default) where T : ICollection<Diagnostic>;
        ValueTask<IEnumerable<RoslynCompletionItem>> GetCompletionsAsync(int position, CancellationToken cancellationToken = default) => ValueTask.FromResult<IEnumerable<RoslynCompletionItem>>([]);
        ValueTask<InfoTipItem> GetInfoTipAsync(int position, CancellationToken cancellationToken = default) => ValueTask.FromResult<InfoTipItem>(default);
        ValueTask<AstNodeItem?> GetAstAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult<AstNodeItem?>(default);
        ValueTask<CompilationResults?> CompileAsync(ICollection<Diagnostic> results, CancellationToken cancellationToken = default);
    }

    public enum CharacterOperation
    {
        None = 0,
        Inserted = 1,
        Deleted = 2
    }

    file static class Extensions
    {
        public static Compilation WithGeneratorDriver<T>(this Compilation compilation, T? driver, CancellationToken cancellationToken = default) where T : GeneratorDriver
        {
            if (driver == null)
            {
                return compilation;
            }
            else
            {
                _ = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation updatedCompilation, out _, cancellationToken);
                return updatedCompilation;
            }
        }
    }
}
