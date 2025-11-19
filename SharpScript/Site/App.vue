<template>
    <MetaSetter :lang="$i18n.locale" :description="$t('description')" />
    <div class="content">
        <SplitPanels class="split-view" :direction="direction">
            <template #panel1>
                <div style="display: flex; justify-content: space-between; column-gap: 4px">
                    <div style="display: flex; column-gap: 4px;">
                        <fluent-select :title="$t('input.language.title')"
                                       :placeholder="$t('input.language.placeholder')" v-model="language" style="min-width: auto;">
                            <fluent-option title="CSharp" value="CSharp">C#</fluent-option>
                            <fluent-option title="VisualBasic" value="VisualBasic">VB</fluent-option>
                            <fluent-option title="IL" value="IL">IL</fluent-option>
                        </fluent-select>
                        <ToggleButton v-model="isScript">{{ $t("input.language.script") }}</ToggleButton>
                    </div>
                    <div style="display: flex; column-gap: 4px;">
                        <fluent-button :title="loading ? message : $t('input.process.title')" @click="processAsync"
                                       :disabled="loading || isSyntaxTree">
                            <fluent-progress-ring v-if="loading"
                                                  style="width: 12px; height: 12px;"></fluent-progress-ring>
                            <TriangleRight12Filled v-else style="fill: currentColor;" />
                        </fluent-button>
                        <fluent-select v-if="inputLanguages.length" v-model="inputLanguage" style="min-width: 105px;"
                                       :title="$t('input.version.title')" :placeholder="$t('input.version.placeholder')">
                            <fluent-option v-for="item in inputLanguages" :title="item" :value="item">
                                {{ getVersion(item) }}
                            </fluent-option>
                        </fluent-select>
                    </div>
                </div>
                <CodeMirror class="editor" v-model:value="code" :language="getLauguage()"
                            :roslyn-tooltip="roslynTooltip.input!" ref="editor" />
            </template>
            <template #panel2>
                <div style="display: flex; justify-content: space-between; column-gap: 4px">
                    <fluent-select :title="$t('output.language.title')" :placeholder="$t('output.language.placeholder')"
                                   v-model="output" style="min-width: auto;">
                        <fluent-option title="CSharp" value="CSharp">C#</fluent-option>
                        <fluent-option title="IL" value="IL">IL</fluent-option>
                        <fluent-option title="Run" value="Run">{{ $t("output.language.run") }}</fluent-option>
                        <fluent-option title="SyntaxTree" value="SyntaxTree" :disabled="language === 'IL'">
                            {{ $t("output.language.syntaxTree") }}
                        </fluent-option>
                    </fluent-select>
                    <div style="display: flex; column-gap: 4px;">
                        <fluent-button v-if="isInitLinter && !diagnostics.errors.length"
                                       :title="$t('output.download.title')" @click="downloadAssemblyAsync" :disabled="loading">
                            <ArrowDownload16Regular style="fill: currentColor;" />
                        </fluent-button>
                        <fluent-button v-if="!isInitLinter" :title="$t('output.linter.title')" @click="initLinterAsync"
                                       :disabled="loading">
                            <Sparkle16Regular style="fill: currentColor;" />
                        </fluent-button>
                        <fluent-select v-if="outputLanguages.length" v-model="outputLanguage" style="min-width: 92px;"
                                       :title="$t('output.version.title')" :placeholder="$t('output.version.placeholder')">
                            <fluent-option v-for="item in outputLanguages" :title="item" :value="item">
                                {{ getVersion(item) }}
                            </fluent-option>
                        </fluent-select>
                    </div>
                </div>
                <div class="editor">
                    <CodeMirror v-if="isDecompile && results.decompiled && !diagnostics.errors.length"
                                v-model:value="results.decompiled" :language="getOutputLanguage()"
                                :roslyn-tooltip="roslynTooltip.output!" :readonly="true" style="flex: 1;" />
                    <div class="output" v-else>
                        <fluent-tree-view class="no-selected-indicator" v-if="isSyntaxTree && syntaxTree"
                                          style="flex: 1; margin: 12px 0;">
                            <SyntaxTreeItem :item="syntaxTree" />
                        </fluent-tree-view>
                        <div v-else-if="isRun && results.outputs.length && !diagnostics.errors.length">
                            <pre class="unset" v-for="item in results.outputs" v-html="renderConsole(item)"></pre>
                        </div>
                        <div v-else-if="diagnostics.errors.length || diagnostics.warnings.length || diagnostics.infos.length">
                            <table cellpadding="4">
                                <thead>
                                    <tr>
                                        <th style="width: 20px;"></th>
                                        <th>ID</th>
                                        <th>{{ $t("output.diagnostic.message") }}</th>
                                        <th>{{ $t("output.diagnostic.location") }}</th>
                                    </tr>
                                </thead>
                                <tbody>
                                    <tr v-for="item in [...diagnostics.errors, ...diagnostics.warnings, ...diagnostics.infos]">
                                        <td :class="item.severity.toLowerCase() + '-icon'"></td>
                                        <td>{{ item.id }}</td>
                                        <td>{{ item.message }}</td>
                                        <td>{{ getLocation(item) }}</td>
                                    </tr>
                                </tbody>
                            </table>
                        </div>
                    </div>
                </div>
            </template>
        </SplitPanels>
        <div class="status-bar">
            <div style="height: 20px;">
                <Alert16Regular style="fill: currentColor; margin: 3px 4px -3px 0;" title="{{ $t('status.alert') }}" />
                <span>{{ message }}</span>
            </div>
            <div style="height: 20px;">
                <span :title="$t('status.errors', [diagnostics.errors.length])">
                    <DismissCircle16Regular style="fill: currentColor; margin: 3px 0 -3px 0;" />
                    <span style="margin: 0 0 0 4px;">{{ diagnostics.errors.length }}</span>
                </span>
                <span :title="$t('status.warnings', [diagnostics.warnings.length])" style="margin: 0 0 0 4px;">
                    <Warning16Regular style="fill: currentColor; margin: 3px 0 -3px 0;" />
                    <span style="margin: 0 0 0 4px;">{{ diagnostics.warnings.length }}</span>
                </span>
            </div>
        </div>
    </div>
    <div class="loading-progress" v-if="!isInitDotnet"></div>
