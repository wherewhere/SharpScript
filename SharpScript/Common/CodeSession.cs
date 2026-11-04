using Microsoft.CodeAnalysis.QuickInfo;
using Microsoft.CodeAnalysis.Text;
using SharpScript.Models;
using SharpScript.RoslynAccess.Models;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Diagnostic = SharpScript.Models.Diagnostic;

namespace SharpScript.Common
{
    public interface ICodeSession
    {
        SourceText SourceCode { get; }
        void ResetCode(string code);
        void ApplyChanges(params TextChanges[] changes);
        ValueTask<IList<TextChange>?> FormatCodeAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult<IList<TextChange>?>([]);
        ValueTask<T> GetDiagnosticsAsync<T>(T results, CancellationToken cancellationToken = default) where T : ICollection<Diagnostic>;
        ValueTask<IEnumerable<RoslynCompletionItem>> GetCompletionsAsync(int position, CancellationToken cancellationToken = default) => ValueTask.FromResult<IEnumerable<RoslynCompletionItem>>([]);
        ValueTask<QuickInfoItem?> GetInfoTipAsync(int position, CancellationToken cancellationToken = default) => ValueTask.FromResult<QuickInfoItem?>(default);
        ValueTask<SignatureHelpItemsData?> GetSignatureHelpAsync(int position, SignatureHelpTriggerReason reason, char? triggerCharacter, CancellationToken cancellationToken = default) => ValueTask.FromResult<SignatureHelpItemsData?>(default);
        ValueTask<AstNodeItem?> GetAstAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult<AstNodeItem?>(default);
        ValueTask<CompilationResults?> CompileAsync(ICollection<Diagnostic> results, CancellationToken cancellationToken = default);
    }

    public enum CharacterOperation
    {
        None = 0,
        Inserted = 1,
        Deleted = 2
    }
}
