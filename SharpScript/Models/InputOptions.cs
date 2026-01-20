using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.VisualBasic;
using System;
using CSharpLanguageVersion = Microsoft.CodeAnalysis.CSharp.LanguageVersion;
using VisualBasicLanguageVersion = Microsoft.CodeAnalysis.VisualBasic.LanguageVersion;

namespace SharpScript.Models
{
    [Flags]
    public enum LanguageType
    {
        CSharp = 0b011,
        VisualBasic = 0b111,
        IL = 0b001
    }

    public interface IInputOptions
    {
        string LanguageName => null;
        Array LanguageVersions => null;
        Enum LanguageVersion { get => null; set { } }
    }

    public abstract class InputOptions : IInputOptions;

    public abstract class RoslynOptions : InputOptions, IInputOptions
    {
        public static SourceCodeKind SourceCodeKind { get; set; } = SourceCodeKind.Regular;

        public virtual string LanguageName => this switch
        {
            CSharpInputOptions => LanguageNames.CSharp,
            VisualBasicInputOptions => LanguageNames.VisualBasic,
            _ => throw new Exception("Invalid language type.")
        };

        public void GetOptions(bool isConsole, out CompilationOptions compilation, out ParseOptions parse)
        {
            switch (this)
            {
                case CSharpInputOptions csharp:
                    CSharpLanguageVersion version = csharp.LanguageVersion;
                    NullableContextOptions nullable = version >= CSharpLanguageVersion.CSharp8
                        ? NullableContextOptions.Enable
                        : NullableContextOptions.Disable;
                    compilation = new CSharpCompilationOptions(
                        isConsole ? OutputKind.ConsoleApplication : OutputKind.DynamicallyLinkedLibrary,
                        optimizationLevel: OptimizationLevel.Release,
                        allowUnsafe: true,
                        nullableContextOptions: nullable);
                    parse = new CSharpParseOptions(
                        version,
                        DocumentationMode.Parse,
                        SourceCodeKind);
                    break;
                case VisualBasicInputOptions vb:
                    compilation = new VisualBasicCompilationOptions(
                        isConsole ? OutputKind.ConsoleApplication : OutputKind.DynamicallyLinkedLibrary,
                        optimizationLevel: OptimizationLevel.Release);
                    parse = new VisualBasicParseOptions(
                        vb.LanguageVersion,
                        DocumentationMode.Parse,
                        SourceCodeKind);
                    break;
                default:
                    throw new Exception("Invalid language type.");
            }
        }
    }

    public sealed class CSharpInputOptions : RoslynOptions, IInputOptions
    {
        Array IInputOptions.LanguageVersions => Enum.GetValues<CSharpLanguageVersion>();
        Enum IInputOptions.LanguageVersion
        {
            get => LanguageVersion;
            set => LanguageVersion = (CSharpLanguageVersion)(value ?? CSharpLanguageVersion.Preview);
        }

        public override string LanguageName => LanguageNames.CSharp;
        public CSharpLanguageVersion LanguageVersion { get; set; } = CSharpLanguageVersion.Preview;
    }

    public sealed class VisualBasicInputOptions : RoslynOptions, IInputOptions
    {
        Array IInputOptions.LanguageVersions => Enum.GetValues<VisualBasicLanguageVersion>();
        Enum IInputOptions.LanguageVersion
        {
            get => LanguageVersion;
            set => LanguageVersion = (VisualBasicLanguageVersion)(value ?? VisualBasicLanguageVersion.Latest);
        }

        public override string LanguageName => LanguageNames.VisualBasic;
        public VisualBasicLanguageVersion LanguageVersion { get; set; } = VisualBasicLanguageVersion.Latest;
    }

    public sealed class ILInputOptions : InputOptions;
}