</template>

<script lang="ts">
    import type { } from "./types.js";
    import type { AstNodeItem, Diagnostic, InfoTipItem, LinePosition, TaggedText } from "sharp-script";
    import type { DotNetWorker, DiagnosticWrapper } from "./worker";
    import LZString from "lz-string";
    import { AsyncLock, Comlink } from "./helpers/shared";
    import { AnsiUp } from "ansi_up";
    import type { EditorView } from "codemirror";
    import type { Extension, Text } from "@codemirror/state";
    import { autocompletion, Completion, CompletionContext, ifNotIn } from "@codemirror/autocomplete";
    import { linter, lintGutter } from "@codemirror/lint";
    import { hoverTooltip } from "@codemirror/view";
    import MetaSetter from "./components/MetaSetter.vue";
    import SplitPanels from "./components/SplitPanels.vue";
    import CodeMirror from "./components/CodeMirror.vue";
    import SyntaxTreeItem from "./components/SyntaxTreeItem.vue";
    import ToggleButton from "./components/ToggleButton.vue";
    import TriangleRight12Filled from "@fluentui/svg-icons/icons/triangle_right_12_filled.svg?component";
    import ArrowDownload16Regular from "@fluentui/svg-icons/icons/arrow_download_16_regular.svg?component";
    import Sparkle16Regular from "@fluentui/svg-icons/icons/sparkle_16_regular.svg?component";
    import Alert16Regular from "@fluentui/svg-icons/icons/alert_16_regular.svg?component";
    import DismissCircle16Regular from "@fluentui/svg-icons/icons/dismiss_circle_16_regular.svg?component";
    import Warning16Regular from "@fluentui/svg-icons/icons/warning_16_regular.svg?component";
    import { direction } from "@fluentui/web-components";

    export default {
        name: "App",
        components: {
            CodeMirror,
            MetaSetter,
            SplitPanels,
            SyntaxTreeItem,
            ToggleButton,
            TriangleRight12Filled,
            ArrowDownload16Regular,
            Sparkle16Regular,
            Alert16Regular,
            DismissCircle16Regular,
            Warning16Regular
        },
        data() {
            return {
                code: 'using System;\nConsole.WriteLine("Hello, World!");',
                language: "CSharp",
                inputLanguages: ["Default", "CSharp1", "CSharp2", "CSharp3", "CSharp4", "CSharp5", "CSharp6", "CSharp7", "CSharp7_1", "CSharp7_2", "CSharp7_3", "CSharp8", "CSharp9", "CSharp10", "CSharp11", "CSharp12", "CSharp13", "CSharp14", "LatestMajor", "Preview", "Latest"],
                inputLanguage: "Preview",
                isScript: false,
                output: "Run",
                outputLanguages: [] as string[],
                outputLanguage: "CSharp1",
                isInitDotnet: false,
                isInitCompiler: false,
                isInitLinter: false,
                loading: false,
                message: '',
                results: {
                    diagnostics: [] as Partial<Diagnostic>[],
                    decompiled: null as string | null,
                    outputs: [] as string[]
                },
                diagnostics: {
                    errors: [] as DiagnosticWrapper[],
                    warnings: [] as DiagnosticWrapper[],
                    infos: [] as DiagnosticWrapper[]
                },
                syntaxTree: null as AstNodeItem | null,
                locker: new AsyncLock(),
                assemblies: null as string[] | null,
                dotnet: null as DotNetWorker | null,
                noWorker: false,
                hashChanged: false,
                roslynTooltip: {
                    input: null as (() => Extension) | null,
                    output: null as (() => Extension) | null
                },
                direction: "row" as "row" | "column"
            }
        },
        computed: {
            isRun() {
                return this.output === "Run";
            },
            isSyntaxTree() {
                return this.output === "SyntaxTree";
            },
            isDecompile() {
                return !this.isRun && !this.isSyntaxTree;
            }
        },
        watch: {
            async language(newValue, oldValue) {
                if (newValue !== oldValue) {
                    try {
                        this.loading = true;
                        const message = this.message;
                        this.message = this.$t("message.changingLanguage");
                        if (this.code === this.getDefaultCode(oldValue)) {
                            this.code = this.getDefaultCode(newValue);
                        }
                        await this.initDotNetAsync();
                        await this.dotnet!.setLanguageTypeAsync(newValue);
                        this.inputLanguages = await this.dotnet!.getInputLanguageVersionsAsync();
                        this.inputLanguage = await this.dotnet!.getInputLanguageVersionAsync();
                        this.message = message;
                    }
                    catch (e) {
                        this.message = this.$t("message.error", `${e}`);
                        console.error(e);
                    }
                    finally {
                        this.message = '';
                        this.loading = false;
                    }
                }
            },
            async inputLanguage(newValue, oldValue) {
                if (newValue !== oldValue) {
                    try {
                        this.loading = true;
                        const message = this.message;
                        this.message = this.$t("message.changingVersion");
                        await this.initDotNetAsync();
                        await this.dotnet!.setInputLanguageVersionAsync(newValue);
                        this.message = message;
                    }
                    catch (e) {
                        this.message = this.$t("message.error", `${e}`);
                        console.error(e);
                    }
                    finally {
                        this.message = '';
                        this.loading = false;
                    }
                }
            },
            async isScript(newValue, oldValue) {
                if (newValue !== oldValue) {
                    try {
                        this.loading = true;
                        const message = this.message;
                        this.message = this.$t("message.changingOutput");
                        await this.initDotNetAsync();
                        await this.dotnet!.setSourceCodeKind(newValue ? "Script" : "Regular");
                        this.message = message;
                    }
                    catch (e) {
                        this.message = this.$t("message.error", `${e}`);
                        console.error(e);
                    }
                    finally {
                        this.message = '';
                        this.loading = false;
                    }
                }
            },
            async output(newValue, oldValue) {
                if (newValue !== oldValue) {
                    try {
                        this.loading = true;
                        const message = this.message;
                        this.message = this.$t("message.changingOutput");
                        await this.initDotNetAsync();
                        if (newValue === "SyntaxTree") {
                            this.outputLanguages = [];
                            await this.initLinterAsync();
                        }
                        else {
                            await this.dotnet!.setOutputTypeAsync(newValue);
                            this.outputLanguages = await this.dotnet!.getOutputLanguageVersionsAsync();
                            this.outputLanguage = await this.dotnet!.getOutputLanguageVersionAsync();
                        }
                        this.message = message;
                    }
                    catch (e) {
                        this.message = this.$t("message.error", `${e}`);
                        console.error(e);
                    }
                    finally {
                        this.message = '';
                        this.loading = false;
                    }
                }
            },
            async outputLanguage(newValue, oldValue) {
                if (newValue !== oldValue) {
                    try {
                        this.loading = true;
                        const message = this.message;
                        this.message = this.$t("message.changingVersion");
                        await this.initDotNetAsync();
                        await this.dotnet!.setOutputLanguageVersionAsync(newValue);
                        this.message = message;
                    }
                    catch (e) {
                        this.message = this.$t("message.error", `${e}`);
                        console.error(e);
                    }
                    finally {
                        this.message = '';
                        this.loading = false;
                    }
                }
            }
        },
        methods: {
            async processAsync() {
                try {
                    this.loading = true;
                    const message = this.message;
                    this.message = this.$t("message.compiling");
                    this.setSettings();
                    await (this.language === "IL" ? this.initDotNetAsync() : this.initCompilerAsync());
                    this.initEditer();
                    await this.$nextTick();
                    this.results = await this.dotnet!.processAsync(this.code);
                    this.message = message;
                }
                catch (e) {
                    this.message = this.$t("message.error", `${e}`);
                    this.results.diagnostics.push({
                        location: {
                            start: { line: 0, character: 0 },
                            end: { line: 0, character: 0 }
                        },
                        message: `${e}`,
                        severity: "Error"
                    });
                    console.error(e);
                }
                finally {
                    this.message = '';
                    this.loading = false;
                }
            },
            async getDiagnosticsAsync(code: string) {
                try {
                    return await this.dotnet!.getDiagnosticsAsync(code);
                }
                catch (e) {
                    console.warn(e);
                }
            },
            async getCompletionsAsync(code: string, position: number) {
                try {
                    return await this.dotnet!.getCompletionsAsync(code, position);
                }
                catch (e) {
                    console.warn(e);
                }
            },
            async getInfoTipAsync(code: string, position: number) {
                try {
                    return await this.dotnet!.getInfoTipAsync(code, position);
                }
                catch (e) {
                    console.warn(e);
                }
            },
            async getAstAsync(code: string) {
                try {
                    return await this.dotnet!.getAstAsync(code);
                }
                catch (e) {
                    console.warn(e);
                }
            },
            async getCSharpInfoTipLiteAsync(code: string, position: number) {
                try {
                    return await this.dotnet!.getCSharpInfoTipLiteAsync(code, position);
                }
                catch (e) {
                    console.warn(e);
                }
            },
            async diagnosticInvokeAsync(index: number) {
                try {
                    return await this.dotnet!.diagnosticInvokeAsync(index);
                }
                catch (e) {
                    console.warn(e);
                }
            },
            async completionGetDescriptionAsync(index: number) {
                try {
                    return await this.dotnet!.completionGetDescriptionAsync(index);
                }
                catch (e) {
                    console.warn(e);
                }
            },
            async completionGetChangeAsync(index: number) {
                try {
                    return await this.dotnet!.completionGetChangeAsync(index);
                }
                catch (e) {
                    console.warn(e);
                }
            },
            async downloadAssemblyAsync() {
                try {
                    this.loading = true;
                    const message = this.message;
                    this.message = this.$t("message.compiling");
                    const href = await this.dotnet!.getAssemblyLinkAsync(this.code);
                    const link = document.createElement('a');
                    link.href = href;
                    link.download = "SharpScript.zip";
                    link.click();
                    this.message = message;
                }
                catch (e) {
                    this.message = this.$t("message.error", `${e}`);
                    console.error(e);
                }
                finally {
                    this.message = '';
                    this.loading = false;
                }
            },
            async initLinterAsync() {
                try {
                    this.loading = true;
                    const message = this.message;
                    this.message = this.$t("message.initLinter");
                    await (this.language === "IL" ? this.initDotNetAsync() : this.initCompilerAsync());
                    this.initEditer();
                    this.message = message;
                }
                catch (e) {
                    console.warn(e);
                }
                finally {
                    this.loading = false;
                }
            },
            async initDotNetAsync() {
                if (!this.isInitDotnet) {
                    const message = this.message;
                    this.message = this.$t("message.loadingDotnet");
                    await this.locker.acquire("initDotNet", async () => {
                        if (!this.isInitDotnet) {
                            const message = this.message;
                            this.message = this.$t("message.initWebWorker");
                            await this.dotnet!.init(document.baseURI, Comlink.proxy((x, y) => document.documentElement.style.setProperty(x, y)));
                            this.message = message;
                            await this.dotnet!.startAsync();
                            this.isInitDotnet = true;
                        }
                    });
                    this.message = message;
                }
            },
            async initCompilerAsync() {
                if (!this.isInitCompiler) {
                    await this.initDotNetAsync();
                    const message = this.message;
                    this.message = this.$t("message.downloadReferences");
                    await this.dotnet!.initAsync();
                    this.message = message;
                    this.isInitCompiler = true;
                }
            },
            initEditer() {
                if (!this.isInitLinter) {
                    const editorHost: any = this.$refs.editor;
                    const editor: EditorView = editorHost!.editor;
                    function getIndex(doc: Text, span: LinePosition) {
                        if (doc.lines <= span.line) {
                            return doc.length;
                        }
                        const index = doc.line(span.line + 1).from + span.character;
                        if (index > doc.length) {
                            return doc.length;
                        }
                        return index;
                    }
                    const that = this;
                    editor.dispatch({
                        effects: editorHost.linterSet.reconfigure(linter(async view => {
                            if (that.isSyntaxTree) {
                                this.getAstAsync(view.state.doc.toString()).then(x => that.syntaxTree = x!);
                            }
                            let diagnostics = await this.getDiagnosticsAsync(view.state.doc.toString());
                            if (diagnostics instanceof Array) {
                                this.diagnostics = {
                                    errors: [],
                                    warnings: [],
                                    infos: []
                                };
                                if (that.language === "IL") {
                                    diagnostics = diagnostics.filter(x => x.severity !== "Info" || x.message !== "Operation completed successfully");
                                }
                                return diagnostics.map(diagnostic => {
                                    return {
                                        from: getIndex(view.state.doc, diagnostic.location.start),
                                        to: getIndex(view.state.doc, diagnostic.location.end),
                                        severity: (() => {
                                            switch (diagnostic.severity) {
                                                case "Error":
                                                    that.diagnostics.errors.push(diagnostic);
                                                    return "error";
                                                case "Warning":
                                                    that.diagnostics.warnings.push(diagnostic);
                                                    return "warning";
                                                case "Info":
                                                case "Hidden":
                                                default:
                                                    if (diagnostic.tags.some(x => x.startsWith("EnforceOnBuild"))) {
                                                        return "hint";
                                                    }
                                                    else {
                                                        that.diagnostics.infos.push(diagnostic);
                                                        return "info";
                                                    }
                                            }
                                        })(),
                                        markClass: diagnostic.tags.includes("Unnecessary") ? "cm-lintRange-unnecessary" : undefined,
                                        message: `${diagnostic.id ? `${diagnostic.id}: ` : ''}${diagnostic.message}`,
                                        actions: diagnostic.actions.map(x => {
                                            return {
                                                name: x.title,
                                                async apply(view) {
                                                    const results = await that.diagnosticInvokeAsync(x.action);
                                                    if (results instanceof Array) {
                                                        view.dispatch({
                                                            changes: results.map(x => {
                                                                const span = x.span;
                                                                return { from: span.start, to: span.end, insert: x.newText }
                                                            })
                                                        });
                                                    }
                                                }
                                            }
                                        })
                                    }
                                });
                            }
                            return [];
                        }))
                    });
                    editor.dispatch({
                        effects: editorHost.lintGutterSet.reconfigure(lintGutter())
                    });
                    function mapTextTagsToType(tags: string[]) {
                        switch (tags.length) {
                            case 0: if (tags.length === 0)
                                console.warn('No tag found for completion, falling back to "keyword".');
                                return "keyword";
                            case 1:
                                return tags[0].toLowerCase();
                            default:
                                return `${tags[0].toLowerCase()}-${tags[1].toLowerCase()}`;
                        }
                    }
                    function renderPartTo(parent: HTMLElement, part: TaggedText) {
                        const span = document.createElement("span");
                        span.className = `tok-${part.tag.toLowerCase()}`;
                        span.textContent = part.text;
                        parent.appendChild(span);
                    }
                    function createSection() {
                        const section = document.createElement("div");
                        section.className = "mirrorsharp-parts-section";
                        return section;
                    }
                    function renderPartsTo(parent: HTMLElement, parts: TaggedText[], splitLinesToSections: boolean) {
                        let section = splitLinesToSections ? createSection() : parent;
                        for (const part of parts) {
                            if (part.tag === "linebreak" && splitLinesToSections) {
                                parent.appendChild(section);
                                section = createSection();
                                continue;
                            }
                            renderPartTo(section, part);
                        }
                        if (splitLinesToSections) {
                            parent.appendChild(section);
                        }
                    }
                    function renderParts(parts: TaggedText[], splitLinesToSections: boolean) {
                        const container = document.createElement("div");
                        renderPartsTo(container, parts, splitLinesToSections);
                        return container;
                    }
                    async function customCompletionAsync(context: CompletionContext) {
                        if (that.language !== "IL") {
                            const pos = context.pos;
                            const line = context.state.doc.lineAt(pos);
                            const text = line.text;
                            if (text.startsWith("#r ") || text.startsWith("#R ")) {
                                async function getAssemblyAsync() {
                                    if (that.assemblies) {
                                        return that.assemblies;
                                    }
                                    else {
                                        const fingerprinting = await that.dotnet!.fingerprinting;
                                        const assemblies = [];
                                        for (const key in fingerprinting) {
                                            const value = fingerprinting[key];
                                            const assembly = value.substring(0, value.lastIndexOf("."));
                                            assemblies.push(assembly);
                                        }
                                        that.assemblies = assemblies;
                                        return assemblies;
                                    }
                                }
                                const path = text.substring(3).trim();
                                const from = line.from + 3;
                                const assemblies = await getAssemblyAsync();
                                const results: Completion[] = [];
                                for (const assembly of assemblies) {
                                    if (assembly.startsWith(path)) {
                                        results.push({
                                            label: assembly,
                                            type: "assembly",
                                            apply(view, completion) {
                                                const label = completion.label;
                                                view.dispatch({
                                                    changes: { from, to: line.to, insert: label },
                                                    selection: { anchor: from + label.length }
                                                });
                                            }
                                        });
                                    }
                                }
                                return results;
                            }
                        }
                        return [];
                    }
                    editor.dispatch({
                        effects: editorHost.autocompletionSet.reconfigure(autocompletion({
                            override: [ifNotIn([';', '{', '}'], async context => {
                                const from = context.pos;
                                const completions = await this.getCompletionsAsync(context.state.doc.toString(), from);
                                const matchContext = context.matchBefore(/[\w\d]+/) ?? { from };
                                return {
                                    from: matchContext.from ?? from,
                                    options: [...completions!.map(item => {
                                        return {
                                            label: item.displayText,
                                            detail: item.inlineDescription,
                                            type: mapTextTagsToType(item.tags),
                                            async info() {
                                                const results = await that.completionGetDescriptionAsync(item.self);
                                                return renderParts(results!, true);
                                            },
                                            async apply(view, completion, from, to) {
                                                const results = await that.completionGetChangeAsync(item.self);
                                                if (results) {
                                                    const textChanges = results.textChanges;
                                                    if (textChanges instanceof Array) {
                                                        const selection = { anchor: results.newPosition ?? to };
                                                        const changes = textChanges.map((x, i) => {
                                                            const span = x.span;
                                                            if (typeof results.newPosition !== "number") {
                                                                if (i == 0) {
                                                                    selection.anchor = from;
                                                                }
                                                                selection.anchor += x.newText?.length ?? 0;
                                                                if (span.start < from) {
                                                                    selection.anchor -= Math.min(from, span.end) - span.start;
                                                                }
                                                            }
                                                            return { from: span.start, to: span.end, insert: x.newText };
                                                        });
                                                        view.dispatch({ changes });
                                                        if (selection.anchor <= view.state.doc.length) {
                                                            view.dispatch({ selection });
                                                        }
                                                    }
                                                }
                                                else {
                                                    const label = completion.label;
                                                    return view.dispatch({
                                                        changes: { from, to, insert: label },
                                                        selection: { anchor: from + label.length }
                                                    });
                                                }
                                            }
                                        } as Completion;
                                    }),
                                    ...await customCompletionAsync(context)],
                                    filter: false
                                };
                            })]
                        }))
                    });
                    function createTooltip(tooltip: InfoTipItem, pos: number) {
                        return {
                            pos,
                            create() {
                                const dom = document.createElement("div")
                                dom.classList.add("mirrorsharp-infotip");
                                tooltip.sections.forEach((section, index) => {
                                    const element = document.createElement("div");
                                    element.className = "mirrorsharp-parts-section";
                                    if (index === 0) {
                                        const icon = document.createElement("span");
                                        icon.classList.add("cm-completionIcon", `cm-completionIcon-${mapTextTagsToType(tooltip.tags)}`);
                                        element.appendChild(icon);
                                    }
                                    renderPartsTo(element, section.parts, false);
                                    dom.appendChild(element);
                                });
                                return { dom };
                            }
                        };
                    }
                    this.roslynTooltip.input = () => hoverTooltip(async (view, pos) => {
                        const tooltip = await this.getInfoTipAsync(view.state.doc.toString(), pos);
                        return createTooltip(tooltip!, pos);
                    });
                    this.roslynTooltip.output = () => hoverTooltip(async (view, pos) => {
                        const tooltip = await this.getCSharpInfoTipLiteAsync(view.state.doc.toString(), pos);
                        return createTooltip(tooltip!, pos);
                    });
                    this.isInitLinter = true;
                }
            },
            getLauguage() {
                switch (this.language) {
                    case "IL":
                        return "il";
                    case "CSharp":
                        return "csharp";
                    case "VisualBasic":
                        return "vb";
                    default:
                        return "plaintext";
                }
            },
            getOutputLanguage() {
                switch (this.output) {
                    case "IL":
                        return "il";
                    case "CSharp":
                        return "csharp";
                    case "VisualBasic":
                        return "vb";
                    default:
                        return "plaintext";
                }
            },
            getVersion(version: string) {
                return version.replace("VisualBasic", "VB ").replace("CSharp", "C# ").replace('_', '.');
            },
            getDefaultCode(language: string) {
                switch (language) {
                    case "CSharp":
                        return 'using System;\nConsole.WriteLine("Hello, World!");';
                    case "VisualBasic":
                        return 'Imports System\nPublic Module Program\n    Public Sub Main()\n        Console.WriteLine("Hello, World!")\n    End Sub\nEnd Module';
                    case "IL":
                        return `.assembly ' ' {\n}\n.assembly extern System.Console {\n}\n.method static void Main() {\n    .entrypoint\n    ldstr "Hello, World!"\n    call void [System.Console]System.Console::WriteLine(string)\n    ret\n}`;
                    default:
                        return '';
                }
            },
            getLocation(item: DiagnosticWrapper) {
                return `[${item.location.start.line + 1}, ${item.location.start.character}] - [${item.location.end.line + 1}, ${item.location.end.character}]`;
            },
            loadSettings() {
                if (this.hashChanged) {
                    this.hashChanged = false;
                    return;
                }
                const hash = location.hash.substring(1);
                if (hash) {
                    const params = new URLSearchParams(hash);
                    if (params.has("language")) {
                        this.language = params.get("language")!;
                    }
                    if (this.language !== "IL") {
                        if (params.has("version")) {
                            this.inputLanguage = params.get("version")!;
                        }
                    }
                    if (params.has("output")) {
                        this.output = params.get("output")!;
                    }
                    if (this.output === "CSharp") {
                        if (params.has("csversion")) {
                            this.outputLanguage = params.get("csversion")!;
                        }
                    }
                    if (params.has("script")) {
                        this.isScript = params.get("script") !== "false";
                    }
                    if (params.has("code")) {
                        this.code = LZString.decompressFromBase64(params.get("code")!);
                    }
                    if (params.has("noworker")) {
                        return params.get("noworker") !== "false";
                    }
                }
            },
            setSettings() {
                const settings: { [key: string]: string } = {};
                if (this.noWorker) {
                    settings.noworker = "true";
                }
                if (this.language !== "CSharp") {
                    settings.language = this.language;
                }
                if (this.output !== "Run") {
                    settings.output = this.output;
                }
                if (this.isScript) {
                    settings.script = "true";
                }
                if (settings.language !== "IL") {
                    if (settings.language === "VisualBasic") {
                        if (this.inputLanguage !== "Latest") {
                            settings.version = this.inputLanguage;
                        }
                    }
                    else {
                        if (this.inputLanguage !== "Preview") {
                            settings.version = this.inputLanguage;
                        }
                    }
                }
                if (settings.output === "CSharp") {
                    if (this.outputLanguage !== "CSharp1") {
                        settings.csversion = this.outputLanguage;
                    }
                }
                if (this.code) {
                    settings.code = LZString.compressToBase64(this.code);
                }
                location.hash = new URLSearchParams(settings).toString();
                this.hashChanged = true;
            },
            renderConsole(output: string) {
                const ansi_up = new AnsiUp();
                const html = ansi_up.ansi_to_html(output);
                return html;
            }
        },
        async mounted() {
            if (this.loadSettings()) {
                this.dotnet = await import("./worker").then(x => x.dotnet);
                this.noWorker = true;
            }
            else {
                const url = new URL(/* @vite-ignore */ "./worker.js", import.meta.url);
                this.dotnet = Comlink.wrap<DotNetWorker>(new Worker(url.href, { type: "module" }));
            }
            addEventListener("hashchange", this.loadSettings);
            const scheme = matchMedia("(max-width: 767px)");
            if (scheme) {
                scheme.addEventListener("change", e => this.direction = e.matches ? "column" : "row");
                this.direction = scheme.matches ? "column" : "row";
            }
        }
    };
