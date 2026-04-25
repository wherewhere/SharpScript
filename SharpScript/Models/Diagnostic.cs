using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System;
using RoslynDiagnostic = Microsoft.CodeAnalysis.Diagnostic;
using ILDiagnostic = ILAssembler.Diagnostic;
using ILDiagnosticSeverity = ILAssembler.DiagnosticSeverity;
using ILLocation = ILAssembler.Location;
using ILSourceText = ILAssembler.SourceText;
using SourceSpan = ILAssembler.SourceSpan;
using System.Runtime.CompilerServices;

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

        public Diagnostic(ILDiagnostic diagnostic) : this(diagnostic.Severity.AsDiagnosticSeverity(), diagnostic.Message)
        {
            ID = diagnostic.Id;
            Location = diagnostic.Location.GetLineSpan();
        }
    }

    file static class Extensions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static DiagnosticSeverity AsDiagnosticSeverity(this ILDiagnosticSeverity severity) => (DiagnosticSeverity)((int)DiagnosticSeverity.Error - (int)severity);

        public static LinePositionSpan GetLineSpan(this ILLocation location)
        {
            (SourceSpan span, ILSourceText text) = location;
            string code = text.Text;
            (int start, int length) = span;
            int line = 0, column = 0;
            for (int i = 0; i < start; i++)
            {
                if (code[i] == '\n')
                {
                    line++;
                    column = 0;
                }
                else
                {
                    column++;
                }
            }
            LinePosition startPosition = new(line, column);
            for (int i = 0; i < length; i++)
            {
                if (code[start + i] == '\n')
                {
                    line++;
                    column = 0;
                }
                else
                {
                    column++;
                }
            }
            LinePosition endPosition = new(line, column);
            return new LinePositionSpan(startPosition, endPosition);
        }
    }
}
