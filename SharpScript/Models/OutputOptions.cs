using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using LanguageVersion = ICSharpCode.Decompiler.CSharp.LanguageVersion;

namespace SharpScript.Models
{
    [Flags]
    public enum OutputType
    {
        CSharp = 0b0011,
        VisualBasic = 0b0111,
        IL = 0b0001,
        Run = 0b1000
    }

    public interface IOutputOptions
    {
        bool IsCSharp => false;
        Enum? LanguageVersion { get => default; set { } }
    }

    public abstract class OutputOptions : IOutputOptions;

    public sealed class CSharpOutputOptions : OutputOptions, IOutputOptions
    {
        public static List<LanguageVersion> LanguageVersions
        {
            get
            {
                List<LanguageVersion> list = [.. Enum.GetValues<LanguageVersion>()];
                _ = list.Remove(LanguageVersion.Preview);
                return list;
            }
        }

        bool IOutputOptions.IsCSharp => true;
        Enum? IOutputOptions.LanguageVersion
        {
            [return: NotNull]
            get => LanguageVersion;
            set => LanguageVersion = (LanguageVersion)(value ?? LanguageVersion.CSharp1);
        }

        public LanguageVersion LanguageVersion { get; set; } = LanguageVersion.CSharp1;
    }

    public sealed class ILOutputOptions : OutputOptions;

    public sealed class RunOutputOptions : OutputOptions;
}
