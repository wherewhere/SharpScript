using Microsoft.CodeAnalysis;
using SharpScript.RoslynAccess;

namespace SharpScript.Common
{
    public sealed class CustomWorkspace() : Workspace(RoslynHost.HostServices, "Custom" /* same as AdHoc */)
    {
        public override bool CanOpenDocuments => true;
        public override bool CanApplyChange(ApplyChangesKind feature) =>
            feature is ApplyChangesKind.ChangeDocument
                or ApplyChangesKind.ChangeParseOptions
                or ApplyChangesKind.ChangeCompilationOptions
                or ApplyChangesKind.AddMetadataReference
                or ApplyChangesKind.RemoveMetadataReference;
        public new Solution SetCurrentSolution(Solution solution) => base.SetCurrentSolution(solution);
    }
}
