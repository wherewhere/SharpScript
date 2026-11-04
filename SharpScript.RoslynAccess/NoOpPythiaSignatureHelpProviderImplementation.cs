using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.ExternalAccess.Pythia.Api;
using Microsoft.CodeAnalysis.Host.Mef;
using System;
using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;

namespace SharpScript.RoslynAccess
{
    [Export(typeof(IPythiaSignatureHelpProviderImplementation)), Shared]
    [method: ImportingConstructor, Obsolete(MefConstruction.ImportingConstructorMessage, error: true)]
    internal sealed class NoOpPythiaSignatureHelpProviderImplementation() : IPythiaSignatureHelpProviderImplementation
    {
        public Task<(ImmutableArray<PythiaSignatureHelpItemWrapper> items, int? selectedItemIndex)> GetMethodGroupItemsAndSelectionAsync(ImmutableArray<IMethodSymbol> accessibleMethods, Document document, InvocationExpressionSyntax invocationExpression, SemanticModel semanticModel, SymbolInfo currentSymbol, CancellationToken cancellationToken)
        {
            return Task.FromResult<(ImmutableArray<PythiaSignatureHelpItemWrapper>, int?)>(([], null));
        }
    }
}
