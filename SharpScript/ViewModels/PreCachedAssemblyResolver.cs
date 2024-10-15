using ICSharpCode.Decompiler.Metadata;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SharpScript.ViewModels
{
    public class PreCachedAssemblyResolver : IAssemblyResolver
    {
        private static readonly Task<MetadataFile> NullFileTask = Task.FromResult<MetadataFile>(null);

        private readonly ConcurrentDictionary<string, (PEFile file, Task<MetadataFile> task)> _peFileCache = new();

        public PreCachedAssemblyResolver(IEnumerable<MetadataReference> references)
        {
            AddToCaches(references.OfType<PortableExecutableReference>().Select(x => x.FilePath).OfType<string>());
        }

        private void AddToCaches(IEnumerable<string> assemblyPaths)
        {
            foreach (string path in assemblyPaths)
            {
                PEFile file = new(path);
                _peFileCache.TryAdd(file.Name, (file, Task.FromResult<MetadataFile>(file)));
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
