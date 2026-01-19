/// <reference types="vite/client" />
/// <reference types="vite-svg-loader" />
/// <reference types="vue-i18n" />

declare module "sharp-script" {
    /**
     * Immutable representation of a line number and position within a SourceText instance.
     */
    export type LinePosition = {
        /**
         * The line number. The first line in a file is defined as line 0 (zero based line
         * numbering).
         */
        readonly line: number;
        /**
         * The character position within the line.
         */
        readonly character: number;
    };

    /**
     * Immutable span represented by a pair of line number and index within the line.
     */
    export type LinePositionSpan = {
        /**
         * Gets the start position of the span.
         */
        readonly start: LinePosition;
        /**
         * Gets the end position of the span.
         */
        readonly end: LinePosition;
    };

    /**
     * Describes how severe a diagnostic is.
     */
    export type DiagnosticSeverity = "Hidden" | "Info" | "Warning" | "Error";

    /**
     * Immutable abstract representation of a span of text. For example, in an error
     * diagnostic that reports a location, it could come from a parsed string, text
     * from a tool editor buffer, etc.
     */
    export type TextSpan = {
        /**
         * Start point of the span.
         */
        readonly start: number;
        /**
         * End of the span.
         */
        readonly end: number;
        /**
         * Length of the span.
         */
        readonly length: number;
        /**
         * Determines whether or not the span is empty.
         */
        readonly isEmpty: boolean;
    };

    /**
     * Describes a single change when a particular span is replaced with a new text.
     */
    export type TextChange = {
        /**
         * The original span of the changed text.
         */
        readonly span: TextSpan;
        /**
         * The new text.
         */
        readonly newText?: string;
    };

    export interface DotNetObject {
        /**
         * Invokes the specified .NET instance public method asynchronously.
         * @param methodIdentifier The identifier of the method to invoke. The method must have a [JSInvokable] attribute specifying this identifier.
         * @param args Arguments to pass to the method, each of which must be JSON-serializable.
         * @returns A promise representing the result of the operation.
         */
        invokeMethodAsync<T>(methodIdentifier: string, ...args: any[]): Promise<T>;
        /**
         * Dispose the specified .NET instance.
         */
        dispose(): void;
    }

    export type ICodeActionObject = {
        invokeMethodAsync(methodIdentifier: "InvokeAsync"): Promise<TextChange[]>;
    } & DotNetObject;

    export interface ICodeAction {
        readonly title: string;
        readonly action: ICodeActionObject;
    }

    export type Diagnostic = {
        readonly id: string;
        readonly location: LinePositionSpan;
        readonly message: string;
        readonly severity: DiagnosticSeverity;
        readonly tags: string[];
        readonly actions: ICodeAction[];
    };

    export type CompileResult = {
        readonly diagnostics: Diagnostic[];
        readonly decompiled: string | null;
        readonly outputs: string[];
    };

    /**
     * A piece of text with a descriptive tag.
     */
    export type TaggedText = {
        /**
         * A descriptive tag from Microsoft.CodeAnalysis.TextTags.
         */
        readonly tag: string;
        /**
         * The actual text to be displayed.
         */
        readonly text: string;
    }

    export type CompletionChange = {
        readonly textChanges: TextChange[];
        readonly newPosition?: number;
    };

    export type ICompletionItemObject = {
        invokeMethodAsync(methodIdentifier: "GetDescriptionAsync"): Promise<TaggedText[]>;
        invokeMethodAsync(methodIdentifier: "GetChangeAsync", ...args: any[]): Promise<CompletionChange>;
    } & DotNetObject;

    export interface ICompletionItem {
        readonly displayText: string;
        readonly filterText: string;
        readonly sortText: string;
        readonly inlineDescription: string;
        readonly tags: string[];
        readonly span: TextSpan;
        readonly self: ICompletionItemObject;
    }

    export type InfoTipTaggedText = {
        readonly tag: string;
        readonly text: string;
    };

    export type InfoTipSection = {
        readonly kind: string;
        readonly parts: InfoTipTaggedText[];
    };

    export type InfoTipItem = {
        readonly tags: string[];
        readonly span: TextSpan;
        readonly sections: InfoTipSection[];
    };

    export type AstItemMap = {
        "node": AstNodeItem;
        "operation": AstOperationItem;
        "token": AstTokenItem;
        "trivia": AstTriviaItem;
        "value": AstValueItem;
    }

    export type AstItemBase<T extends keyof AstItemMap = keyof AstItemMap> = {
        readonly type: T;
    };

    export type AstItemChild = AstItemMap[keyof AstItemMap];

    export type AstItemAll = {
        readonly type: keyof AstItemMap;
    } & Partial<Omit<AstOperationItem, "type" | "property">>
        & Partial<Omit<AstTokenItem, "type">>
        & Partial<Omit<AstValueItem, "type">>;

    export type AstNodeItem = {
        readonly property: string;
        readonly kind: string;
        readonly span: TextSpan;
        readonly children: AstItemChild[];
    } & AstItemBase<"node">;

    export type AstOperationItem = {
        readonly property: "Operation";
        readonly kind: string;
    } & AstItemBase<"operation">;

    export type AstTokenItem = {
        readonly value: string;
    } & Omit<AstNodeItem, "type"> & AstItemBase<"token">;

