using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using SharpScript.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SourceCodeKind = Microsoft.CodeAnalysis.SourceCodeKind;

namespace SharpScript
{
    public static class Program
    {
        private static Compiler Compiler
        {
            get => field ??= new(NullLoggerFactory.Instance);
            set;
        }

        private static WebAssemblyHost Current { get; set; }

        private static Task Main(string[] args)
        {
            WebAssemblyHostBuilder builder = WebAssemblyHostBuilder.CreateDefault(args);
            WebAssemblyHost current = Current = builder.Build();
            Compiler = new Compiler(current.Services.GetRequiredService<ILoggerFactory>());
            return current.RunAsync();
        }

        [JSInvokable]
        public static Task InitAsync(string baseUrl, IDictionary<string, string> fingerprinting) => RoslynCodeSession.InitAsync(baseUrl, fingerprinting, Current is WebAssemblyHost host ? host.Services.GetRequiredService<ILogger<RoslynCodeSession>>() : NullLogger<RoslynCodeSession>.Instance).AsTask();

        [JSInvokable]
        public static Task<CompileResult> ProcessAsync(string code) => Compiler.ProcessAsync(code).AsTask();

        [JSInvokable]
        public static Task<DotNetStreamReference> GetAssemblyAsync(string code) => Compiler.GetAssemblyAsync(code).AsTask().ContinueWith(x => x.Result is MemoryStream stream ? new DotNetStreamReference(stream) : null);

        [JSInvokable]
        public static Task<List<Diagnostic>> GetDiagnosticsAsync(string code) => Compiler.GetDiagnosticsAsync(code).AsTask();

        [JSInvokable]
        public static Task<IEnumerable<RoslynCompletionItem>> GetCompletionsAsync(string code, int position) => Compiler.GetCompletionsAsync(code, position);

        [JSInvokable]
        public static Task<InfoTipItem> GetInfoTipAsync(string code, int position) => Compiler.GetInfoTipAsync(code, position);

        [JSInvokable]
        public static Task<AstNodeItem> GetAstAsync(string code) => Compiler.GetAstAsync(code);

        [JSInvokable]
        public static Task<InfoTipItem> GetCSharpInfoTipLiteAsync(string code, int position) => InfoTipServer.GetInfoTipAsync(code, position);

        [JSInvokable]
        public static IEnumerable<string> GetLanguageTypes() => Compiler.LanguageTypes.Select(x => x.ToString());

        [JSInvokable]
        public static void SetLanguageType(string type) => Compiler.LanguageType = Enum.Parse<LanguageType>(type, true);

        [JSInvokable]
        public static string GetSourceCodeKind() => Compiler.SourceCodeKind.ToString();

        [JSInvokable]
        public static void SetSourceCodeKind(string kind) => Compiler.SourceCodeKind = Enum.Parse<SourceCodeKind>(kind, true);

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
        public static string GetInputLanguageVersion() => Compiler.InputLanguageVersion;

        [JSInvokable]
        public static void SetInputLanguageVersion(string version) => Compiler.InputLanguageVersion = version;

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
        public static string GetOutputLanguageVersion() => Compiler.OutputLanguageVersion;

        [JSInvokable]
        public static void SetOutputLanguageVersion(string version) => Compiler.OutputLanguageVersion = version;
    }
}