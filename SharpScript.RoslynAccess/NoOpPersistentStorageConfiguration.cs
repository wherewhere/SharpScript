using Microsoft.CodeAnalysis.Host;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Storage;
using System;
using System.Composition;

namespace SharpScript.RoslynAccess
{
    [ExportWorkspaceService(typeof(IPersistentStorageConfiguration), ServiceLayer.Test), Shared]
    [method: ImportingConstructor, Obsolete(MefConstruction.ImportingConstructorMessage, error: true)]
    internal sealed class NoOpPersistentStorageConfiguration() : IPersistentStorageConfiguration
    {
        public bool ThrowOnFailure => false;
        public string? TryGetStorageLocation(SolutionKey solutionKey) => null;
    }
}
