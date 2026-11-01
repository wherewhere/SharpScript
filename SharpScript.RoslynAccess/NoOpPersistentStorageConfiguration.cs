using Microsoft.CodeAnalysis.Host;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Storage;
using System.Composition;

namespace SharpScript.RoslynAccess
{
    [Shared]
    [method: ImportingConstructor]
    [ExportWorkspaceService(typeof(IPersistentStorageConfiguration), ServiceLayer.Host)]
    internal sealed class NoOpPersistentStorageConfiguration() : IPersistentStorageConfiguration
    {
        public bool ThrowOnFailure => false;
        public string? TryGetStorageLocation(SolutionKey solutionKey) => null;
    }
}
