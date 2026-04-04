using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System;
using RoslynDiagnostic = Microsoft.CodeAnalysis.Diagnostic;

namespace SharpScript.Models
{
    public sealed class Diagnostic(DiagnosticSeverity severity, string? message, params ICodeAction[] actions)
    {
        public string? ID { get; }
        public LinePositionSpan Location { get; }
        public string? Message => message;
        public string Severity => severity.ToString();
        public string[] Tags { get; } = [];
        public ICodeAction[] Actions { get; } = actions;

        public Diagnostic(Exception? exception) : this(DiagnosticSeverity.Error, exception?.Message) { }

        public Diagnostic(RoslynDiagnostic diagnostic, params RoslynCodeAction[] actions) : this(diagnostic.Severity, diagnostic.GetMessage(), actions)
        {
            ID = diagnostic.Id;
            Location = diagnostic.Location.GetLineSpan().Span;
            Tags = [.. diagnostic.Descriptor.CustomTags];
        }

        public Diagnostic(Mono.ILASM.Location location, DiagnosticSeverity severity, string message, params ILCodeAction[] actions) : this(severity, message, actions)
        {
            LinePosition position = new(location.line - 1, location.column);
            Location = new LinePositionSpan(position, position);
        }
    }
}
