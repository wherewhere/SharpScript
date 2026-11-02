using SharpScript.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace SharpScript.Models
{
    [method: JsonConstructor]
    public sealed class CompilationResults(string assemblyName, MemoryStream assemblyStream, MemoryStream? symbolStream = null, MemoryStream? documentationStream = null) : IDisposable, IAsyncDisposable
    {
        public string AssemblyName => assemblyName;
        public MemoryStream AssemblyStream => assemblyStream;
        public MemoryStream? SymbolStream => symbolStream;
        public MemoryStream? DocumentationStream => documentationStream;
        public MetadataReferenceCollection? References { get; }

        public long Position
        {
            set
            {
                AssemblyStream?.Position = value;
                SymbolStream?.Position = value;
                DocumentationStream?.Position = value;
            }
        }

        public CompilationResults(string assemblyName, MemoryStream assemblyStream, MemoryStream symbolStream, MemoryStream documentationStream, MetadataReferenceCollection references) : this(assemblyName, assemblyStream, symbolStream, documentationStream) => References = references;

        public void Seek(long offset, SeekOrigin loc)
        {
            AssemblyStream?.Seek(offset, loc);
            SymbolStream?.Seek(offset, loc);
            DocumentationStream?.Seek(offset, loc);
        }

        public void Dispose()
        {
            AssemblyStream?.Dispose();
            SymbolStream?.Dispose();
            DocumentationStream?.Dispose();
            GC.SuppressFinalize(this);
        }

        public async ValueTask DisposeAsync()
        {
            await Task.WhenAll(GetTasks()).ConfigureAwait(false);
            IEnumerable<Task> GetTasks()
            {
                if (AssemblyStream != null)
                {
                    yield return AssemblyStream.DisposeAsync().AsTask();
                }
                if (SymbolStream != null)
                {
                    yield return SymbolStream.DisposeAsync().AsTask();
                }
                if (DocumentationStream != null)
                {
                    yield return DocumentationStream.DisposeAsync().AsTask();
                }
            }
            GC.SuppressFinalize(this);
        }
    }
}
