using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;
using SharpScript.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SharpScript
{
    public static class Program
    {
        public static Compiler Compiler { get; } = new Compiler();
        public static WebAssemblyHost Current { get; private set; }

        private static async Task Main(string[] args)
        {
            WebAssemblyHostBuilder builder = WebAssemblyHostBuilder.CreateDefault(args);
            Current = builder.Build();
            await Current.RunAsync();
        }

        [JSInvokable]
        public static async Task InitAsync(string baseUrl) => await Compiler.InitAsync(baseUrl).ConfigureAwait(false);

        [JSInvokable]
        public static async Task<CompileResult> ProcessAsync(string code)
        {
            await Compiler.ProcessAsync(code).ConfigureAwait(false);
            return new CompileResult(Compiler.Diagnostics, Compiler.IsDecompile, Compiler.Decompiled);
        }

        [JSInvokable]
        public static IEnumerable<string> GetLanguageTypes() => Compiler.LanguageTypes.Select(x => x.ToString());

        [JSInvokable]
        public static void SetLanguageType(string type) => Compiler.Options.LanguageType = Enum.Parse<LanguageType>(type, true);

        [JSInvokable]
        public static IEnumerable<string> GetOutputTypes() => Compiler.OutputTypes.Select(x => x.ToString());

        [JSInvokable]
        public static void SetOutputType(string type) => Compiler.Options.OutputType = Enum.Parse<OutputType>(type, true);

        [JSInvokable]
        public static IEnumerable<string> GetInputLanguageVersions()
        {
            if (((IInputOptions)Compiler.Options.InputOptions).LanguageVersions is Array array)
            {
                foreach (object @enum in array)
                {
                    yield return @enum.ToString();
                }
            }
        }

        [JSInvokable]
        public static void SetInputLanguageVersion(string type)
        {
            if (((IInputOptions)Compiler.Options.InputOptions).LanguageVersion?.GetType() is Type @enum)
            {
                ((IInputOptions)Compiler.Options.InputOptions).LanguageVersion = (Enum)Enum.Parse(@enum, type, true);
            }
        }

        [JSInvokable]
        public static IEnumerable<string> GetOutputLanguageVersions()
        {
            if (((IOutputOptions)Compiler.Options.OutputOptions).IsCSharp)
            {
                foreach (object @enum in CSharpOutputOptions.LanguageVersions)
                {
                    yield return @enum.ToString();
                }
            }
        }

        [JSInvokable]
        public static void SetOutputLanguageVersion(string type)
        {
            if (((IOutputOptions)Compiler.Options.OutputOptions).LanguageVersion?.GetType() is Type @enum)
            {
                ((IOutputOptions)Compiler.Options.OutputOptions).LanguageVersion = (Enum)Enum.Parse(@enum, type, true);
            }
        }

        public record CompileResult(List<string> Diagnostics, bool IsDecompile, string Decompiled);
    }
}