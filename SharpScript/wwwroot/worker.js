importScripts("https://cdn.jsdelivr.net/npm/comlink/dist/umd/comlink.min.js");
const window = self;
const document = {
    baseURI: location.href,
    childNodes: [],
    documentElement: {
        style: {
            setProperty() { }
        }
    },
    addEventListener() { },
    createElement() {
        return {};
    },
    createElementNS() {
        return {};
    },
    hasChildNodes() {
        return false;
    }
};
const Node = { COMMENT_NODE: 8 };
const history = { state: {} };
let diagnostics = [], completions = [];
const dotnet = {
    init(baseURI, onDownloadResourceProgress) {
        document.baseURI = baseURI;
        document.documentElement.style.setProperty = (x, y) => onDownloadResourceProgress(x, y);
    },
    async startAsync() {
        importScripts("_framework/blazor.webassembly.js");
        await Blazor.start();
    },
    async initAsync() {
        let fingerprinting = Blazor.runtime.config.resources.fingerprinting;
        if (!fingerprinting) {
            fingerprinting = {};
            for (const x of Blazor.runtime.config.resources.coreAssembly) {
                fingerprinting[x.name] = x.virtualPath;
            }
            for (const x of Blazor.runtime.config.resources.assembly) {
                fingerprinting[x.name] = x.virtualPath;
            }
        }
        return await DotNet.invokeMethodAsync("SharpScript", "InitAsync", new URL("_framework/", document.baseURI).toString(), fingerprinting);
    },
    async processAsync(code) {
        return await DotNet.invokeMethodAsync("SharpScript", "ProcessAsync", code);
    },
    async getAssemblyAsync(code) {
        return await DotNet.invokeMethodAsync("SharpScript", "GetAssemblyAsync", code);
    },
    async getDiagnosticsAsync(code) {
        const result = await DotNet.invokeMethodAsync("SharpScript", "GetDiagnosticsAsync", code);
        if (result instanceof Array) {
            diagnostics.forEach(x => x.dispose());
            diagnostics = [];
            result.forEach(diagnostic => {
                diagnostic.actions = diagnostic.actions.map(x => {
                    diagnostics.push(x.action);
                    return {
                        title: x.title,
                        action: diagnostics.length - 1
                    }
                });
            });
        }
        return result;
    },
    async getCompletionsAsync(code, position) {
        const result = await DotNet.invokeMethodAsync("SharpScript", "GetCompletionsAsync", code, position);
        if (result instanceof Array) {
            completions.forEach(x => x.dispose());
            completions = [];
            result.forEach(x => {
                completions.push(x.self);
                x.self = completions.length - 1;
            });
        }
        return result;
    },
    async getInfoTipAsync(code, position) {
        return await DotNet.invokeMethodAsync("SharpScript", "GetInfoTipAsync", code, position);
    },
    async getLanguageTypesAsync() {
        return await DotNet.invokeMethodAsync("SharpScript", "GetLanguageTypes");
    },
    async setLanguageTypeAsync(type) {
        return await DotNet.invokeMethodAsync("SharpScript", "SetLanguageType", type);
    },
    async getOutputTypesAsync() {
        return await DotNet.invokeMethodAsync("SharpScript", "GetOutputTypes");
    },
    async setOutputTypeAsync(type) {
        return await DotNet.invokeMethodAsync("SharpScript", "SetOutputType", type);
    },
    async getInputLanguageVersionsAsync() {
        return await DotNet.invokeMethodAsync("SharpScript", "GetInputLanguageVersions");
    },
    async getInputLanguageVersionAsync() {
        return await DotNet.invokeMethodAsync("SharpScript", "GetInputLanguageVersion");
    },
    async setInputLanguageVersionAsync(type) {
        return await DotNet.invokeMethodAsync("SharpScript", "SetInputLanguageVersion", type);
    },
    async getOutputLanguageVersionsAsync() {
        return await DotNet.invokeMethodAsync("SharpScript", "GetOutputLanguageVersions");
    },
    async getOutputLanguageVersionAsync() {
        return await DotNet.invokeMethodAsync("SharpScript", "GetOutputLanguageVersion");
    },
    async setOutputLanguageVersionAsync(type) {
        return await DotNet.invokeMethodAsync("SharpScript", "SetOutputLanguageVersion", type);
    },
    async invokeMethodAsync(assembly, method, ...args) {
        return await DotNet.invokeMethodAsync(assembly, method, ...args);
    },
    async diagnosticInvokeAsync(index) {
        const diagnostic = diagnostics[index];
        if (diagnostic) {
            return await diagnostic.invokeMethodAsync("InvokeAsync");
        }
    },
    async completionGetDescriptionAsync(index) {
        const completion = completions[index];
        if (completion) {
            return await completion.invokeMethodAsync("GetDescriptionAsync");
        }
    },
    async completionGetChangeAsync(index) {
        const completion = completions[index];
        if (completion) {
            return await completion.invokeMethodAsync("GetChangeAsync");
        }
    },
    async getAssemblyLinkAsync(code) {
        const assembly = await this.getAssemblyAsync(code);
        const file = new File([await assembly.arrayBuffer()], "SharpScript.dll");
        return URL.createObjectURL(file);
    }
};
Comlink.expose(dotnet);