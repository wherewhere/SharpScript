using Microsoft.CodeAnalysis;
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

        public static AdhocWorkspace Workspace => new(_services.Value);
    }
}
