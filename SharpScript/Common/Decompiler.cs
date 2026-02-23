using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.CSharp.OutputVisitor;
using ICSharpCode.Decompiler.CSharp.Syntax;
using ICSharpCode.Decompiler.Disassembler;
using ICSharpCode.Decompiler.Metadata;
using SharpScript.Models;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SharpScript.Common
{
    public static class Decompiler
    {
        public static async ValueTask<string> CSharpDecompileAsync(CompilationResults streams, CSharpOutputOptions options, CancellationToken cancellationToken = default)
        {
            streams.Position = 0;
            using PEFile assemblyFile = new(string.Empty, streams.AssemblyStream);
            PortablePdbDebugInfoProvider? debugInfo = null;
            try
            {
                if (streams.SymbolStream is MemoryStream symbol)
                {
                    debugInfo = new PortablePdbDebugInfoProvider(symbol);
                }

                CSharpDecompiler decompiler =
                    new(assemblyFile,
                        new PreCachedAssemblyResolver(streams.References),
                        new DecompilerSettings(options.LanguageVersion))
                    {
                        DebugInfoProvider = debugInfo,
                        CancellationToken = cancellationToken,
                        DocumentationProvider = ContentBasedXmlDocumentationProvider.CreateFromBytes(streams.DocumentationStream?.ToArray())
                    };
                SyntaxTree syntaxTree = decompiler.DecompileWholeModuleAsSingleFile();

                SortTree(syntaxTree);

                StringBuilder code = new();
                await using StringWriter codeWriter = new(code);
                new ExtendedCSharpOutputVisitor(codeWriter, CreateFormattingOptions())
                    .VisitSyntaxTree(syntaxTree);
                return code.ToString();
            }
            finally
            {
                debugInfo?.Dispose();
            }
        }

        private static void SortTree(SyntaxTree root)
        {
            // Note: the sorting logic cannot be reused, but should match IL and Jit ASM ordering
            AstNode? firstMovedNode = null;
            foreach (AstNode node in root.Children)
            {
                if (node == firstMovedNode) { break; }
                if (node is NamespaceDeclaration @namespace && IsNonUserCode(@namespace))
                {
                    node.Remove();
                    root.AddChildWithExistingRole(node);
                    firstMovedNode ??= node;
                }
            }
        }

        private static bool IsNonUserCode(NamespaceDeclaration @namespace) =>
            // Note: the logic cannot be reused, but should match IL and Jit ASM
            @namespace.Members.Any(member => member is not TypeDeclaration type || !IsCompilerGenerated(type));

        private static bool IsCompilerGenerated(TypeDeclaration type) =>
            type.Attributes.Any(section => section.Attributes.Any(attribute => attribute.Type is SimpleType { Identifier: nameof(CompilerGeneratedAttribute) or "CompilerGenerated" }));

        private static CSharpFormattingOptions CreateFormattingOptions()
        {
            CSharpFormattingOptions options = FormattingOptionsFactory.CreateAllman();
            options.IndentationString = "    ";
            options.MinimumBlankLinesBetweenTypes = 1;
            return options;
        }

        public static async ValueTask<string> ILDecompileAsync(CompilationResults streams)
        {
            streams.Position = 0;
            using PEFile assemblyFile = new(string.Empty, streams.AssemblyStream);
            PortablePdbDebugInfoProvider? debugInfo = null;
            try
            {
                if (streams.SymbolStream is MemoryStream symbol)
                {
                    debugInfo = new PortablePdbDebugInfoProvider(symbol);
                }

                StringBuilder code = new();
                await using StringWriter codeWriter = new(code);

                PlainTextOutput output = new(codeWriter) { IndentationString = "    " };
                ReflectionDisassembler disassembler = new(output, default)
                {
                    DebugInfo = debugInfo,
                    ShowSequencePoints = true
                };

                disassembler.WriteAssemblyHeader(assemblyFile);
                output.WriteLine(); // empty line

                MetadataReader metadata = assemblyFile.Metadata;
                DecompileTypes(assemblyFile, output, disassembler, metadata);
                return code.ToString();
            }
            finally
            {
                debugInfo?.Dispose();
            }
        }

        private static void DecompileTypes(PEFile assemblyFile, PlainTextOutput output, ReflectionDisassembler disassembler, MetadataReader metadata)
        {
            const int MaxNonUserTypeHandles = 10;
            TypeDefinitionHandle[] nonUserTypeHandlesLease = [];
            int nonUserTypeHandlesCount = -1;

            // user code (first)                
            foreach (TypeDefinitionHandle typeHandle in metadata.TypeDefinitions)
            {
                TypeDefinition type = metadata.GetTypeDefinition(typeHandle);
                if (!type.GetDeclaringType().IsNil)
                {
                    continue; // not a top-level type
                }

                if (IsNonUserCode(metadata, type) && nonUserTypeHandlesCount < MaxNonUserTypeHandles)
                {
                    if (nonUserTypeHandlesCount == -1)
                    {
                        nonUserTypeHandlesLease = new TypeDefinitionHandle[MaxNonUserTypeHandles];
                        nonUserTypeHandlesCount = 0;
                    }

                    nonUserTypeHandlesLease[nonUserTypeHandlesCount] = typeHandle;
                    nonUserTypeHandlesCount += 1;
                    continue;
                }

                disassembler.DisassembleType(assemblyFile, typeHandle);
                output.WriteLine();
            }

            // non-user code (second)
            if (nonUserTypeHandlesCount > 0)
            {
                foreach (TypeDefinitionHandle typeHandle in nonUserTypeHandlesLease[..nonUserTypeHandlesCount])
                {
                    disassembler.DisassembleType(assemblyFile, typeHandle);
                    output.WriteLine();
                }
            }
        }

        private static bool IsNonUserCode(MetadataReader metadata, TypeDefinition type) =>
            // Note: the logic cannot be reused, but should match C# and Jit ASM
            !type.NamespaceDefinition.IsNil && type.IsCompilerGenerated(metadata);

        private class ExtendedCSharpOutputVisitor(TextWriter textWriter, CSharpFormattingOptions formattingPolicy) : CSharpOutputVisitor(textWriter, formattingPolicy)
        {
            public override void VisitTypeDeclaration(TypeDeclaration typeDeclaration)
            {
                base.VisitTypeDeclaration(typeDeclaration);
                if (typeDeclaration.NextSibling is NamespaceDeclaration or TypeDeclaration)
                { NewLine(); }
            }

            public override void VisitNamespaceDeclaration(NamespaceDeclaration namespaceDeclaration)
            {
                base.VisitNamespaceDeclaration(namespaceDeclaration);
                if (namespaceDeclaration.NextSibling is NamespaceDeclaration or TypeDeclaration)
                { NewLine(); }
            }

            public override void VisitAttributeSection(AttributeSection attributeSection)
            {
                base.VisitAttributeSection(attributeSection);
                if (attributeSection is { AttributeTarget: "assembly" or "module", NextSibling: not AttributeSection { AttributeTarget: "assembly" or "module" } })
                { NewLine(); }
            }
        }
    }
}
