using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using SharpScript.Common;
using System;
using System.Collections.Generic;
using System.Net.Http;
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
        public static async Task InitAsync(string baseUrl)
        {
            await Compiler.InitAsync(baseUrl).ConfigureAwait(false);
        }

        [JSInvokable]
        public static async Task<List<string>> ProcessAsync(string code)
        {
            await Compiler.ProcessAsync(code).ConfigureAwait(false);
            return Compiler.Diagnostics;
        }
    }
}