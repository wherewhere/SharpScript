<template>
    <div class="content">
        <SplitPanels class="split-view" :direction="direction">
            <template #panel1>
                <div class="toolbar">
                    <div class="toolgroup">
                        <ComboBox name="language" :title="t('input.language.title')"
                                  :placeholder="t('input.language.placeholder')" position="below" v-model="language">
                            <option title="CSharp" value="CSharp">C#</option>
                            <option title="VisualBasic" value="VisualBasic">VB</option>
                            <option title="Mono IL" value="IL">IL</option>
                            <option title="CoreCLR IL" value="CIL">CIL</option>
                        </ComboBox>
                        <ToggleButton v-if="!isIL" v-model="isScript">{{ t("input.language.script") }}</ToggleButton>
                    </div>
                    <div class="toolgroup">
                        <button :title="loading ? message : t('input.process.title')" @click="processAsync"
                                :disabled="loading || isSyntaxTree" class="icon-button">
                            <ProgressRing v-if="loading" style="width: 12px; height: 12px;" />
                            <TriangleRight12Filled v-else style="fill: currentColor;" />
                        </button>
                        <ComboBox name="version" v-if="inputLanguages.length" v-model="inputLanguage"
                                  :title="t('input.version.title')" position="below"
                                  :placeholder="t('input.version.placeholder')">
                            <option v-for="item in inputLanguages" :title="item" :value="item">
                                {{ getVersion(item) }}
                            </option>
                        </ComboBox>
                    </div>
                </div>
                <CodeMirror class="editor" v-model:value="code" :language="getLauguage()" :readonly="loading"
                            :lint-gutter="lintGutter" :linter="linter" :keymap="keymapProp" ref="editor"
                            :roslyn-tooltip="roslynTooltip.input" :roslyn-completion="roslynCompletion" @change="onChange" />
            </template>
            <template #panel2>
                <div class="toolbar">
                    <div class="toolgroup">
                        <ComboBox name="output" :title="t('output.language.title')"
                                  :placeholder="t('output.language.placeholder')" position="below" v-model="output">
                            <option title="CSharp" value="CSharp">C#</option>
                            <option title="IL" value="IL">IL</option>
                            <option title="Run" value="Run">{{ t("output.language.run") }}</option>
                            <option title="SyntaxTree" value="SyntaxTree" :disabled="isIL">
                                {{ t("output.language.syntaxTree") }}
                            </option>
                        </ComboBox>
                        <button class="icon-button" v-if="isInitLinter || isIL" :title="t('output.format.title')"
                                @click="formatEditorAsync" :disabled="loading">
                            <CodeText16Regular style="fill: currentColor;" />
                        </button>
                    </div>
                    <div class="toolgroup">
                        <button class="icon-button" v-if="isInitLinter && !diagnostics.errors.length"
                                :title="t('output.download.title')" @click="downloadAssemblyAsync" :disabled="loading">
                            <ArrowDownload16Regular style="fill: currentColor;" />
                        </button>
                        <button class="icon-button" v-if="!isInitLinter" :title="t('output.linter.title')"
                                @click="initLinterAsync" :disabled="loading">
                            <Sparkle16Regular style="fill: currentColor;" />
                        </button>
                        <ComboBox name="version" v-if="outputLanguages.length" v-model="outputLanguage" position="below"
                                  :title="t('output.version.title')" :placeholder="t('output.version.placeholder')">
                            <option v-for="item in outputLanguages" :title="item" :value="item">
                                {{ getVersion(item) }}
                            </option>
                        </ComboBox>
                    </div>
                </div>
                <div class="editor">
                    <CodeMirror v-if="isDecompile && results.decompiled && !diagnostics.errors.length"
                                v-model:value="results.decompiled" :language="getOutputLanguage()"
                                :roslyn-tooltip="roslynTooltip.output" :readonly="true" style="flex: 1;" />
                    <div class="output" v-else>
                        <div class="syntax-tree" v-if="isSyntaxTree && syntaxTree">
                            <ol>
                                <SyntaxTreeItem :item="syntaxTree" />
                            </ol>
                        </div>
                        <div v-else-if="isRun && results.outputs.length && !diagnostics.errors.length">
                            <pre class="unset" v-for="item in renderConsole(results.outputs)" v-html="item"></pre>
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
    import type { Extension } from "@codemirror/state";
    import type { AstNodeItem, InfoTipItem, TextChanges } from "sharp-script";
    import type { setProperty, DotNetWorker, DiagnosticWrapper } from "./worker";
    import type { ChangeHandler, ExtensionHandler, TooltipHandler } from "./components/CodeMirror.vue";
    import { computed, nextTick, onMounted, ref, shallowRef, useTemplateRef, watch, watchPostEffect } from "vue";
    import { useI18n } from "vue-i18n";
    import { useSeoMeta } from "@unhead/vue";
    import { compressToEncodedURIComponent, decompressFromEncodedURIComponent } from "lz-string";
    import { useAnalytics } from "./helpers/analytics";
    import { AsyncLock, Comlink } from "./helpers/shared";
    import { AnsiUp } from "ansi_up";
    import { keymap, type ViewUpdate } from "@codemirror/view";
    import { createCompletion } from "./editor/completion";
    import { createLinter } from "./editor/diagnostics";
    import { createFormatKeymap, formatAsync } from "./editor/formatting";
    import { createTooltip } from "./editor/hover";
    import { csharpExample, vbExample, ilExample, cilExample } from "./helpers/examples";
    import { getCustomCompletionAsync } from "./helpers/fingerprinting";
    import { setTimeoutAsync } from "./helpers/utils";
    import { keywords } from "./package.json";
    import ComboBox from "./components/ComboBox.vue";
    import ProgressRing from "./components/ProgressRing.vue";
    import SplitPanels from "./components/SplitPanels.vue";
    import CodeMirror from "./components/CodeMirror.vue";
    import SyntaxTreeItem from "./components/SyntaxTreeItem.vue";
    import ToggleButton from "./components/ToggleButton.vue";
    import TriangleRight12Filled from "@fluentui/svg-icons/icons/triangle_right_12_filled.svg?component";
    import CodeText16Regular from "@fluentui/svg-icons/icons/code_text_16_regular.svg?component";
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

    let noreferrer = false;
    const hash = location.hash.substring(1);
    if (hash) {
        const params = new URLSearchParams(hash);
        if (params.has("noreferrer")) {
            noreferrer = params.get("noreferrer") !== "false";;
        }
    }
    useAnalytics(noreferrer);

    const code = shallowRef(csharpExample);
    const language = shallowRef("CSharp");
    const inputLanguages = ref<readonly string[]>(["Default", "CSharp1", "CSharp2", "CSharp3", "CSharp4", "CSharp5", "CSharp6", "CSharp7", "CSharp7_1", "CSharp7_2", "CSharp7_3", "CSharp8", "CSharp9", "CSharp10", "CSharp11", "CSharp12", "CSharp13", "CSharp14", "LatestMajor", "Preview", "Latest"]);
    const inputLanguage = shallowRef<string | undefined>("Preview");
    const isScript = shallowRef(false);
    const output = shallowRef("Run");
    const outputLanguages = ref<readonly string[]>([]);
    const outputLanguage = shallowRef<string | undefined>("CSharp1");
    const loading = shallowRef(false);
    const message = shallowRef('');
    const results = ref<{
        decompiled?: string,
        outputs: readonly string[]
    }>({ outputs: [] });

    const isRun = computed(() => output.value === "Run");
    const isSyntaxTree = computed(() => output.value === "SyntaxTree");
    const isDecompile = computed(() => !isRun.value && !isSyntaxTree.value);

    let dotnet: DotNetWorker | undefined;
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
                    await (newValue === "IL" || newValue === "CIL" ? initDotNetAsync() : initCompilerAsync());
                    await dotnet!.setLanguageTypeAsync(newValue);
                    inputLanguages.value = await dotnet!.getInputLanguageVersionsAsync();
                    inputLanguage.value = await dotnet!.getInputLanguageVersionAsync();
                    message.value = mes;
                }
                catch (e: any) {
                    message.value = t("message.error", e?.message || `${e}`);
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
                catch (e: any) {
                    message.value = t("message.error", e?.message || `${e}`);
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
                catch (e: any) {
                    message.value = t("message.error", e?.message || `${e}`);
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
                catch (e: any) {
                    message.value = t("message.error", e?.message || `${e}`);
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
                catch (e: any) {
                    message.value = t("message.error", e?.message || `${e}`);
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
        () => results.value.decompiled,
        async (newValue, oldValue) => {
            if (newValue !== oldValue && output.value === "CSharp") {
                setCSharpInfoTipLiteAsync(newValue ?? '');
            }
        }
    )

    async function resetCodeAsync(code: string) {
        try {
            return await dotnet!.resetCodeAsync(code);
        }
        catch (e) {
            console.warn(e);
        }
    }

    let changeList: TextChanges[] = [];
    async function applyChangesAsync() {
        try {
            if (changeList.length) {
                const task = dotnet!.applyChangesAsync(changeList);
                changeList = [];
                return await task;
            }
        }
        catch (e) {
            console.warn(e);
        }
    }

    async function formatCodeAsync() {
        try {
            return await dotnet!.formatCodeAsync();
        }
        catch (e) {
            console.warn(e);
        }
    }

    const isIL = computed(() => language.value === "IL" || language.value === "CIL");

    const diagnostics = ref({
        errors: [] as DiagnosticWrapper[],
        warnings: [] as DiagnosticWrapper[],
        infos: [] as DiagnosticWrapper[]
    });
    async function processAsync() {
        try {
            loading.value = true;
            const mes = message.value;
            message.value = t("message.compiling");
            setSettings();
            await (isIL.value ? initDotNetAsync() : initCompilerAsync());
            await initEditerAsync();
            await applyChangesAsync();
            await nextTick();
            const result = await dotnet!.processAsync();
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
        catch (e: any) {
            const mesg = e?.message || `${e}`;
            message.value = t("message.error", mesg);
            diagnostics.value.errors.push({
                id: '',
                location: {
                    start: { line: 0, character: 0 },
                    end: { line: 0, character: 0 }
                },
                message: mesg,
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

    async function getDiagnosticsAsync() {
        try {
            await applyChangesAsync();
            return await dotnet!.getDiagnosticsAsync();
        }
        catch (e) {
            console.warn(e);
            return [];
        }
    }

    async function getCompletionsAsync(position: number) {
        try {
            await applyChangesAsync();
            return await dotnet!.getCompletionsAsync(position);
        }
        catch (e) {
            console.warn(e);
            return [];
        }
    }

    async function getInfoTipAsync(position: number) {
        try {
            await applyChangesAsync();
            return await dotnet!.getInfoTipAsync(position);
        }
        catch (e) {
            console.warn(e);
            return {} as InfoTipItem;
        }
    }

    async function getAstAsync() {
        try {
            await applyChangesAsync();
            return await dotnet!.getAstAsync();
        }
        catch (e) {
            console.warn(e);
        }
    }

    async function setCSharpInfoTipLiteAsync(code: string) {
        try {
            return await dotnet!.setCSharpInfoTipLiteAsync(code);
        }
        catch (e) {
            console.warn(e);
        }
    }

    async function getCSharpInfoTipLiteAsync(position: number) {
        try {
            return await dotnet!.getCSharpInfoTipLiteAsync(position);
        }
        catch (e) {
            console.warn(e);
            return {} as InfoTipItem;
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

    const editor = useTemplateRef("editor");
    async function formatEditorAsync() {
        try {
            loading.value = true;
            const mes = message.value;
            message.value = t("message.formatting");
            await formatAsync(editor.value!.editor!, isIL, formatCodeAsync);
            message.value = mes;
        }
        catch (e) {
            console.warn(e);
        }
        finally {
            loading.value = false;
        }
    }

    async function downloadAssemblyAsync() {
        try {
            loading.value = true;
            const mes = message.value;
            message.value = t("message.compiling");
            await applyChangesAsync();
            const href = await dotnet!.getAssemblyLinkAsync();
            if (href) {
                const link = document.createElement('a');
                link.href = href;
                link.download = "SharpScript.zip";
                link.click();
            }
            message.value = mes;
        }
        catch (e: any) {
            message.value = t("message.error", e?.message || `${e}`);
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
            await (isIL.value ? initDotNetAsync() : initCompilerAsync());
            await initEditerAsync();
            message.value = mes;
        }
        catch (e) {
            console.warn(e);
        }
        finally {
            loading.value = false;
        }
    }

    const isInitDotnet = shallowRef(false);
    const locker = new AsyncLock();
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

    const isInitLinter = shallowRef(false);
    const syntaxTree = shallowRef<AstNodeItem>();
    const onChange = shallowRef<ChangeHandler>();
    const linter = ref<Extension | undefined>();
    const lintGutter = ref(false);
    const roslynCompletion = ref<ExtensionHandler>();
    const roslynTooltip = ref({
        input: undefined as TooltipHandler,
        output: undefined as TooltipHandler
    });
    const keymapProp = ref<Extension | undefined>();
    async function initEditerAsync() {
        if (!isInitLinter.value) {
            await resetCodeAsync(code.value);

            onChange.value = ({ changes }: ViewUpdate) => {
                const events: TextChanges = [];
                changes.iterChanges((fromA, toA, _, __, inserted) => {
                    events.push({
                        span: {
                            start: fromA,
                            end: toA,
                        },
                        newText: inserted.toString(),
                    });
                });
                events.sort((a, b) => {
                    if (!("span" in a)) { return 1; }
                    if (!("span" in b)) { return -1; }
                    return (b.span.start - a.span.start);
                });
                changeList.push(events);
            };

            linter.value = createLinter(
                () => {
                    if (isSyntaxTree) {
                        getAstAsync().then(x => syntaxTree.value = x!);
                    }
                },
                getDiagnosticsAsync,
                diagnosticInvokeAsync,
                diagnostics,
                language
            );
            lintGutter.value = true;

            roslynCompletion.value = async () => createCompletion(
                getCompletionsAsync,
                completionGetDescriptionAsync,
                completionGetChangeAsync,
                getCustomCompletionAsync(isIL, await dotnet!.fingerprinting)
            );

            roslynTooltip.value.input = options => createTooltip(getInfoTipAsync, options);
            roslynTooltip.value.output = () => createTooltip(getCSharpInfoTipLiteAsync);

            keymapProp.value = keymap.of(createFormatKeymap(isIL, formatCodeAsync));

            isInitLinter.value = true;
        }
    }

    function getLauguage() {
        switch (language.value) {
            case "IL":
            case "CIL":
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

    function getVersion(version?: string) {
        return version ? version.replace("VisualBasic", "VB ").replace("CSharp", "C# ").replace('_', '.') : '';
    }

    function getDefaultCode(language: string) {
        switch (language) {
            case "CSharp":
                return csharpExample;
            case "VisualBasic":
                return vbExample;
            case "IL":
                return ilExample;
            case "CIL":
                return cilExample;
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
            if (params.has("version")) {
                inputLanguage.value = params.get("version")!;
            }
            if (params.has("output")) {
                output.value = params.get("output")! as typeof output.value;
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
            if (params.has("noreferrer")) {
                noreferrer = params.get("noreferrer") !== "false";;
            }
            if (params.has("noworker")) {
                return params.get("noworker") !== "false";
            }
        }
    }

    function setSettings() {
        const settings: Record<string, string> = {};
        if (noreferrer) {
            settings.noreferrer = "true";
        }
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
        if (inputLanguages.value.length) {
            if (settings.language === "VisualBasic") {
                if (inputLanguage.value !== "Latest") {
                    settings.version = inputLanguage.value!;
                }
            }
            else {
                if (inputLanguage.value !== "Preview") {
                    settings.version = inputLanguage.value!;
                }
            }
        }
        if (settings.output === "CSharp") {
            if (outputLanguage.value !== "CSharp1") {
                settings.csversion = outputLanguage.value!;
            }
        }
        if (code.value) {
            settings.code = compressToEncodedURIComponent(code.value);
        }
        location.hash = new URLSearchParams(settings).toString();
        hashChanged = true;
    }

    function renderConsole(output: readonly string[]) {
        const ansi_up = new AnsiUp();
        ansi_up.use_classes = true;
        return output.map(line => ansi_up.ansi_to_html(line));
    }

    const direction = shallowRef<"row" | "column">("row");
    onMounted(async () => {
        function onResize() {
            direction.value = innerWidth < innerHeight ? "column" : "row";
        }
        onResize();
        addEventListener("resize", onResize);
        await nextTick();
        const importWorker = () => import("./worker");
        if (loadSettings()) {
            dotnet = await importWorker().then(x => x.dotnet);
            noWorker = true;
        }
        else {
            const url = new URL(importWorker.toString().match(/import\(["'`](\S+)["'`]\)/)![1], import.meta.url);
            dotnet = Comlink.wrap<DotNetWorker>(new Worker(url.href, { type: "module" }));
        }
        addEventListener("hashchange", loadSettings);
    });
</script>

<style lang="scss">
    @use "./styles/fonts";
    @use "./styles/ansi";
    @use "./styles/theme";
    @use "./styles/colors";
    @use "./styles/controls";

    $base-transition: background-color colors.$control-faster-animation-duration ease-in-out;

    * {
        box-sizing: border-box;
    }

    :root {
        --large-gap-size: 8px;
        --font-size: #{colors.$content-control-font-size};
        --status-bar-height: 32px;

        @include theme.auto-theme {
            accent-color: theme.themed(colors.$accent-fill-color-default);
            color-scheme: theme.themed((light: light, dark: dark));
        }

        @media (max-width: 767px) {
            --large-gap-size: 6px;
            --font-size: #{colors.$caption-text-block-font-size};
            --status-bar-height: 28px;
        }
    }

    body {
        margin: 0;
        width: 100%;
        height: 100%;
        overflow: hidden;
        font-family: colors.$content-control-theme-font-family;
        font-size: var(--font-size);
        line-height: colors.$content-control-line-height;
        transition: $base-transition;

        @include theme.auto-theme {
            background: theme.themed(colors.$solid-background-fill-color-base);
            color: theme.themed(colors.$text-fill-color-primary);
        }
    }
</style>

<style lang="scss" scoped>
    @use "./styles/theme";
    @use "./styles/colors";

    $base-transition: background-color colors.$control-faster-animation-duration ease-in-out;

    :deep(pre.unset) {
        margin-top: 0;
        margin-bottom: 0;
        font-size: inherit;
        font-family: inherit;
        white-space: pre-wrap;
        overflow: visible;
    }

    .toolbar {
        display: flex;
        column-gap: 18.5px;
        overflow-x: auto;
        overflow-y: hidden;
        white-space: nowrap;
        justify-content: space-between;

        .toolgroup {
            display: flex;
            column-gap: 4px;

            button.icon-button {
                padding: 0;
                width: var(--status-bar-height);
                height: var(--status-bar-height);
            }

            button,
            .combobox {
                height: var(--status-bar-height);
                flex-shrink: 0;
            }
        }
    }

    .loading-progress {
        bottom: 0;
        width: var(--blazor-load-percentage, 0%);
        height: 3px;
        transition: all 0.2s ease-in-out;
        overflow: hidden;
        position: absolute;

        @include theme.auto-theme {
            background: theme.themed(colors.$accent-fill-color-default);
        }
    }

    .content {
        height: 100%;
        padding: var(--large-gap-size) var(--large-gap-size) 0 var(--large-gap-size);

        $status-bar-height: var(--status-bar-height);

        div.split-view {
            height: calc(100% - $status-bar-height);
            gap: var(--large-gap-size);

            :deep(.slotted) {
                display: flex;
                flex-direction: column;
                row-gap: var(--large-gap-size);

                &::-webkit-scrollbar {
                    display: none;
                }
            }

            .editor {
                flex: 1;
                display: flex;
                overflow: auto;
                flex-direction: column;
                border-radius: colors.$overlay-corner-radius;
                transition: $base-transition;

                @include theme.auto-theme {
                    background: theme.themed(colors.$card-background-fill-color-default);
                    border: 1px solid theme.themed(colors.$card-stroke-color-default);
                }

                :deep(.cm-editor) {
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
                    flex: 1;
                    display: flex;
                    line-height: 1.4;
                    flex-direction: column;
                    font-family: var(--font-monospace);
                    padding: 0 12px;
                    width: 100%;

                    &>div {
                        padding: 4px 0;
                    }

                    .syntax-tree {
                        display: flex;
                        flex-direction: row;

                        ol {
                            padding: 0;
                            margin: 4px 0;
                            white-space: nowrap;
                        }
                    }
                }
            }
        }

        div.status-bar {
            display: flex;
            height: $status-bar-height;
            padding-left: 3px;
            padding-right: 4px;
            font-family: var(--font-monospace);
            justify-content: space-between;
            align-items: center;

            >div {
                transform: translateY(-1px);
            }
        }
    }
</style>