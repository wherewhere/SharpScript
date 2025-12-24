<template>
    <div class="content">
        <SplitPanels class="split-view" :direction="direction">
            <template #panel1>
                <div style="display: flex; justify-content: space-between; column-gap: 4px">
                    <div style="display: flex; column-gap: 4px;">
                        <fluent-select :title="t('input.language.title')" :placeholder="t('input.language.placeholder')"
                                       position="below" v-model="language" style="min-width: auto;">
                            <fluent-option title="CSharp" value="CSharp">C#</fluent-option>
                            <fluent-option title="VisualBasic" value="VisualBasic">VB</fluent-option>
                            <fluent-option title="IL" value="IL">IL</fluent-option>
                        </fluent-select>
                        <ToggleButton v-model="isScript">{{ t("input.language.script") }}</ToggleButton>
                    </div>
                    <div style="display: flex; column-gap: 4px;">
                        <fluent-button :title="loading ? message : t('input.process.title')" @click="processAsync"
                                       :disabled="loading || isSyntaxTree">
                            <fluent-progress-ring v-if="loading"
                                                  style="width: 12px; height: 12px;"></fluent-progress-ring>
                            <TriangleRight12Filled v-else style="fill: currentColor;" />
                        </fluent-button>
                        <fluent-select v-if="inputLanguages.length" v-model="inputLanguage" style="min-width: 105px;"
                                       :title="t('input.version.title')" position="below"
                                       :placeholder="t('input.version.placeholder')">
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
                    <fluent-select :title="t('output.language.title')" :placeholder="t('output.language.placeholder')"
                                   position="below" v-model="output" style="min-width: auto;">
                        <fluent-option title="CSharp" value="CSharp">C#</fluent-option>
                        <fluent-option title="IL" value="IL">IL</fluent-option>
                        <fluent-option title="Run" value="Run">{{ t("output.language.run") }}</fluent-option>
                        <fluent-option title="SyntaxTree" value="SyntaxTree" :disabled="language === 'IL'">
                            {{ t("output.language.syntaxTree") }}
                        </fluent-option>
                    </fluent-select>
                    <div style="display: flex; column-gap: 4px;">
                        <fluent-button v-if="isInitLinter && !diagnostics.errors.length"
                                       :title="t('output.download.title')" @click="downloadAssemblyAsync" :disabled="loading">
                            <ArrowDownload16Regular style="fill: currentColor;" />
                        </fluent-button>
                        <fluent-button v-if="!isInitLinter" :title="t('output.linter.title')" @click="initLinterAsync"
                                       :disabled="loading">
                            <Sparkle16Regular style="fill: currentColor;" />
                        </fluent-button>
                        <fluent-select v-if="outputLanguages.length" v-model="outputLanguage" style="min-width: 92px;"
                                       position="below" :title="t('output.version.title')"
                                       :placeholder="t('output.version.placeholder')">
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
                                        <th>{{ t("output.diagnostic.message") }}</th>
                                        <th>{{ t("output.diagnostic.location") }}</th>
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
                <span :title="t('status.errors', [diagnostics.errors.length])">
                    <DismissCircle16Regular style="fill: currentColor; margin: 3px 0 -3px 0;" />
                    <span style="margin: 0 0 0 4px;">{{ diagnostics.errors.length }}</span>
                </span>
                <span :title="t('status.warnings', [diagnostics.warnings.length])" style="margin: 0 0 0 4px;">
                    <Warning16Regular style="fill: currentColor; margin: 3px 0 -3px 0;" />
                    <span style="margin: 0 0 0 4px;">{{ diagnostics.warnings.length }}</span>
                </span>
            </div>
        </div>
    </div>
    <div class="loading-progress" v-if="!isInitDotnet"></div>
</template>