</script>

<style lang="scss">
    @use "github:microsoft/fluentui-blazor?branch=dev&path=/src/Core/wwwroot/css/reboot.css";

    :root {
        --font-monospace: "Cascadia Code NF", "Cascadia Code PL", "Cascadia Code", "Cascadia Next SC", "Cascadia Next TC", "Cascadia Next JP", Consolas, "Courier New", "Liberation Mono", SFMono-Regular, Menlo, Monaco, monospace;
        color-scheme: light;

        @media (prefers-color-scheme: dark) {
            color-scheme: dark;
        }
    }

    * {
        transition: background-color 0.083s ease-in-out;
    }

    body,
    .body {
        width: 100%;
        height: 100%;
        overflow: hidden;
        background: var(--neutral-fill-stealth-rest);
    }
</style>

<style lang="scss" scoped>
    :deep(pre.unset) {
        margin-top: 0;
        margin-bottom: 0;
        font-size: inherit;
        font-family: inherit;
        white-space: pre-wrap;
    }

    .loading-progress {
        bottom: 0;
        width: var(--blazor-load-percentage, 0%);
        background: var(--accent-fill-rest);
        height: calc((var(--stroke-width) * 3) * 1px);
        transition: all 0.2s ease-in-out;
        overflow: hidden;
        position: absolute;
    }

    .no-selected-indicator fluent-tree-item[selected]::after {
        display: none;
    }

    :deep(fluent-select)::part(listbox),
    :deep(fluent-select) .listbox {
        max-height: calc(var(--base-height-multiplier) * 30px);
    }

    .content {
        height: 100%;
        padding: 8px 8px 0 8px;
    }

    div.status-bar {
        display: flex;
        height: 30px;
        padding: 4px 4px 6px 4px;
        font-family: var(--font-monospace);
        justify-content: space-between;
    }

    div.split-view {
        height: calc(100% - 30px);
        gap: 8px;

        :deep(.slotted) {
            display: flex;
            flex-direction: column;
            row-gap: 8px;

            &::-webkit-scrollbar {
                display: none;
            }

            .editor {
                flex: 1;
                display: flex;
                overflow: auto;
                background: var(--neutral-fill-input-rest);
                border-radius: calc(var(--layer-corner-radius) * 1px);


                .cm-editor {
                    flex: 1;
                    overflow: inherit;
                }

                &>div {
                    display: flex;
                    overflow: inherit;

                    &:not([class]) {
                        display: flex;
                    }
                }

                &>.output {
                    display: flex;
                    font-family: var(--font-monospace);
                    padding: 0 12px;
                    width: 100%;

                    &>div {
                        padding: 4px 0;
                    }
                }
            }
        }
    }
</style>