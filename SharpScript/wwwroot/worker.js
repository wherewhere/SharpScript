import AsyncLock from "https://cdn.jsdelivr.net/npm/async-lock/+esm";
import * as Comlink from "https://cdn.jsdelivr.net/npm/comlink/+esm";
if (typeof window === "undefined") {
    self.window = self;
    self.document = {
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
        },
        querySelector() {
            return null;
        }
    };
    self.Node = { COMMENT_NODE: 8 };
    self.history = { state: {} };
}
let diagnostics = [], completions = [];
function getFingerprinting() {
    let fingerprinting = Blazor.runtime.config.resources.fingerprinting;
    if (!fingerprinting) {
        fingerprinting = {};
        for (const x of Blazor.runtime.config.resources.coreAssembly) {
            fingerprinting[x.name] = x.virtualPath;
        }
        for (const x of Blazor.runtime.config.resources.assembly) {
            fingerprinting[x.name] = x.virtualPath;
        }
        Blazor.runtime.config.resources.fingerprinting = fingerprinting;
    }
    return fingerprinting;
}
const locker = new AsyncLock();
const dotnet = {
    init(baseURI, onDownloadResourceProgress) {
        if (typeof Document === "undefined") {
            document.baseURI = baseURI;
            if (onDownloadResourceProgress) {
                document.documentElement.style.setProperty = (x, y) => onDownloadResourceProgress(x, y);
            }
        }
    },
    get fingerprinting() {
        return getFingerprinting();
    },
    async startAsync() {
        await import("./_framework/blazor.webassembly.js");
        await Blazor.start();
    },
    async initAsync() {
        return await DotNet.invokeMethodAsync("SharpScript", "InitAsync", new URL("_framework/", document.baseURI).toString(), getFingerprinting());
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
    async getAstAsync(code) {
        return await DotNet.invokeMethodAsync("SharpScript", "GetAstAsync", code);
    },
    async getCSharpInfoTipLiteAsync(code, position) {
        return await DotNet.invokeMethodAsync("SharpScript", "GetCSharpInfoTipLiteAsync", code, position);
    },
    async getLanguageTypesAsync() {
        return await locker.acquire("inputLanguage", () => DotNet.invokeMethodAsync("SharpScript", "GetLanguageTypes"));
    },
    async setLanguageTypeAsync(type) {
        return await locker.acquire("inputLanguage", () => DotNet.invokeMethodAsync("SharpScript", "SetLanguageType", type));
    },
    async getSourceCodeKind() {
        return await DotNet.invokeMethodAsync("SharpScript", "GetSourceCodeKind");
    },
    async setSourceCodeKind(kind) {
        return await DotNet.invokeMethodAsync("SharpScript", "SetSourceCodeKind", kind);
    },
    async getOutputTypesAsync() {
        return await locker.acquire("outputLanguage", () => DotNet.invokeMethodAsync("SharpScript", "GetOutputTypes"));
    },
    async setOutputTypeAsync(type) {
        return await locker.acquire("outputLanguage", () => DotNet.invokeMethodAsync("SharpScript", "SetOutputType", type));
    },
    async getInputLanguageVersionsAsync() {
        return await locker.acquire("inputLanguage", () => DotNet.invokeMethodAsync("SharpScript", "GetInputLanguageVersions"));
    },
    async getInputLanguageVersionAsync() {
        return await locker.acquire("inputLanguage", () => DotNet.invokeMethodAsync("SharpScript", "GetInputLanguageVersion"));
    },
    async setInputLanguageVersionAsync(version) {
        return await locker.acquire("inputLanguage", () => DotNet.invokeMethodAsync("SharpScript", "SetInputLanguageVersion", version));
    },
    async getOutputLanguageVersionsAsync() {
        return await locker.acquire("outputLanguage", () => DotNet.invokeMethodAsync("SharpScript", "GetOutputLanguageVersions"));
    },
    async getOutputLanguageVersionAsync() {
        return await locker.acquire("outputLanguage", () => DotNet.invokeMethodAsync("SharpScript", "GetOutputLanguageVersion"));
    },
    async setOutputLanguageVersionAsync(version) {
        return await locker.acquire("outputLanguage", () => DotNet.invokeMethodAsync("SharpScript", "SetOutputLanguageVersion", version));
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
        const file = new File([await assembly.arrayBuffer()], "SharpScript.zip");
        return URL.createObjectURL(file);
    }
};
if (typeof WorkerGlobalScope !== "undefined" && self instanceof WorkerGlobalScope) {
    Comlink.expose(dotnet);
}
export { dotnet };