using SharpScript.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace SharpScript.Models
{
    public sealed record CompilationResults(string AssemblyName, MemoryStream AssemblyStream, MemoryStream? SymbolStream = null, MemoryStream? DocumentationStream = null) : IDisposable, IAsyncDisposable
    {
        public MetadataReferenceCollection? References { get; init; }

        public long Position
        {
            set
            {
                AssemblyStream?.Position = value;
                SymbolStream?.Position = value;
                DocumentationStream?.Position = value;
            }
        }

        public CompilationResults(string AssemblyName, MemoryStream AssemblyStream, MemoryStream SymbolStream, MemoryStream DocumentationStream, MetadataReferenceCollection references) : this(AssemblyName, AssemblyStream, SymbolStream, DocumentationStream) => References = references;

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
