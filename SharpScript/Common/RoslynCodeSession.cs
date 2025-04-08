using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Mobius.ILasm.Core;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using CSharpLanguageVersion = Microsoft.CodeAnalysis.CSharp.LanguageVersion;

namespace SharpScript.Common
{
    public sealed class RoslynCodeSession : ICodeSession
    {
        private static readonly SourceText EmptySourceText = SourceText.From(string.Empty);
        private static string baseUrl;

        private readonly ILogger<RoslynCodeSession> _logger;
        private readonly RoslynOptions _options;
        private readonly bool _isConsole;
        private bool _outOfDate;

        public static List<MetadataReference> References { get; private set; }

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

        public AdhocWorkspace Workspace { get; set; }

        private SourceText _sourceText;
        public SourceText SourceText
        {
            get => _sourceText;
            private set
            {
                if (value == _sourceText)
                {
                    return;
                }

                _sourceText = value;
                _outOfDate = true;
            }
        }

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

        public RoslynCodeSession(string code, RoslynOptions options, bool isConsole, ILogger<RoslynCodeSession> logger = null)
        {
            _options = options;
            _isConsole = isConsole;
            _logger = logger ?? NullLogger<RoslynCodeSession>.Instance;
            _sourceText = code == null ? EmptySourceText : SourceText.From(code, Encoding.Default);
            Workspace = new AdhocWorkspace();
            ProjectId projectId = ProjectId.CreateNewId();
            DocumentId docId = DocumentId.CreateNewId(projectId, "SharpScript.CodeSession");
            options.GetOptions(isConsole, out CompilationOptions compilation, out ParseOptions parse);
            Solution solution = Workspace.CurrentSolution
                .AddProject(projectId, "SharpScript.Project.CodeSession", "SharpScript", compilation.Language)
                .AddDocument(docId, "SharpScript.CodeSession.Document", _sourceText);
            Workspace.OpenDocument(docId);
            solution = solution
                .AddMetadataReferences(projectId, References)
                .WithProjectCompilationOptions(projectId, compilation)
                .WithProjectParseOptions(projectId, parse);
            _currentDocument = solution.GetDocument(docId);
        }

        public static async ValueTask InitAsync(string baseUrl)
        {
            RoslynCodeSession.baseUrl = baseUrl;
            if (References?.Count is not > 0)
            {
                References = await GetMetadataReferencesAsync(
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

        private void EnsureUpToDate()
        {
            if (!_outOfDate)
            {
                return;
            }
            _currentDocument = _currentDocument.WithText(SourceText);
            Workspace.TryApplyChanges(_currentDocument.Project.Solution);
            _outOfDate = false;
        }

        public void SetSourceText(string code)
        {
            SourceText = SourceText.From(code, Encoding.Default);
            EnsureUpToDate();
        }

        public async ValueTask<T> GetDiagnosticsAsync<T>(T results) where T : ICollection<Diagnostic>
        {
            Compilation compilation = await CurrentDocument.Project.GetCompilationAsync().ConfigureAwait(false);
            results.AddRange( compilation.GetDiagnostics().Select(x => new Diagnostic(x)));
            return results;
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

        public async ValueTask<IEnumerable<CompletionItem>> GetCompletionsAsync(int position)
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

            CompletionList completions = await service.GetCompletionsAsync(CurrentDocument, position).ConfigureAwait(false);
            TextSpan typedSpan = CompletionService.GetDefaultCompletionListSpan(SourceText, position);
            string typedText = SourceText.GetSubText(typedSpan).ToString();

            IReadOnlyList<Microsoft.CodeAnalysis.Completion.CompletionItem> filteredItems = typedText.Length != 0
                ? CompletionService.FilterItems(CurrentDocument, [.. completions.ItemsList], typedText)
                : completions.ItemsList;

            return filteredItems.Select(x => new CompletionItem(x.DisplayText, x.FilterText, x.SortText, x.InlineDescription, x.Tags, x.Span));
        }

        public async ValueTask<CompilationResults> Compile(ICollection<Diagnostic> results)
        {
            MemoryStream assemblyStream = new();
            MemoryStream symbolStream = new();
            Compilation compilation = await CurrentDocument.Project.GetCompilationAsync();
            EmitResult emitResult = compilation.Emit(assemblyStream, symbolStream);
            if (emitResult.Success)
            {
                assemblyStream.Seek(0, SeekOrigin.Begin);
                symbolStream.Seek(0, SeekOrigin.Begin);
                return new CompilationResults(assemblyStream, symbolStream);
            }
            else
            {
                if (!_isConsole && _options is CSharpInputOptions { LanguageVersion: >= CSharpLanguageVersion.CSharp9}
                    && emitResult.Diagnostics.Any(x => x is { Id: "CS8805", Severity: DiagnosticSeverity.Error }))
                {
                    return await ConsoleVersion.Compile(results);
                }
                results.AddRange(emitResult.Diagnostics.Select(x => new Diagnostic(x)));
                return null;
            }
        }

        public RoslynCodeSession WithIsConsole(bool isConsole) => new(SourceText.ToString(), _options, isConsole, _logger);

        private static async ValueTask<List<MetadataReference>> GetMetadataReferencesAsync(params string[] assemblies)
        {
            List<MetadataReference> references = [];
            using HttpClient client = new() { BaseAddress = new Uri(baseUrl) };
            foreach (string assembly in assemblies)
            {
                using Stream stream = await client.GetStreamAsync($"{assembly}.wasm").ConfigureAwait(false);
                references.Add(MetadataReference.CreateFromImage(WebcilConverterUtil.ConvertFromWebcil(stream)));
            }
            return references;
        }
    }

    public sealed class ILCodeSession(string code, bool isConsole) : ICodeSession
    {
        private string _code = code;

        public ValueTask<CompilationResults> Compile(ICollection<Diagnostic> results)
        {
            Logger logger = new(results);
            Driver driver = new(logger, isConsole ? Driver.Target.Exe : Driver.Target.Dll, false, false, false);
            try
            {
                MemoryStream assemblyStream = new();
                if (driver.Assemble([_code], assemblyStream))
                {
                    assemblyStream.Seek(0, SeekOrigin.Begin);
                    return ValueTask.FromResult(new CompilationResults(assemblyStream, null));
                }
            }
            catch (Exception ex) when (ex.GetType().Name.StartsWith("yy"))
            {
                return ValueTask.FromResult<CompilationResults>(null);
            }
            return ValueTask.FromResult<CompilationResults>(null);
        }

        public ValueTask<T> GetDiagnosticsAsync<T>(T results) where T: ICollection<Diagnostic>
        {
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

        public void SetSourceText(string code) => _code = code;

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
        ValueTask<T> GetDiagnosticsAsync<T>(T results) where T : ICollection<Diagnostic>;
        ValueTask<IEnumerable<CompletionItem>> GetCompletionsAsync(int position) => ValueTask.FromResult<IEnumerable<CompletionItem>>([]);
        void SetSourceText(string code);
        ValueTask<CompilationResults> Compile(ICollection<Diagnostic> results);
    }

    public enum CharacterOperation
    {
        None = 0,
        Inserted = 1,
        Deleted = 2
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

    public record struct CompletionItem(string DisplayText, string FilterText, string SortText, string InlineDescription, ImmutableArray<string> Tags, TextSpan Span);
}
