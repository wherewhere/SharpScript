using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Mobius.ILasm.Core;
using SharpScript.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Diagnostic = SharpScript.Models.Diagnostic;

namespace SharpScript.Common
{
    public sealed partial class MonoCodeSession(SourceText code, bool isConsole) : ICodeSession
    {
        SourceText ICodeSession.SourceCode => code;

        public void ResetCode(string _code) => code = SourceText.From(_code, Encoding.Default);

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
            Logger logger = new(results);
            Driver driver = new(logger, isConsole ? Driver.Target.Exe : Driver.Target.Dll, false, false, false);
            try
            {
                MemoryStream assemblyStream = new();
                if (driver.Assemble([code.ToString()], assemblyStream))
                {
                    _ = assemblyStream.Seek(0, SeekOrigin.Begin);
                    return ValueTask.FromResult<CompilationResults?>(new CompilationResults("SharpScript.Playground", assemblyStream, null));
                }
            }
            catch (Exception ex) when (ex.GetType().Name.StartsWith("yy"))
            {
                return ValueTask.FromResult<CompilationResults?>(null);
            }
            return ValueTask.FromResult<CompilationResults?>(null);
        }

        public ValueTask<T> GetDiagnosticsAsync<T>(T results, CancellationToken cancellationToken = default) where T : ICollection<Diagnostic>
        {
            cancellationToken.ThrowIfCancellationRequested();
            Logger logger = new(results);
            Driver driver = new(logger, isConsole ? Driver.Target.Exe : Driver.Target.Dll, false, false, false);

            try
            {
                using MemoryStream assemblyStream = new();
                _ = driver.Assemble([code.ToString()], assemblyStream);
                return ValueTask.FromResult(results);
            }
            catch (Exception ex) when (ex.GetType().Name.StartsWith("yy"))
            {
                return ValueTask.FromResult(results);
            }
        }

        private partial class Logger(ICollection<Diagnostic> results) : Mobius.ILasm.interfaces.ILogger
        {
            public void Info(string message) => results.Add(new Diagnostic(DiagnosticSeverity.Info, message));

            public void Warning(string message)
            {
                if (MissingReferenceRegex.Match(message) is { Success: true, Groups: [_, { Value: { Length: > 0 } value }] })
                {
                    Task<IReadOnlyList<TextChange>?> InvokeAsync()
                    {
                        return Task.FromResult<IReadOnlyList<TextChange>?>([
                            new TextChange(
                                new TextSpan(0, 0),
                                $$"""
                                .assembly extern {{value}} {
                                }

                                """)
                        ]);
                    }
                    ILCodeAction action = new("adding", InvokeAsync);
                    results.Add(new Diagnostic(DiagnosticSeverity.Warning, message.Replace(", adding.", "."), action));
                }
                else
                {
                    results.Add(new Diagnostic(DiagnosticSeverity.Warning, message));
                }
            }

            public void Error(string message) => results.Add(new Diagnostic(DiagnosticSeverity.Error, message));

            public void Warning(Mono.ILASM.Location location, string message) => results.Add(new Diagnostic(location, DiagnosticSeverity.Warning, message));

            public void Error(Mono.ILASM.Location location, string message) => results.Add(new Diagnostic(location, DiagnosticSeverity.Error, message));

            [GeneratedRegex(@"^Reference to undeclared extern assembly '(.*)', adding\.$")]
            private static partial Regex MissingReferenceRegex { get; }
        }
    }
}
