using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using SharpScript.Common;
using SharpScript.RoslynAccess;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;

namespace SharpScript.Models
{
    public interface ICodeAction : IDisposable
    {
        string Title { get; }
        DotNetObjectReference<ICodeAction> Action => DotNetObjectReference.Create(this);
        [JSInvokable]
        Task<IReadOnlyList<TextChange>?> InvokeAsync();
        void IDisposable.Dispose() { Action?.Dispose(); GC.SuppressFinalize(this); }
    }

    public sealed class RoslynCodeAction(CodeAction action, RoslynCodeSession session) : ICodeAction
    {
        public string Title => action.Title;

        [JSInvokable]
        public async Task<IReadOnlyList<TextChange>?> InvokeAsync()
        {
            try
            {
                ImmutableArray<CodeActionOperation> operations = await action.GetOperationsAsync(default).ConfigureAwait(false);
                foreach (CodeActionOperation operation in operations)
                {
                    operation.Apply(session.Workspace, default);
                }
            }
            catch (Exception ex)
            {
                session._logger.LogError(ex, "Error while applying code action '{title}'.", action.Title);
            }
            return await session.RollbackWorkspaceChangesAsync().ConfigureAwait(false);
        }

        public static IEnumerable<RoslynCodeAction> FlatActions(CodeAction action, RoslynCodeSession session)
        {
            if (action.NestedActions is { IsEmpty: false } actions)
            {
                foreach (CodeAction nested in actions)
                {
                    foreach (RoslynCodeAction item in FlatActions(nested, session))
                    {
                        yield return item;
                    }
                }
            }
            else if (!RoslynHost.IsCodeActionNotSupportedByLSP(action))
            {
                yield return new RoslynCodeAction(action, session);
            }
        }
    }

    public sealed class ILCodeAction(string title, Func<Task<IReadOnlyList<TextChange>?>> invoker) : ICodeAction
    {
        public string Title => title;

        public DotNetObjectReference<ICodeAction> Action => DotNetObjectReference.Create<ICodeAction>(this);

        [JSInvokable]
        public Task<IReadOnlyList<TextChange>?> InvokeAsync() => invoker();
    }
}