<script lang="ts" setup>
    import "./types";
    import type { AstNodeItem, LinePosition } from "sharp-script";
    import type { setProperty, DotNetWorker, DiagnosticWrapper } from "./worker";
    import { computed, nextTick, onMounted, ref, shallowRef, useTemplateRef, watch, watchPostEffect } from "vue";
    import { useI18n } from "vue-i18n";
    import { useSeoMeta } from "@unhead/vue";
    import { compressToEncodedURIComponent, decompressFromEncodedURIComponent } from "lz-string";
    import { AsyncLock, Comlink } from "./helpers/shared";
    import { AnsiUp } from "ansi_up";
    import type { Extension, Text } from "@codemirror/state";
    import { autocompletion, ifNotIn, Completion, CompletionContext } from "@codemirror/autocomplete";
    import { linter, lintGutter } from "@codemirror/lint";
    import { hoverTooltip } from "@codemirror/view";
    import { mapTextTagsToType, renderParts } from "./helpers/render-parts";
    import { getAssemblyAsync } from "./helpers/autocompletion";
    import { createTooltip } from "./helpers/tooltips.js";
    import { setTimeoutAsync } from "./helpers/utils.js";
    import { keywords } from "./package.json";
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

    const { locale, t } = useI18n();
    watchPostEffect(() => document.documentElement.lang = locale.value);

    const title = "SharpScript";
    const description = computed(() => t("description"));
    const author = "wherewhere";
    useSeoMeta({
        // Basic SEO
        title,
        description,
        author: author,
        keywords: keywords.join(", "),

        // Open Graph
        ogTitle: title,
        ogDescription: description,
        ogType: "website",
        ogLocale: () => locale.value.replace('-', '_'),
        ogSiteName: title,

        // Twitter
        twitterCard: "summary",
        twitterSite: "@wherewhere7",

        // Product specific (structured data will be generated)
        articleAuthor: [author],
        articleTag: keywords
    });

    const code = shallowRef('using System;\nConsole.WriteLine("Hello, World!");');
    const language = shallowRef("CSharp");
    const inputLanguages = ref(["Default", "CSharp1", "CSharp2", "CSharp3", "CSharp4", "CSharp5", "CSharp6", "CSharp7", "CSharp7_1", "CSharp7_2", "CSharp7_3", "CSharp8", "CSharp9", "CSharp10", "CSharp11", "CSharp12", "CSharp13", "CSharp14", "LatestMajor", "Preview", "Latest"]);
    const inputLanguage = shallowRef("Preview");
    const isScript = shallowRef(false);
    const output = shallowRef("Run");
    const outputLanguages = ref<string[]>([]);
    const outputLanguage = shallowRef("CSharp1");
    const isInitDotnet = shallowRef(false);
    const isInitLinter = shallowRef(false);
    const loading = shallowRef(false);
    const message = shallowRef('');
    const results = ref({
        decompiled: null as string | null,
        outputs: [] as string[]
    });
    const diagnostics = ref({
        errors: [] as DiagnosticWrapper[],
        warnings: [] as DiagnosticWrapper[],
        infos: [] as DiagnosticWrapper[]
    });
    const syntaxTree = shallowRef<AstNodeItem>();
    const locker = new AsyncLock();
    const roslynTooltip = ref({
        input: null as (() => Extension) | null,
        output: null as (() => Extension) | null
    });
    const direction = shallowRef<"row" | "column">("row");
    const isRun = computed(() => output.value === "Run");
    const isSyntaxTree = computed(() => output.value === "SyntaxTree");
    const isDecompile = computed(() => !isRun.value && !isSyntaxTree.value);

    let dotnet: DotNetWorker | null = null;
    watch(
        language,
        async (newValue, oldValue) => {
            if (newValue !== oldValue) {
                try {
                    loading.value = true;
                    const mes = message.value;
                    message.value = t("message.changingLanguage");
                    if (code.value === getDefaultCode(oldValue)) {
                        code.value = getDefaultCode(newValue);
                    }
                    await initDotNetAsync();
                    await dotnet!.setLanguageTypeAsync(newValue);
                    inputLanguages.value = await dotnet!.getInputLanguageVersionsAsync();
                    inputLanguage.value = await dotnet!.getInputLanguageVersionAsync();
                    message.value = mes;
                }
                catch (e) {
                    message.value = t("message.error", `${e}`);
                    console.error(e);
                }
                finally {
                    message.value = '';
                    loading.value = false;
                }
            }
        }
    )
    watch(
        inputLanguage,
        async (newValue, oldValue) => {
            if (newValue !== oldValue) {
                try {
                    loading.value = true;
                    const mes = message.value;
                    message.value = t("message.changingVersion");
                    await initDotNetAsync();
                    await dotnet!.setInputLanguageVersionAsync(newValue);
                    message.value = mes;
                }
                catch (e) {
                    message.value = t("message.error", `${e}`);
                    console.error(e);
                }
                finally {
                    message.value = '';
                    loading.value = false;
                }
            }
        }
    );
    watch(
        isScript,
        async (newValue, oldValue) => {
            if (newValue !== oldValue) {
                try {
                    loading.value = true;
                    const mes = message.value;
                    message.value = t("message.changingOutput");
                    await initDotNetAsync();
                    await dotnet!.setSourceCodeKind(newValue ? "Script" : "Regular");
                    message.value = mes;
                }
                catch (e) {
                    message.value = t("message.error", `${e}`);
                    console.error(e);
                }
                finally {
                    message.value = '';
                    loading.value = false;
                }
            }
        }
    );
    watch(
        output,
        async (newValue, oldValue) => {
            if (newValue !== oldValue) {
                try {
                    loading.value = true;
                    const mes = message.value;
                    message.value = t("message.changingOutput");
                    await initDotNetAsync();
                    if (newValue === "SyntaxTree") {
                        outputLanguages.value = [];
                        await initLinterAsync();
                    }
                    else {
                        await dotnet!.setOutputTypeAsync(newValue);
                        outputLanguages.value = await dotnet!.getOutputLanguageVersionsAsync();
                        outputLanguage.value = await dotnet!.getOutputLanguageVersionAsync();
                    }
                    message.value = mes;
                }
                catch (e) {
                    message.value = t("message.error", `${e}`);
                    console.error(e);
                }
                finally {
                    message.value = '';
                    loading.value = false;
                }
            }
        }
    );
    watch(
        outputLanguage,
        async (newValue, oldValue) => {
            if (newValue !== oldValue) {
                try {
                    loading.value = true;
                    const mes = message.value;
                    message.value = t("message.changingVersion");
                    await initDotNetAsync();
                    await dotnet!.setOutputLanguageVersionAsync(newValue);
                    message.value = mes;
                }
                catch (e) {
                    message.value = t("message.error", `${e}`);
                    console.error(e);
                }
                finally {
                    message.value = '';
                    loading.value = false;
                }
            }
        }
    );

    async function processAsync() {
        try {
            loading.value = true;
            const mes = message.value;
            message.value = t("message.compiling");
            setSettings();
            await (language.value === "IL" ? initDotNetAsync() : initCompilerAsync());
            initEditer();
            await nextTick();
            const result = await dotnet!.processAsync(code.value);
            results.value = result;
            diagnostics.value = {
                errors: [],
                warnings: [],
                infos: []
            };
            result.diagnostics.forEach(diagnostic => {
                switch (diagnostic.severity) {
                    case "Error":
                        diagnostics.value.errors.push(diagnostic);
                        break;
                    case "Warning":
                        diagnostics.value.warnings.push(diagnostic);
                        break;
                    case "Info":
                    case "Hidden":
                    default:
                        if (!diagnostic.tags.some(x => x.startsWith("EnforceOnBuild"))) {
                            diagnostics.value.infos.push(diagnostic);
                        }
                        break;
                }
            });
            message.value = mes;
        }
        catch (e) {
            message.value = t("message.error", `${e}`);
            diagnostics.value.errors.push({
                id: '',
                location: {
                    start: { line: 0, character: 0 },
                    end: { line: 0, character: 0 }
                },
                message: `${e}`,
                severity: "Error",
                actions: [],
                tags: []
            });
            console.error(e);
        }
        finally {
            message.value = '';
            loading.value = false;
        }
    }

    async function getDiagnosticsAsync(code: string) {
        try {
            return await dotnet!.getDiagnosticsAsync(code);
        }
        catch (e) {
            console.warn(e);
        }
    }

    async function getCompletionsAsync(code: string, position: number) {
        try {
            return await dotnet!.getCompletionsAsync(code, position);
        }
        catch (e) {
            console.warn(e);
        }
    }

    async function getInfoTipAsync(code: string, position: number) {
        try {
            return await dotnet!.getInfoTipAsync(code, position);
        }
        catch (e) {
            console.warn(e);
        }
    }

    async function getAstAsync(code: string) {
        try {
            return await dotnet!.getAstAsync(code);
        }
        catch (e) {
            console.warn(e);
        }
    }

    async function getCSharpInfoTipLiteAsync(code: string, position: number) {
        try {
            return await dotnet!.getCSharpInfoTipLiteAsync(code, position);
        }
        catch (e) {
            console.warn(e);
        }
    }

    async function diagnosticInvokeAsync(index: number) {
        try {
            return await dotnet!.diagnosticInvokeAsync(index);
        }
        catch (e) {
            console.warn(e);
        }
    }

    async function completionGetDescriptionAsync(index: number) {
        try {
            return await dotnet!.completionGetDescriptionAsync(index);
        }
        catch (e) {
            console.warn(e);
        }
    }

    async function completionGetChangeAsync(index: number) {
        try {
            return await dotnet!.completionGetChangeAsync(index);
        }
        catch (e) {
            console.warn(e);
        }
    }

    async function downloadAssemblyAsync() {
        try {
            loading.value = true;
            const mes = message.value;
            message.value = t("message.compiling");
            const href = await dotnet!.getAssemblyLinkAsync(code.value);
            const link = document.createElement('a');
            link.href = href;
            link.download = "SharpScript.zip";
            link.click();
            message.value = mes;
        }
        catch (e) {
            message.value = t("message.error", `${e}`);
            console.error(e);
        }
        finally {
            message.value = '';
            loading.value = false;
        }
    }

    async function initLinterAsync() {
        try {
            loading.value = true;
            const mes = message.value;
            message.value = t("message.initLinter");
            await (language.value === "IL" ? initDotNetAsync() : initCompilerAsync());
            initEditer();
            message.value = mes;
        }
        catch (e) {
            console.warn(e);
        }
        finally {
            loading.value = false;
        }
    }

    let noWorker = false;
    async function initDotNetAsync() {
        if (!isInitDotnet.value) {
            const mes = message.value;
            message.value = t("message.loadingDotnet");
            await locker.acquire("initDotNet", async () => {
                if (!isInitDotnet.value) {
                    const mes = message.value;
                    message.value = t("message.initWebWorker");
                    if (noWorker) {
                        dotnet!.init();
                    }
                    else {
                        await dotnet!.init(document.baseURI, Comlink.proxy<setProperty>((x, y) => document.documentElement.style.setProperty(x, y)));
                    }
                    message.value = mes;
                    await dotnet!.startAsync();
                    isInitDotnet.value = true;
                }
            });
            message.value = mes;
        }
    }

    let isInitCompiler = false;
    async function initCompilerAsync() {
        if (!isInitCompiler) {
            await initDotNetAsync();
            const mes = message.value;
            message.value = t("message.downloadReferences");
            if (noWorker) {
                await setTimeoutAsync(1);
            }
            await dotnet!.initAsync();
            message.value = mes;
            isInitCompiler = true;
        }
    }

    const editor = useTemplateRef("editor");
    function initEditer() {
        if (!isInitLinter.value) {
            const editorHost = editor.value!;
            const editorView = editorHost.editor!;
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
            editorView.dispatch({
                effects: editorHost.linterSet.reconfigure(linter(async view => {
                    if (isSyntaxTree) {
                        getAstAsync(view.state.doc.toString()).then(x => syntaxTree.value = x!);
                    }
                    let diags = await getDiagnosticsAsync(view.state.doc.toString());
                    if (diags instanceof Array) {
                        diagnostics.value = {
                            errors: [],
                            warnings: [],
                            infos: []
                        };
                        if (language.value === "IL") {
                            diags = diags.filter(x => x.severity !== "Info" || x.message !== "Operation completed successfully");
                        }
                        return diags.map(diagnostic => {
                            return {
                                from: getIndex(view.state.doc, diagnostic.location.start),
                                to: getIndex(view.state.doc, diagnostic.location.end),
                                severity: (() => {
                                    switch (diagnostic.severity) {
                                        case "Error":
                                            diagnostics.value.errors.push(diagnostic);
                                            return "error";
                                        case "Warning":
                                            diagnostics.value.warnings.push(diagnostic);
                                            return "warning";
                                        case "Info":
                                        case "Hidden":
                                        default:
                                            if (diagnostic.tags.some(x => x.startsWith("EnforceOnBuild"))) {
                                                return "hint";
                                            }
                                            else {
                                                diagnostics.value.infos.push(diagnostic);
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
                                            const results = await diagnosticInvokeAsync(x.action);
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
            editorView.dispatch({
                effects: editorHost.lintGutterSet.reconfigure(lintGutter())
            });
            async function customCompletionAsync(context: CompletionContext) {
                if (language.value !== "IL") {
                    const pos = context.pos;
                    const line = context.state.doc.lineAt(pos);
                    const text = line.text;
                    if (text.startsWith("#r ") || text.startsWith("#R ")) {
                        const path = text.substring(3).trim();
                        const from = line.from + 3;
                        const results: Completion[] = [];
                        for (const assembly of await getAssemblyAsync(dotnet!.fingerprinting)) {
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
            editorView.dispatch({
                effects: editorHost.autocompletionSet.reconfigure(autocompletion({
                    override: [ifNotIn([';', '{', '}'], async context => {
                        const from = context.pos;
                        const completions = await getCompletionsAsync(context.state.doc.toString(), from);
                        const matchContext = context.matchBefore(/[\w\d]+/) ?? { from };
                        return {
                            from: matchContext.from ?? from,
                            options: [...completions!.map(item => {
                                return {
                                    label: item.displayText,
                                    detail: item.inlineDescription,
                                    type: mapTextTagsToType(item.tags),
                                    async info() {
                                        const results = await completionGetDescriptionAsync(item.self);
                                        return renderParts(results!, true);
                                    },
                                    async apply(view, completion, from, to) {
                                        const results = await completionGetChangeAsync(item.self);
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

            roslynTooltip.value.input = () => hoverTooltip(async (view, pos) => {
                const tooltip = await getInfoTipAsync(view.state.doc.toString(), pos);
                return createTooltip(tooltip!, pos);
            });
            roslynTooltip.value.output = () => hoverTooltip(async (view, pos) => {
                const tooltip = await getCSharpInfoTipLiteAsync(view.state.doc.toString(), pos);
                return createTooltip(tooltip!, pos);
            });
            isInitLinter.value = true;
        }
    }

    function getLauguage() {
        switch (language.value) {
            case "IL":
                return "il";
            case "CSharp":
                return "csharp";
            case "VisualBasic":
                return "vb";
            default:
                return "plaintext";
        }
    }

    function getOutputLanguage() {
        switch (output.value) {
            case "IL":
                return "il";
            case "CSharp":
                return "csharp";
            case "VisualBasic":
                return "vb";
            default:
                return "plaintext";
        }
    }

    function getVersion(version: string) {
        return version.replace("VisualBasic", "VB ").replace("CSharp", "C# ").replace('_', '.');
    }

    function getDefaultCode(language: string) {
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
    }

    function getLocation(item: DiagnosticWrapper) {
        return `[${item.location.start.line + 1}, ${item.location.start.character}] - [${item.location.end.line + 1}, ${item.location.end.character}]`;
    }

    let hashChanged = false;
    function loadSettings() {
        if (hashChanged) {
            hashChanged = false;
            return;
        }
        const hash = location.hash.substring(1);
        if (hash) {
            const params = new URLSearchParams(hash);
            if (params.has("language")) {
                language.value = params.get("language")!;
            }
            if (language.value !== "IL") {
                if (params.has("version")) {
                    inputLanguage.value = params.get("version")!;
                }
            }
            if (params.has("output")) {
                output.value = params.get("output")!;
            }
            if (output.value === "CSharp") {
                if (params.has("csversion")) {
                    outputLanguage.value = params.get("csversion")!;
                }
            }
            if (params.has("script")) {
                isScript.value = params.get("script") !== "false";
            }
            if (params.has("code")) {
                code.value = decompressFromEncodedURIComponent(params.get("code")!);
            }
            if (params.has("noworker")) {
                return params.get("noworker") !== "false";
            }
        }
    }

    function setSettings() {
        const settings: { [key: string]: string } = {};
        if (noWorker) {
            settings.noworker = "true";
        }
        if (language.value !== "CSharp") {
            settings.language = language.value;
        }
        if (output.value !== "Run") {
            settings.output = output.value;
        }
        if (isScript.value) {
            settings.script = "true";
        }
        if (settings.language !== "IL") {
            if (settings.language === "VisualBasic") {
                if (inputLanguage.value !== "Latest") {
                    settings.version = inputLanguage.value;
                }
            }
            else {
                if (inputLanguage.value !== "Preview") {
                    settings.version = inputLanguage.value;
                }
            }
        }
        if (settings.output === "CSharp") {
            if (outputLanguage.value !== "CSharp1") {
                settings.csversion = outputLanguage.value;
            }
        }
        if (code.value) {
            settings.code = compressToEncodedURIComponent(code.value);
        }
        location.hash = new URLSearchParams(settings).toString();
        hashChanged = true;
    }

    function renderConsole(output: string) {
        const ansi_up = new AnsiUp();
        const html = ansi_up.ansi_to_html(output);
        return html;
    }

    onMounted(async () => {
        if (loadSettings()) {
            dotnet = await import("./worker").then(x => x.dotnet);
            noWorker = true;
        }
        else {
            const url = new URL(/* @vite-ignore */ "./worker.js", import.meta.url);
            dotnet = Comlink.wrap<DotNetWorker>(new Worker(url.href, { type: "module" }));
        }
        addEventListener("hashchange", loadSettings);
        const scheme = matchMedia("(max-width: 767px)");
        if (scheme) {
            scheme.addEventListener("change", e => direction.value = e.matches ? "column" : "row");
            direction.value = scheme.matches ? "column" : "row";
        }
    });
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

    .no-selected-indicator :deep(fluent-tree-item[selected])::after {
        display: none;
    }

    :deep(fluent-select)::part(listbox) {
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
                box-sizing: border-box;
                flex-direction: column;
                background: var(--neutral-fill-input-rest);
                border: calc(var(--stroke-width) * 1px) solid var(--neutral-stroke-layer-rest);
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
                    flex-direction: column;
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