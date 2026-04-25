using ILAssembler;
using SharpScript.Models;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Diagnostic = SharpScript.Models.Diagnostic;
using ILDiagnostic = ILAssembler.Diagnostic;
using RoslynSourceText = Microsoft.CodeAnalysis.Text.SourceText;

namespace SharpScript.Common
{
    public sealed partial class CoreCLRCodeSession(RoslynSourceText code, Options options) : ICodeSession
    {
        RoslynSourceText ICodeSession.SourceCode => code;

        public void ResetCode(string _code) => code = RoslynSourceText.From(_code, Encoding.Default);

        public void ApplyChanges(params TextChanges[] changes)
        {
            foreach (TextChanges change in changes)
            {
                code = code.WithChanges(change);
            }
        }

        public ValueTask<CompilationResults?> CompileAsync(ICollection<Diagnostic> results, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DocumentCompiler compiler = new();
            (ImmutableArray<ILDiagnostic> diagnostics, PEBuilder? builder) = compiler.Compile(
                code.AsSourceTest("Program.il"),
                path => new SourceText(string.Empty, path),
                _ => [],
                options);
            if (builder != null)
            {
                MemoryStream assemblyStream = new();
                BlobBuilder blobBuilder = new();
                builder.Serialize(blobBuilder);
                blobBuilder.WriteContentTo(assemblyStream);
                _ = assemblyStream.Seek(0, SeekOrigin.Begin);
                return ValueTask.FromResult<CompilationResults?>(new CompilationResults("SharpScript.Playground", assemblyStream, null));
            }
            results.AddRange(diagnostics.Select(x => new Diagnostic(x)));
            return ValueTask.FromResult<CompilationResults?>(null);
        }

        public ValueTask<T> GetDiagnosticsAsync<T>(T results, CancellationToken cancellationToken = default) where T : ICollection<Diagnostic>
        {
            cancellationToken.ThrowIfCancellationRequested();
            DocumentCompiler compiler = new();
            (ImmutableArray<ILDiagnostic> diagnostics, PEBuilder? builder) = compiler.Compile(
                code.AsSourceTest("Program.il"),
                path => new SourceText(string.Empty, path),
                _ => [],
                options);
            results.AddRange(diagnostics.Select(x => new Diagnostic(x)));
            return ValueTask.FromResult(results);
        }
    }

    file static class Extensions
    {
        public static SourceText AsSourceTest(this RoslynSourceText sourceText, string path) => new(sourceText.ToString(), path);
    }
}
