/// <reference types="./env.d.ts" />
import type { Diagnostic, TextChanges, ICodeActionObject, ICompletionItemObject } from "sharp-script";
import { AsyncLock, Comlink } from "./helpers/shared";

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
    } as any;
    self.history = {} as any;
    self.Element = function () { } as any;
    self.Node = function () { } as any;
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
    async initAsync() {
        return await DotNet.invokeMethodAsync("SharpScript", "InitAsync", new URL("_framework/", document.baseURI).toString(), getFingerprinting());
    },
    async resetCodeAsync(code: string) {
        return await DotNet.invokeMethodAsync("SharpScript", "ResetCode", code);
    },
    async applyChangesAsync(changes: TextChanges[]) {
        return await DotNet.invokeMethodAsync("SharpScript", "ApplyChanges", changes);
    },
    async processAsync() {
        const { diagnostics, ...result } = await DotNet.invokeMethodAsync("SharpScript", "ProcessAsync");
        return {
            ...result,
            diagnostics: getDiagnostics(diagnostics)
        }
    },
    async getAssemblyAsync() {
        return await DotNet.invokeMethodAsync("SharpScript", "GetAssemblyAsync");
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
    async getInfoTipAsync(position: number) {
        return await DotNet.invokeMethodAsync("SharpScript", "GetInfoTipAsync", position);
    },
    async getAstAsync() {
        return await DotNet.invokeMethodAsync("SharpScript", "GetAstAsync");
    },
    async formatCodeAsync() {
        return await DotNet.invokeMethodAsync("SharpScript", "FormatCodeAsync");
    },
    async setCSharpInfoTipLiteAsync(code: string) {
        return await DotNet.invokeMethodAsync("SharpScript", "SetCSharpInfoTipLite", code);
    },
    async getCSharpInfoTipLiteAsync(position: number) {
        return await DotNet.invokeMethodAsync("SharpScript", "GetCSharpInfoTipLiteAsync", position);
    },
    async getLanguageTypesAsync() {
        return await locker.acquire("inputLanguage", () => DotNet.invokeMethodAsync("SharpScript", "GetLanguageTypes"));
    },
    async setLanguageTypeAsync(type: string) {
        return await locker.acquire("inputLanguage", () => DotNet.invokeMethodAsync("SharpScript", "SetLanguageType", type));
    },
    async getSourceCodeKind() {
        return await DotNet.invokeMethodAsync("SharpScript", "GetSourceCodeKind");
    },
    async setSourceCodeKind(kind: string) {
        return await DotNet.invokeMethodAsync("SharpScript", "SetSourceCodeKind", kind);
    },
    async getOutputTypesAsync() {
        return await locker.acquire("outputLanguage", () => DotNet.invokeMethodAsync("SharpScript", "GetOutputTypes"));
    },
    async setOutputTypeAsync(type: string) {
        return await locker.acquire("outputLanguage", () => DotNet.invokeMethodAsync("SharpScript", "SetOutputType", type));
    },
    async getInputLanguageVersionsAsync() {
        return await locker.acquire("inputLanguage", () => DotNet.invokeMethodAsync("SharpScript", "GetInputLanguageVersions"));
    },
    async getInputLanguageVersionAsync() {
        return await locker.acquire("inputLanguage", () => DotNet.invokeMethodAsync("SharpScript", "GetInputLanguageVersion"));
    },
    async setInputLanguageVersionAsync(version?: string) {
        return await locker.acquire("inputLanguage", () => DotNet.invokeMethodAsync("SharpScript", "SetInputLanguageVersion", version));
    },
    async getOutputLanguageVersionsAsync() {
        return await locker.acquire("outputLanguage", () => DotNet.invokeMethodAsync("SharpScript", "GetOutputLanguageVersions"));
    },
    async getOutputLanguageVersionAsync() {
        return await locker.acquire("outputLanguage", () => DotNet.invokeMethodAsync("SharpScript", "GetOutputLanguageVersion"));
    },
    async setOutputLanguageVersionAsync(version?: string) {
        return await locker.acquire("outputLanguage", () => DotNet.invokeMethodAsync("SharpScript", "SetOutputLanguageVersion", version));
    },
    async invokeMethodAsync(assembly: string, method: string, ...args: any[]) {
        return await DotNet.invokeMethodAsync(assembly, method, ...args);
    },
    async diagnosticInvokeAsync(index: number) {
        const diagnostic = diagnostics[index];
        if (diagnostic) {
            return await diagnostic.invokeMethodAsync("InvokeAsync");
        }
    },
    async completionGetDescriptionAsync(index: number) {
        const completion = completions[index];
        if (completion) {
            return await completion.invokeMethodAsync("GetDescriptionAsync");
        }
    },
    async completionGetChangeAsync(index: number) {
        const completion = completions[index];
        if (completion) {
            return await completion.invokeMethodAsync("GetChangeAsync");
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
    Comlink.expose(dotnet);
}

export { dotnet };
export type DiagnosticWrapper = Awaited<ReturnType<typeof dotnet.getDiagnosticsAsync>>[number];
export type DotNetWorker = typeof dotnet;