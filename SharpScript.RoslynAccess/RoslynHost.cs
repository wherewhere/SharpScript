using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.Host;
using Microsoft.CodeAnalysis.Host.Mef;
using System;

namespace SharpScript.RoslynAccess
{
    public static class RoslynHost
    {
        private static readonly Lazy<HostServices> _services = new(() =>
            MefHostServices.Create(
                MefHostServices.DefaultAssemblies
                    .Add(typeof(RoslynHost).Assembly)));

        public static HostServices HostServices => _services.Value;

        public static bool IsCodeActionNotSupportedByLSP(CodeAction suggestedAction) =>
            // Filter out CodeActionsWithOptions that do not report having an OptionsService
            // that can provide options. 
            (suggestedAction is CodeActionWithOptions codeAction
                && !codeAction.IsOptionServiceAvailable())
            // Skip code actions that requires non-document changes. We can't apply them in LSP currently.
            // https://github.com/dotnet/roslyn/issues/48698
            || suggestedAction.Tags.Contains(CodeAction.RequiresNonDocumentChange);
    }
}
