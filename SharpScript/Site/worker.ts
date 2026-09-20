/// <reference types="./env.d.ts" />
import type { Diagnostic, TextChanges, ICodeActionObject, ICompletionItemObject } from "sharp-script";
import { expose } from "comlink";
import AsyncLock from "async-lock";

if (typeof window === "undefined") {
    self.Node = class { static readonly COMMENT_NODE = 8 } as any;
    self.Element = class extends self.Node { } as any;
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
        createComment() {
            return {};
        },
        createElement() {
            return {};
        },
        createElementNS() {
            return {};
        },
        createRange() {
            return {};
        },
        createTextNode() {
            return {};
        },
        getElementById() {
            return null;
        },
        hasChildNodes() {
            return false;
        },
        querySelector() {
            return null;
        },
        querySelectorAll() {
            return [];
        },
        removeEventListener() { }
    } as any;
    self.history = {} as any;
}

let diagnostics: ICodeActionObject[] = [], completions: ICompletionItemObject[] = [];
function getDiagnostics(list: Diagnostic[]) {
    if (list instanceof Array) {
        diagnostics.forEach(x => x.dispose());
        diagnostics = [];
        return list.map(diagnostic => {
            const { actions, ...result } = diagnostic;
            return {
                ...result,
                actions: diagnostic.actions.map(x => {
                    diagnostics.push(x.action);
                    return {
                        title: x.title,
                        action: diagnostics.length - 1
                    }
                })
            };
        });
    }
    return [];
}

