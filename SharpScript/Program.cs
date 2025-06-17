using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
        public static Compiler Compiler { get; private set; }
        public static WebAssemblyHost Current { get; private set; }

        private static Task Main(string[] args)
        {
            WebAssemblyHostBuilder builder = WebAssemblyHostBuilder.CreateDefault(args);
            Current = builder.Build();
            Compiler = new Compiler(Current.Services.GetRequiredService<ILoggerFactory>());
            return Current.RunAsync();
        }

        [JSInvokable]
        public static Task InitAsync(string baseUrl) => RoslynCodeSession.InitAsync(baseUrl, Current.Services.GetRequiredService<ILogger<RoslynCodeSession>>()).AsTask();

        [JSInvokable]
        public static Task<CompileResult> ProcessAsync(string code) => Compiler.ProcessAsync(code).AsTask();

        [JSInvokable]
        public static Task<DotNetStreamReference> GetAssemblyAsync(string code) => Compiler.GetAssemblyAsync(code).AsTask().ContinueWith(x => x.Result == null ? null : new DotNetStreamReference(x.Result));

        [JSInvokable]
        public static Task<List<Diagnostic>> GetDiagnosticsAsync(string code) => Compiler.GetDiagnosticsAsync(code).AsTask();

        [JSInvokable]
        public static Task<IEnumerable<RoslynCompletionItem>> GetCompletionsAsync(string code, int position) => Compiler.GetCompletionsAsync(code, position);

        [JSInvokable]
        public static Task<InfoTipItem> GetInfoTipAsync(string code, int position) => Compiler.GetInfoTipAsync(code, position);

        [JSInvokable]
        public static IEnumerable<string> GetLanguageTypes() => Compiler.LanguageTypes.Select(x => x.ToString());

        [JSInvokable]
        public static void SetLanguageType(string type) => Compiler.LanguageType = Enum.Parse<LanguageType>(type, true);

        [JSInvokable]
        public static IEnumerable<string> GetOutputTypes() => Compiler.OutputTypes.Select(x => x.ToString());

        [JSInvokable]
        public static void SetOutputType(string type) => Compiler.OutputType = Enum.Parse<OutputType>(type, true);

        [JSInvokable]
        public static IEnumerable<string> GetInputLanguageVersions()
        {
            if (((IInputOptions)Compiler.InputOptions).LanguageVersions is Array array)
            {
                foreach (object @enum in array)
                {
                    yield return @enum.ToString();
                }
            }
        }

        [JSInvokable]
        public static string GetInputLanguageVersion() => ((IInputOptions)Compiler.InputOptions).LanguageVersion?.ToString() ?? string.Empty;

        [JSInvokable]
        public static void SetInputLanguageVersion(string type)
        {
            if (((IInputOptions)Compiler.InputOptions).LanguageVersion?.GetType() is Type @enum)
            {
                ((IInputOptions)Compiler.InputOptions).LanguageVersion = (Enum)Enum.Parse(@enum, type, true);
            }
        }

        [JSInvokable]
        public static IEnumerable<string> GetOutputLanguageVersions()
        {
            if (((IOutputOptions)Compiler.OutputOptions).IsCSharp)
            {
                foreach (object @enum in CSharpOutputOptions.LanguageVersions)
                {
                    yield return @enum.ToString();
                }
            }
        }

        [JSInvokable]
        public static string GetOutputLanguageVersion() => ((IOutputOptions)Compiler.OutputOptions).LanguageVersion?.ToString() ?? string.Empty;

        [JSInvokable]
        public static void SetOutputLanguageVersion(string type)
        {
            if (((IOutputOptions)Compiler.OutputOptions).LanguageVersion?.GetType() is Type @enum)
            {
                ((IOutputOptions)Compiler.OutputOptions).LanguageVersion = (Enum)Enum.Parse(@enum, type, true);
            }
        }
    }
}