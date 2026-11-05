using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SharpScript.Common
{
    public static class FileLevelDirectiveParser
    {
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
            foreach (SyntaxTrivia trivia in result.Token.LeadingTrivia)
            {
                if (trivia.IsKind(SyntaxKind.IgnoredDirectiveTrivia))
                {
                    if (trivia.GetStructure() is IgnoredDirectiveTriviaSyntax { Content: { RawKind: (int)SyntaxKind.StringLiteralToken, Text: { Length: > 0 } content } })
                    {
                        ReadOnlySpan<char> message = content.AsSpan().Trim();
                        int separatorIndex = message.IndexOf(' ');
                        if (separatorIndex >= 0)
                        {
                            switch (message[..separatorIndex].TrimEnd())
                            {
                                case "property":
                                    ReadOnlySpan<char> rest = message[(separatorIndex + 1)..].TrimStart();
                                    separatorIndex = rest.IndexOf('=');
                                    if (separatorIndex >= 0)
                                    {
                                        ReadOnlySpan<char> name = rest[..separatorIndex].TrimEnd();
                                        switch (name)
                                        {
                                            case "AssemblyName":
                                                assembly = rest[(separatorIndex + 1)..].TrimStart().Trim(['\'', '"']).ToString();
                                                break;
                                            case "Features":
                                                ReadOnlySpan<char> value = rest[(separatorIndex + 1)..].TrimStart();
                                                foreach (Range range in value.SplitAny([',', ';', ' ']))
                                                {
                                                    ReadOnlySpan<char> feature = value[range].Trim();
                                                    if (feature.IsEmpty) { continue; }
                                                    separatorIndex = feature.IndexOf('=');
                                                    if (separatorIndex < 0)
                                                    {
                                                        features[feature.ToString()] = "true";
                                                    }
                                                    else
                                                    {
                                                        features[feature[..separatorIndex].TrimEnd().ToString()] = feature[(separatorIndex + 1)..].TrimStart().ToString();
                                                    }
                                                }
                                                break;
                                        }
                                    }
                                    break;
                            }
                        }
                    }
                }
                else if (trivia.IsKind(SyntaxKind.ReferenceDirectiveTrivia))
                {
                    switch (trivia.GetStructure())
                    {
                        case ReferenceDirectiveTriviaSyntax { File: { RawKind: (int)SyntaxKind.StringLiteralToken, Text: { Length: > 0 } content } } syntax:
                            ReadOnlySpan<char> file = content.AsSpan().Trim().Trim(['\'', '"']);
                            references.Add(file.ToString());
                            changes.Add(new TextChange(new TextSpan(syntax.SpanStart, 2), comment));
                            break;
                        case ReferenceDirectiveTriviaSyntax syntax:
                            ReadOnlySpan<char> message = syntax.ToString().AsSpan().Trim();
                            int separatorIndex = message.IndexOf(' ');
                            if (separatorIndex > 1)
                            {
                                file = message[(separatorIndex + 1)..].TrimStart().Trim(['\'', '"']);
                                references.Add(file.ToString());
                                changes.Add(new TextChange(new TextSpan(syntax.SpanStart, 2), comment));
                            }
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
                                ReadOnlySpan<char> message = syntax.ToString().AsSpan().TrimEnd();
                                int separatorIndex = message.IndexOf(' ');
                                if (separatorIndex > 1)
                                {
                                    ReadOnlySpan<char> feature = message[(separatorIndex + 1)..].TrimStart();
                                    if (feature.IsEmpty) { continue; }
                                    int equalsIndex = feature.IndexOf('=');
                                    if (equalsIndex < 0)
                                    {
                                        features[feature.ToString()] = "true";
                                    }
                                    else
                                    {
                                        features[feature[..equalsIndex].TrimEnd().ToString()] = feature[(equalsIndex + 1)..].TrimStart().ToString();
                                    }
                                    changes.Add(new TextChange(new TextSpan(syntax.SpanStart, 2), comment));
                                }
                                break;
                            case "assembly":
                                message = syntax.ToString().AsSpan().TrimEnd();
                                separatorIndex = message.IndexOf(' ');
                                if (separatorIndex > 1)
                                {
                                    assembly = message[(separatorIndex + 1)..].TrimStart().Trim(['\'', '"']).ToString();
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
                string line = text.ToString().Trim();
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
                    string path = temp.TrimStart().Trim(['\'', '"']).ToString();
                    references.Add(path);
                    changes.Add(new TextChange(new TextSpan(text.Start, 2), comment));
                }
                else if (line.StartsWith("#feature ", StringComparison.OrdinalIgnoreCase))
                {
                    ReadOnlySpan<char> feature = line.AsSpan()[9..].TrimStart();
                    if (feature.IsEmpty) { continue; }
                    int equalsIndex = feature.IndexOf('=');
                    if (equalsIndex < 0)
                    {
                        features[feature.ToString()] = "true";
                    }
                    else
                    {
                        features[feature[..equalsIndex].TrimEnd().ToString()] = feature[(equalsIndex + 1)..].TrimStart().ToString();
                    }
                    changes.Add(new TextChange(new TextSpan(text.Start, 2), comment));
                }
                else if (line.StartsWith("#assembly ", StringComparison.OrdinalIgnoreCase))
                {
                    assembly = line.AsSpan()[10..].TrimStart().Trim(['\'', '"']).ToString();
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
