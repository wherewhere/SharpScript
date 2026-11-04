using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SharpScript.Common
{
    public static class FileLevelDirectiveParser
    {
        private static readonly SearchValues<char> featureSeparators = SearchValues.Create([',', ';', ' ']);

        public static FileLevelDirectives Parse(SourceText text, string language, string comment) => language switch
        {
            LanguageNames.CSharp => ParseCSharp(text, comment),
            _ => ParseOthers(text, comment)
        };

        private static FileLevelDirectives ParseCSharp(SourceText text, string comment = "//")
        {
            List<string> references = [];
            Dictionary<string, string> features = [with(StringComparer.OrdinalIgnoreCase)];
            string? assembly = null;
            List<TextChange> changes = [];
            SyntaxTokenParser tokenizer = SyntaxFactory.CreateTokenParser(text);
            SyntaxTokenParser.Result result = tokenizer.ParseLeadingTrivia();
            Span<Range> parts = stackalloc Range[2];
            foreach (SyntaxTrivia trivia in result.Token.LeadingTrivia)
            {
                if (trivia.IsKind(SyntaxKind.IgnoredDirectiveTrivia))
                {
                    if (trivia.GetStructure() is IgnoredDirectiveTriviaSyntax { Content: { RawKind: (int)SyntaxKind.StringLiteralToken, Text: { Length: > 0 } content } })
                    {
                        ReadOnlySpan<char> message = content.AsSpan().Trim();
                        int count = message.Split(parts, ' ', StringSplitOptions.RemoveEmptyEntries);
                        ReadOnlySpan<char> kind = count > 0 ? message[parts[0]] : default;
                        ReadOnlySpan<char> rest = count > 1 ? message[parts[1]] : default;
                        switch (kind)
                        {
                            case "property":
                                int separatorIndex = rest.IndexOf('=');
                                if (separatorIndex >= 0)
                                {
                                    ReadOnlySpan<char> name = rest[..separatorIndex].Trim();
                                    ReadOnlySpan<char> value = rest[(separatorIndex + 1)..].Trim();
                                    switch (name)
                                    {
                                        case "AssemblyName":
                                            assembly = value.Trim([' ', '\'', '"']).ToString();
                                            break;
                                        case "Features":
                                            foreach (Range range in value.SplitAny(featureSeparators))
                                            {
                                                ReadOnlySpan<char> feature = value[range];
                                                if (feature.IsWhiteSpace()) { continue; }
                                                int equalsIndex = feature.IndexOf('=');
                                                if (equalsIndex < 0)
                                                {
                                                    features[feature.ToString()] = "true";
                                                }
                                                else
                                                {
                                                    features[feature[..equalsIndex].ToString()] = feature[(equalsIndex + 1)..].ToString();
                                                }
                                            }
                                            break;
                                    }
                                }
                                break;
                        }
                    }
                }
                else if (trivia.IsKind(SyntaxKind.ReferenceDirectiveTrivia))
                {
                    switch (trivia.GetStructure())
                    {
                        case ReferenceDirectiveTriviaSyntax { File: { RawKind: (int)SyntaxKind.StringLiteralToken, Text: { Length: > 0 } content } } syntax:
                            ReadOnlySpan<char> file = content.AsSpan().Trim([' ', '\'', '"']);
                            references.Add(file.ToString());
                            changes.Add(new TextChange(new TextSpan(syntax.SpanStart, 2), comment));
                            break;
                        case ReferenceDirectiveTriviaSyntax syntax:
                            ReadOnlySpan<char> message = syntax.ToString().AsSpan();
                            int count = message.Split(parts, ' ', StringSplitOptions.RemoveEmptyEntries);
                            file = count > 1 ? message[parts[1]].Trim([' ', '\'', '"']) : default;
                            references.Add(file.ToString());
                            changes.Add(new TextChange(new TextSpan(syntax.SpanStart, 2), comment));
                            break;
                    }
                }
                else if (trivia.IsKind(SyntaxKind.BadDirectiveTrivia))
                {
                    if (trivia.GetStructure() is BadDirectiveTriviaSyntax { DirectiveNameToken: { RawKind: (int)SyntaxKind.IdentifierToken } directiveNameToken } syntax)
                    {
                        string directiveName = directiveNameToken.ValueText;
                        switch (directiveName)
                        {
                            case "feature":
                                ReadOnlySpan<char> message = syntax.ToString().AsSpan();
                                int count = message.Split(parts, ' ', StringSplitOptions.RemoveEmptyEntries);
                                if (count == 2)
                                {
                                    ReadOnlySpan<char> feature = message[parts[1]];
                                    if (feature.IsWhiteSpace()) { continue; }
                                    int equalsIndex = feature.IndexOf('=');
                                    if (equalsIndex < 0)
                                    {
                                        features[feature.ToString()] = "true";
                                    }
                                    else
                                    {
                                        features[feature[..equalsIndex].ToString()] = feature[(equalsIndex + 1)..].ToString();
                                    }
                                    changes.Add(new TextChange(new TextSpan(syntax.SpanStart, 2), comment));
                                }
                                break;
                            case "assembly":
                                message = syntax.ToString().AsSpan();
                                count = message.Split(parts, ' ', StringSplitOptions.RemoveEmptyEntries);
                                if (count == 2)
                                {
                                    assembly = message[parts[1]].Trim([' ', '\'', '"']).ToString();
                                    changes.Add(new TextChange(new TextSpan(syntax.SpanStart, 2), comment));
                                }
                                break;
                        }
                    }
                }
            }
            return new FileLevelDirectives(references, features, assembly, changes);
        }

        private static FileLevelDirectives ParseOthers(SourceText code, string comment)
        {
            List<string> references = [];
            Dictionary<string, string> features = [with(StringComparer.OrdinalIgnoreCase)];
            string? assembly = null;
            List<TextChange> changes = [];
            TextLineCollection lines = code.Lines;
            foreach (TextLine text in lines)
            {
                string line = text.ToString().TrimStart();
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }
                else if (line[0] != '#')
                {
                    break;
                }
                else if (line.StartsWith("#r ", StringComparison.OrdinalIgnoreCase))
                {
                    ReadOnlySpan<char> temp = line.AsSpan()[3..];
                    string path = temp.Trim([' ', '\'', '"']).ToString();
                    references.Add(path);
                    changes.Add(new TextChange(new TextSpan(text.Start, 2), comment));
                }
                else if (line.StartsWith("#feature ", StringComparison.OrdinalIgnoreCase))
                {
                    ReadOnlySpan<char> feature = line.AsSpan()[9..];
                    if (feature.IsWhiteSpace()) { continue; }
                    int equalsIndex = feature.IndexOf('=');
                    if (equalsIndex < 0)
                    {
                        features[feature.ToString()] = "true";
                    }
                    else
                    {
                        features[feature[..equalsIndex].ToString()] = feature[(equalsIndex + 1)..].ToString();
                    }
                    changes.Add(new TextChange(new TextSpan(text.Start, 2), comment));
                }
                else if (line.StartsWith("#assembly ", StringComparison.OrdinalIgnoreCase))
                {
                    assembly = line.AsSpan()[10..].Trim([' ', '\'', '"']).ToString();
                    references.Add(assembly);
                    changes.Add(new TextChange(new TextSpan(text.Start, 2), comment));
                }
            }
            return new FileLevelDirectives(references, features, assembly, changes);
        }
    }

    [method: JsonConstructor]
    public class FileLevelDirectives(List<string> references, Dictionary<string, string> features, string? assembly, List<TextChange> changes)
    {
        public List<string> References => references;
        public Dictionary<string, string> Features => features;
        public string? Assembly => assembly;
        public List<TextChange> Changes => changes;
    }
}