    export type AstTriviaItem = Omit<AstNodeItem, "type"> & AstItemBase<"trivia">;

    export type AstValueItem = {
        readonly span: TextSpan;
        readonly value: string;
    } & AstItemBase<"value">;
}

declare module "async-lock" {
    namespace AsyncLock {
        type AsyncLockDoneCallback<T> = (err?: Error | null, ret?: T) => void;
        interface AsyncLockOptions { }
    }
    class AsyncLock {
        constructor(options?: AsyncLock.AsyncLockOptions);
        /**
         * Lock on asynchronous code.
         *
         * @param key resource key or keys to lock
         * @param fn function to execute
         * @param opts options
         *
         * @example
         * import AsyncLock = require('async-lock');
         * const lock = new AsyncLock();
         *
         * lock.acquire(
         *     key,
         *     () => {
         *         // return value or promise
         *     },
         *     opts
         * ).then(() => {
         *     // lock released
         * });
         */
        acquire<T>(
            key: string | string[],
            fn: (() => T | PromiseLike<T>) | ((done: AsyncLock.AsyncLockDoneCallback<T>) => any),
            opts?: AsyncLock.AsyncLockOptions,
        ): Promise<T>;
    }
    export = AsyncLock;
}

declare module "bilibili-card:*" {
    import { ComponentOptions } from "vue";
    const component: ComponentOptions;
    export default component;
}

declare module "*/blazor.webassembly.js" {
    import { CompileResult, Diagnostic, ICompletionItem, InfoTipItem, AstNodeItem } from "sharp-script";
    global {
        const Blazor: {
            runtime: {
                config: { [key: string]: any }
            };
            start(options?: { [key: string]: any }): Promise<void>;
        };
        const DotNet: {
            invokeMethodAsync(assemblyName: "SharpScript", methodIdentifier: "InitAsync", baseUrl: string, fingerprinting: { [key: string]: string }): Promise<void>;
            invokeMethodAsync(assemblyName: "SharpScript", methodIdentifier: "ProcessAsync", code: string): Promise<CompileResult>;
            invokeMethodAsync(assemblyName: "SharpScript", methodIdentifier: "GetAssemblyAsync", code: string): Promise<{ arrayBuffer(): Promise<ArrayBuffer> }>;
            invokeMethodAsync(assemblyName: "SharpScript", methodIdentifier: "GetDiagnosticsAsync", code: string): Promise<Diagnostic[]>;
            invokeMethodAsync(assemblyName: "SharpScript", methodIdentifier: "GetCompletionsAsync", code: string, position: number): Promise<ICompletionItem[]>;
            invokeMethodAsync(assemblyName: "SharpScript", methodIdentifier: "GetInfoTipAsync", code: string, position: number): Promise<InfoTipItem>;
            invokeMethodAsync(assemblyName: "SharpScript", methodIdentifier: "GetAstAsync", code: string): Promise<AstNodeItem>;
            invokeMethodAsync(assemblyName: "SharpScript", methodIdentifier: "GetCSharpInfoTipLiteAsync", code: string, position: number): Promise<InfoTipItem>;
            invokeMethodAsync(assemblyName: "SharpScript", methodIdentifier: "GetLanguageTypes"): Promise<string[]>;
            invokeMethodAsync(assemblyName: "SharpScript", methodIdentifier: "SetLanguageType", type: string): Promise<void>;
            invokeMethodAsync(assemblyName: "SharpScript", methodIdentifier: "GetSourceCodeKind"): Promise<string>;
            invokeMethodAsync(assemblyName: "SharpScript", methodIdentifier: "SetSourceCodeKind", kind: string): Promise<void>;
            invokeMethodAsync(assemblyName: "SharpScript", methodIdentifier: "GetOutputTypes"): Promise<string[]>;
            invokeMethodAsync(assemblyName: "SharpScript", methodIdentifier: "SetOutputType", type: string): Promise<void>;
            invokeMethodAsync(assemblyName: "SharpScript", methodIdentifier: "GetInputLanguageVersions"): Promise<string[]>;
            invokeMethodAsync(assemblyName: "SharpScript", methodIdentifier: "GetInputLanguageVersion"): Promise<string>;
            invokeMethodAsync(assemblyName: "SharpScript", methodIdentifier: "SetInputLanguageVersion", version: string): Promise<void>;
            invokeMethodAsync(assemblyName: "SharpScript", methodIdentifier: "GetOutputLanguageVersions"): Promise<string[]>;
            invokeMethodAsync(assemblyName: "SharpScript", methodIdentifier: "GetOutputLanguageVersion"): Promise<string>;
            invokeMethodAsync(assemblyName: "SharpScript", methodIdentifier: "SetOutputLanguageVersion", version: string): Promise<void>;
            /**
             * Invokes the specified .NET public method asynchronously.
             *
             * @param assemblyName The short name (without key/version or .dll extension) of the .NET assembly containing the method.
             * @param methodIdentifier The identifier of the method to invoke. The method must have a [JSInvokable] attribute specifying this identifier.
             * @param args Arguments to pass to the method, each of which must be JSON-serializable.
             * @returns A promise representing the result of the operation.
             */
            invokeMethodAsync<T>(assemblyName: string, methodIdentifier: string, ...args: any[]): Promise<T>;
        }
    }
}