function getFingerprinting() {
    let fingerprinting: Record<string, string> = Blazor.runtime.config.resources.fingerprinting;
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
export type Fingerprinting = ReturnType<typeof getFingerprinting>;
export type setProperty = typeof document.documentElement.style.setProperty;
const locker = new AsyncLock();

const dotnet = {
    init(baseURI?: string, onDownloadResourceProgress?: setProperty): void | Promise<void> {
        if (typeof Document === "undefined") {
            (document as any).baseURI = baseURI;
            if (onDownloadResourceProgress) {
                document.documentElement.style.setProperty = (x, y) => onDownloadResourceProgress(x, y);
            }
        }
    },
    get fingerprinting(): Fingerprinting | Promise<Fingerprinting> {
        return getFingerprinting();
    },
    async startAsync() {
        const url = "../_framework/blazor.webassembly.js";
        await import(/* @vite-ignore */ url);
        await Blazor.start();
    },
    initAsync() {
        return DotNet.invokeMethodAsync("SharpScript", "InitAsync", new URL("_framework/", document.baseURI).toString(), getFingerprinting());
    },
    resetCodeAsync(code: string) {
        return DotNet.invokeMethodAsync("SharpScript", "ResetCode", code);
    },
    applyChangesAsync(changes: TextChanges[]) {
        return DotNet.invokeMethodAsync("SharpScript", "ApplyChanges", changes);
    },
    async processAsync() {
        const { diagnostics, ...result } = await DotNet.invokeMethodAsync("SharpScript", "ProcessAsync");
        return {
            ...result,
            diagnostics: getDiagnostics(diagnostics)
        }
    },
    getAssemblyAsync() {
        return DotNet.invokeMethodAsync("SharpScript", "GetAssemblyAsync");
    },
    async getDiagnosticsAsync() {
        const result = await DotNet.invokeMethodAsync("SharpScript", "GetDiagnosticsAsync");
        return getDiagnostics(result);
    },
    async getCompletionsAsync(position: number) {
        const result = await DotNet.invokeMethodAsync("SharpScript", "GetCompletionsAsync", position);
        if (result instanceof Array) {
            completions.forEach(x => x.dispose());
            completions = [];
            return result.map(x => {
                const { self, ...result } = x;
                completions.push(x.self);
                return {
                    ...result,
                    self: completions.length - 1
                };
            });
        }
        return [];
    },
    getInfoTipAsync(position: number) {
        return DotNet.invokeMethodAsync("SharpScript", "GetInfoTipAsync", position);
    },
    getAstAsync() {
        return DotNet.invokeMethodAsync("SharpScript", "GetAstAsync");
    },
    formatCodeAsync() {
        return DotNet.invokeMethodAsync("SharpScript", "FormatCodeAsync");
    },
    setCSharpInfoTipLiteAsync(code: string) {
        return DotNet.invokeMethodAsync("SharpScript", "SetCSharpInfoTipLite", code);
    },
    getCSharpInfoTipLiteAsync(position: number) {
        return DotNet.invokeMethodAsync("SharpScript", "GetCSharpInfoTipLiteAsync", position);
    },
    getLanguageTypesAsync() {
        return locker.acquire("inputLanguage", () => DotNet.invokeMethodAsync("SharpScript", "GetLanguageTypes"));
    },
    setLanguageTypeAsync(type: string) {
        return locker.acquire("inputLanguage", () => DotNet.invokeMethodAsync("SharpScript", "SetLanguageType", type));
    },
    getSourceCodeKind() {
        return DotNet.invokeMethodAsync("SharpScript", "GetSourceCodeKind");
    },
    setSourceCodeKind(kind: string) {
        return DotNet.invokeMethodAsync("SharpScript", "SetSourceCodeKind", kind);
    },
    getOutputTypesAsync() {
        return locker.acquire("outputLanguage", () => DotNet.invokeMethodAsync("SharpScript", "GetOutputTypes"));
    },
    setOutputTypeAsync(type: string) {
        return locker.acquire("outputLanguage", () => DotNet.invokeMethodAsync("SharpScript", "SetOutputType", type));
    },
    getInputLanguageVersionsAsync() {
        return locker.acquire("inputLanguage", () => DotNet.invokeMethodAsync("SharpScript", "GetInputLanguageVersions"));
    },
    getInputLanguageVersionAsync() {
        return locker.acquire("inputLanguage", () => DotNet.invokeMethodAsync("SharpScript", "GetInputLanguageVersion"));
    },
    setInputLanguageVersionAsync(version?: string) {
        return locker.acquire("inputLanguage", () => DotNet.invokeMethodAsync("SharpScript", "SetInputLanguageVersion", version));
    },
    getOutputLanguageVersionsAsync() {
        return locker.acquire("outputLanguage", () => DotNet.invokeMethodAsync("SharpScript", "GetOutputLanguageVersions"));
    },
    getOutputLanguageVersionAsync() {
        return locker.acquire("outputLanguage", () => DotNet.invokeMethodAsync("SharpScript", "GetOutputLanguageVersion"));
    },
    setOutputLanguageVersionAsync(version?: string) {
        return locker.acquire("outputLanguage", () => DotNet.invokeMethodAsync("SharpScript", "SetOutputLanguageVersion", version));
    },
    invokeMethodAsync(assembly: string, method: string, ...args: any[]) {
        return DotNet.invokeMethodAsync(assembly, method, ...args);
    },
    async diagnosticInvokeAsync(index: number) {
        const diagnostic = diagnostics[index];
        if (diagnostic) {
            return diagnostic.invokeMethodAsync("InvokeAsync");
        }
    },
    async completionGetDescriptionAsync(index: number) {
        const completion = completions[index];
        if (completion) {
            return completion.invokeMethodAsync("GetDescriptionAsync");
        }
    },
    async completionGetChangeAsync(index: number) {
        const completion = completions[index];
        if (completion) {
            return completion.invokeMethodAsync("GetChangeAsync");
        }
    },
    async getAssemblyLinkAsync() {
        const assembly = await this.getAssemblyAsync();
        if (assembly) {
            const file = new File([await assembly.arrayBuffer()], "SharpScript.Playground.zip");
            return URL.createObjectURL(file);
        }
    }
};

declare const WorkerGlobalScope: ObjectConstructor;
if (typeof WorkerGlobalScope !== "undefined" && self instanceof WorkerGlobalScope) {
    expose(dotnet);
}

export { dotnet };
export type DiagnosticWrapper = Awaited<ReturnType<typeof dotnet.getDiagnosticsAsync>>[number];
export type DotNetWorker = typeof dotnet;