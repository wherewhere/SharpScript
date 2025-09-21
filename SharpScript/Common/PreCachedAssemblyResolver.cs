using ICSharpCode.Decompiler.Metadata;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace SharpScript.Common
{
    public class PreCachedAssemblyResolver : IAssemblyResolver
    {
        private static readonly Task<MetadataFile> NullFileTask = Task.FromResult<MetadataFile>(null);

        private readonly ConcurrentDictionary<string, (PEFile file, Task<MetadataFile> task)> _peFileCache = new();

        public PreCachedAssemblyResolver(params MetadataReferenceCollection references)
        {
            foreach (MetadataReferenceHost host in references ?? RoslynCodeSession.References ?? [])
            {
                AddToCaches((host.Reference.Display, host.Image));
            }
        }

        private void AddToCaches(params IEnumerable<(string name, byte[] bytes)> assemblyPaths)
        {
            foreach ((string name, byte[] bytes) in assemblyPaths)
            {
                PEFile file = new(name, new MemoryStream(bytes));
                _ = _peFileCache.TryAdd(file.Name, (file, Task.FromResult<MetadataFile>(file)));
            }
        }

        public MetadataFile Resolve(IAssemblyReference reference)
        {
            return ResolveFromCacheForDecompilation(reference).file;
        }

        public Task<MetadataFile> ResolveAsync(IAssemblyReference reference)
        {
            return ResolveFromCacheForDecompilation(reference).task;
        }

        public MetadataFile ResolveModule(MetadataFile mainModule, string moduleName)
        {
            throw new NotSupportedException();
        }

        public Task<MetadataFile> ResolveModuleAsync(MetadataFile mainModule, string moduleName)
        {
            throw new NotSupportedException();
        }

        private (PEFile file, Task<MetadataFile> task) ResolveFromCacheForDecompilation(IAssemblyReference reference)
        {
            // It is OK to _not_ find the assembly for decompilation, as e.g. in IL we can reference arbitrary assemblies
            return !_peFileCache.TryGetValue(reference.Name, out (PEFile file, Task<MetadataFile> task) cached)
                ? ((PEFile file, Task<MetadataFile> task))(null, NullFileTask)
                : (cached.file, cached.task);
        }
    }
}
