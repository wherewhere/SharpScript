using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Elfie.Diagnostics;
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
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
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
        private static string _baseUrl;

        private readonly Dictionary<string, List<CodeFixProvider>> _providers;
        private readonly ImmutableArray<DiagnosticAnalyzer> _analyzers;
        private readonly RoslynOptions _options;
        private readonly string _language;
        private readonly bool _isConsole;
        private bool _outOfDate;
        private HashSet<MetadataReference> _addon = [];
        private AsyncLocker _addonLocker = new();

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

        public async ValueTask<ICodeSession> SetSourceTextAsync(string code, CancellationToken cancellationToken = default)
        {
            SourceCode = await AddReferencesAsync(code, cancellationToken).ConfigureAwait(false);
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
            _completionService.GetDescriptionAsync(_currentDocument, item, cancellationToken).ContinueWith(x => x.Result.TaggedParts);

        public Task<CompletionChange> GetCompletionChangeAsync(CompletionItem item, CancellationToken cancellationToken = default) =>
            _completionService.GetChangeAsync(_currentDocument, item, cancellationToken: cancellationToken).ContinueWith(x => new CompletionChange(x.Result));

        public async ValueTask<InfoTipItem> GetInfoTipAsync(int position, CancellationToken cancellationToken = default)
        {
            QuickInfoItem info = await QuickInfoService.GetQuickInfoAsync(CurrentDocument, position, cancellationToken).ConfigureAwait(false);
            if (info is null or { Sections.IsEmpty: true }) { return default; }
            return new InfoTipItem(info);
        }

        public async ValueTask<CompilationResults> CompileAsync(ICollection<Diagnostic> results, CancellationToken cancellationToken = default)
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
                    return await ConsoleVersion.CompileAsync(results, cancellationToken).ConfigureAwait(false);
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
                        references.Add(MetadataReference.CreateFromImage(array, documentation: await CreateDocumentation(client, assembly, logger).ConfigureAwait(false), filePath: assembly));
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

        private async ValueTask<string> AddReferencesAsync(string code, CancellationToken cancellationToken = default)
        {
            if (code.StartsWith("#r ", StringComparison.OrdinalIgnoreCase))
            {
                using StringReader reader = new(code);
                HashSet<MetadataReference> references = [];
                HttpClient client = null;
                BlazorBoot boot = null;
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
                                string path = temp.Trim([' ', '\'', '"']).ToString();
                                _ = builder.AppendLine($"// {temp}");
                                if (!string.IsNullOrEmpty(path) && !References.Any(x => x.Display.Equals(path, StringComparison.OrdinalIgnoreCase)))
                                {
                                    try
                                    {
                                        if (_addon.FirstOrDefault(x => x.Display.Equals(path, StringComparison.OrdinalIgnoreCase)) is MetadataReference reference)
                                        {
                                            references.Add(reference);
                                        }
                                        else
                                        {
                                            string fileName = $"{path}.wasm";
                                            client ??= new() { BaseAddress = new Uri(_baseUrl) };
                                            boot ??= await client.GetFromJsonAsync("blazor.boot.json", SourceGenerationContext.Default.BlazorBoot, cancellationToken: cancellationToken).ConfigureAwait(false);
                                            if (boot.Resources.FingerPrinting.FirstOrDefault(x => x.Value.Equals(fileName, StringComparison.OrdinalIgnoreCase)) is { Key.Length: > 0 } result)
                                            {
                                                using Stream stream = await client.GetStreamAsync(result.Key, cancellationToken).ConfigureAwait(false);
                                                byte[] array = await WebcilConverterUtil.ConvertFromWebcilAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
                                                references.Add(MetadataReference.CreateFromImage(array, documentation: await CreateDocumentationAsync(client, path, _logger, cancellationToken).ConfigureAwait(false), filePath: path));
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
                        if (!references.SetEquals(_addon))
                        {
                            _addon = references;
                            Solution solution = Workspace.CurrentSolution.WithProjectMetadataReferences(_currentDocument.Project.Id, [.. References, .. references]);
                            _ = Workspace.TryApplyChanges(solution);
                            _currentDocument = Workspace.CurrentSolution.GetDocument(_currentDocument.Id);
                        }
                    }
                }
                code = builder.Append(await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false)).ToString();
            }
            else if (_addon.Count > 0)
            {
                _addon = [];
                Solution solution = Workspace.CurrentSolution.WithProjectMetadataReferences(_currentDocument.Project.Id, References);
                _ = Workspace.TryApplyChanges(solution);
                _currentDocument = Workspace.CurrentSolution.GetDocument(_currentDocument.Id);
            }
            return code;
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

    public sealed record CompilationResults(MemoryStream AssemblyStream, MemoryStream SymbolStream) : IDisposable
    {
        public void Dispose()
        {
            AssemblyStream?.Dispose();
            SymbolStream?.Dispose();
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
}
