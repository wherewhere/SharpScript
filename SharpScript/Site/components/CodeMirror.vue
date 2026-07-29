<template>
    <div ref="root"></div>
</template>

<script lang="ts" setup>
    import type { lang } from "../types";
    import { onMounted, onUnmounted, toRaw, useTemplateRef, watch } from "vue";
    import { basicSetup } from "codemirror";
    import { Compartment, EditorState, type Extension } from "@codemirror/state";
    import { indentUnit, StreamLanguage } from "@codemirror/language";
    import { keymap, type hoverTooltip, EditorView, type ViewUpdate } from "@codemirror/view";
    import { lintGutter } from "@codemirror/lint";
    import { vscodeDark, vscodeLight } from "@uiw/codemirror-theme-vscode";
    import { vscodeKeymap } from "@replit/codemirror-vscode-keymap";

    const { language, readonly, lintGutter: lintGutterProp, keymap: keymapProp, linter, roslynTooltip, roslynCompletion } = defineProps<{
        language?: lang;
        readonly?: boolean;
        lintGutter?: boolean;
        linter?: Extension;
        keymap?: Extension;
        roslynTooltip?: (options: Parameters<typeof hoverTooltip>[1]) => Extension;
        roslynCompletion?: () => Extension | Promise<Extension>;
    }>();
    export type TooltipHandler = typeof roslynTooltip;
    export type ExtensionHandler = typeof roslynCompletion;

    const value = defineModel<string>("value");
    const tooltipOptions: Parameters<typeof hoverTooltip>[1] = {
        hideOnChange: true
    };

    let changed = false;
    const languageSet = new Compartment();
    const readonlySet = new Compartment();
    const lintGutterSet = new Compartment();
    const linterSet = new Compartment();
    const keymapSet = new Compartment();
    const empty: Extension = [];
    watch(
        () => language,
        async (newValue, oldValue) => {
            if (newValue !== oldValue) {
                const lang = await getLanguageAsync(newValue!);
                editor!.dispatch({ effects: languageSet.reconfigure(lang) });
                updateTooltip();
                updateCompletionAsync();
            }
        });
    watch(
        value,
        (newValue, oldValue) => {
            if (!changed && newValue !== oldValue) {
                editor!.dispatch({ changes: { from: 0, to: editor!.state.doc.length, insert: newValue } });
            }
            changed = false;
        });
    watch(
        () => readonly,
        (newValue, oldValue) => {
            if (newValue !== oldValue) {
                editor!.dispatch({ effects: readonlySet.reconfigure(EditorState.readOnly.of(!!newValue)) });
            }
        });
    watch(
        () => lintGutterProp,
        (newValue, oldValue) => {
            if (newValue !== oldValue) {
                editor!.dispatch({ effects: lintGutterSet.reconfigure(newValue ? lintGutter() : empty) });
            }
        });
    watch(
        () => linter,
        (newValue, oldValue) => {
            if (newValue !== oldValue) {
                editor!.dispatch({ effects: linterSet.reconfigure(toRaw(newValue) || empty) });
            }
        });
    watch(
        () => keymapProp,
        (newValue, oldValue) => {
            if (newValue !== oldValue) {
                editor!.dispatch({ effects: keymapSet.reconfigure(toRaw(newValue) || empty) });
            }
        });
    watch(
        () => roslynTooltip,
        (newValue, oldValue) => {
            if (newValue !== oldValue && language !== "il") {
                editor!.dispatch({ effects: tooltipSet.reconfigure(newValue ? newValue(tooltipOptions) : empty) });
            }
        });
    watch(
        () => roslynCompletion,
        async (newValue, oldValue) => {
            if (newValue !== oldValue && language !== "il") {
                editor!.dispatch({ effects: autocompletionSet.reconfigure(newValue ? await newValue() : empty) });
            }
        });

    let editor: EditorView | null = null;
    const themeSet = new Compartment();
    function updateTheme(e: MediaQueryListEvent) {
        editor!.dispatch({ effects: themeSet.reconfigure(e.matches ? vscodeDark : vscodeLight) });
    }

    const tooltipSet = new Compartment();
    function getTooltip() {
        return language !== "il" && roslynTooltip ? roslynTooltip(tooltipOptions) : empty;
    }
    function updateTooltip() {
        editor!.dispatch({ effects: tooltipSet.reconfigure(getTooltip()) });
    }

    const autocompletionSet = new Compartment();
    async function getCompletionAsync() {
        return language !== "il" && roslynCompletion ? await roslynCompletion() : empty;
    }
    async function updateCompletionAsync() {
        editor!.dispatch({ effects: autocompletionSet.reconfigure(await getCompletionAsync()) });
    }

    async function getLanguageAsync(lang?: lang) {
        switch (lang) {
            case "il":
                const { msil } = await import("codemirror-lang-msil");
                return msil({
                    tooltip: {
                        options: {
                            hideOnChange: true
                        }
                    }
                });
            case "csharp":
                const { csharp } = await import("@where/codemirror-lang-csharp");
                return csharp();
            case "vb":
                return StreamLanguage.define(await import("@codemirror/legacy-modes/mode/vb").then(m => m.vb));
            default:
                return empty;
        }
    }

    const emit = defineEmits<{
        change: [update: ViewUpdate]
    }>();
    export type ChangeHandler = (update: ViewUpdate) => void;

    const root = useTemplateRef("root");
    const scheme = matchMedia("(prefers-color-scheme: dark)");
    onMounted(async () => {
        function getTheme() {
            if (typeof scheme !== "undefined") {
                scheme.addEventListener("change", updateTheme);
                if (scheme.matches) {
                    return themeSet.of(vscodeDark);
                }
            }
            return themeSet.of(vscodeLight);
        };
        editor = new EditorView({
            doc: value.value,
            parent: root.value!,
            extensions: [
                basicSetup,
                getTheme(),
                keymap.of(vscodeKeymap),
                indentUnit.of("    "),
                keymapSet.of(keymapProp || empty),
                linterSet.of(linter || empty),
                lintGutterSet.of(lintGutterProp ? lintGutter() : empty),
                readonlySet.of(EditorState.readOnly.of(!!readonly)),
                tooltipSet.of(getTooltip()),
                languageSet.of(empty),
                autocompletionSet.of(empty),
                EditorView.updateListener.of(e => {
                    if (e.docChanged) {
                        changed = true;
                        value.value = e.state.doc.toString();
                        emit("change", e);
                    }
                })
            ]
        });
        editor.dispatch({
            effects: [
                languageSet.reconfigure(await getLanguageAsync(language)),
                autocompletionSet.reconfigure(await getCompletionAsync())
            ]
        });
    });
    onUnmounted(() => {
        if (typeof scheme !== "undefined") {
            scheme.removeEventListener("change", updateTheme);
        }
        editor!.destroy();
    });

    defineExpose({
        get editor() {
            return editor;
        }
    });
</script>

<style lang="scss">
    .hidden-icon::after,
    .info-icon::after,
    .warning-icon::after,
    .error-icon::after {
        display: inline-block;
        width: calc(var(--font-size) + 2px);
        height: calc(var(--font-size) + 2px);
    }

    .hidden-icon::after,
    .info-icon::after {
        content: url("../assets/vs-icon/light/StatusInformation.svg");
    }

    .warning-icon::after {
        content: url("../assets/vs-icon/light/StatusWarning.svg");
    }

    .error-icon::after {
        content: url("../assets/vs-icon/light/StatusError.svg");
    }

    @media (prefers-color-scheme: dark) {

        .hidden-icon::after,
        .info-icon::after {
            content: url("../assets/vs-icon/dark/StatusInformation.svg");
        }

        .warning-icon::after {
            content: url("../assets/vs-icon/dark/StatusWarning.svg");
        }

        .error-icon::after {
            content: url("../assets/vs-icon/dark/StatusError.svg");
        }
    }
</style>

<style lang="scss" scoped>
    @use "../styles/theme";
    @use "../styles/colors";

    $combobox-item-foreground: colors.$text-fill-color-primary;
    $combobox-item-foreground-pressed: colors.$text-fill-color-secondary;
    $combobox-item-foreground-pointer-over: colors.$text-fill-color-primary;
    $combobox-item-foreground-disabled: colors.$text-fill-color-disabled;
    $combobox-item-foreground-selected: colors.$text-fill-color-primary;
    $combobox-item-foreground-selected-unfocused: colors.$text-fill-color-primary;
    $combobox-item-foreground-selected-pressed: colors.$text-fill-color-secondary;
    $combobox-item-foreground-selected-pointer-over: colors.$text-fill-color-primary;
    $combobox-item-foreground-selected-disabled: colors.$text-fill-color-disabled;
    $combobox-item-background: colors.$subtle-fill-color-transparent;
    $combobox-item-background-pressed: colors.$subtle-fill-color-tertiary;
    $combobox-item-background-pointer-over: colors.$subtle-fill-color-secondary;
    $combobox-item-background-disabled: colors.$subtle-fill-color-disabled;
    $combobox-item-background-selected: colors.$subtle-fill-color-secondary;
    $combobox-item-background-selected-unfocused: colors.$subtle-fill-color-tertiary;
    $combobox-item-background-selected-pressed: colors.$subtle-fill-color-secondary;
    $combobox-item-background-selected-pointer-over: colors.$subtle-fill-color-tertiary;
    $combobox-item-background-selected-disabled: colors.$subtle-fill-color-secondary;

    $combobox-dropdown-foreground: colors.$text-fill-color-primary;
    $combobox-dropdown-background: colors.$solid-background-fill-color-tertiary;
    $combobox-dropdown-border: colors.$surface-stroke-color-flyout;

    $combobox-padding: 6px 34px 6px 11px;
    $combobox-dropdown-border-thickness: 1px;
    $combobox-dropdown-content-margin: 5px 4px;
    $combobox-dropdown-button-background-corner-radius: 4px;

    $compact-combobox-item-theme-padding: 2px 4px;
    $combobox-item-corner-radius: 3px;

    $flyout-presenter-background: colors.$solid-background-fill-color-tertiary;
    $flyout-border-theme-brush: colors.$surface-stroke-color-flyout;
    $flyout-border-theme-thickness: 1px;

    $flyout-theme-max-height: 758px;
    $flyout-theme-max-width: 456px;
    $flyout-theme-min-height: 44px;
    $flyout-theme-min-width: 96px;
    $flyout-content-padding: 8px 12px;

    :deep(.cm-editor) {
        background: none;
        outline: none;

        .cm-gutter-lint {
            width: 3px;
            overflow: visible;

            .cm-gutterElement {
                padding: 0;

                .cm-lint-marker {
                    content: none;
                    height: 100%;
                    width: 100%;
                    transform-origin: left;
                    transition: transform colors.$control-faster-animation-duration ease-in-out;
                    
                    &:hover {
                        transform: scaleX(2);
                    }

                    &.cm-lint-marker-hint {
                        background: transparent;
                    }

                    &.cm-lint-marker-info {
                        background: #a5a5a5;
                    }

                    &.cm-lint-marker-warning {
                        background: #008000;
                    }

                    &.cm-lint-marker-error {
                        background: #ff0000;
                    }

                    @media (prefers-color-scheme: dark) {
                        &.cm-lint-marker-warning {
                            background: #95db7d;
                        }

                        &.cm-lint-marker-error {
                            background: #fc3e36;
                        }
                    }
                }
            }
        }

        .cm-scroller,
        .cm-diagnostic,
        .cm-completionInfo,
        .cm-tooltip-autocomplete>ul {
            font-family: var(--font-monospace);
        }

        .cm-tooltip {
            display: flex;
            flex-direction: column;
            border-radius: colors.$overlay-corner-radius;
            max-height: min($flyout-theme-max-height, 100%);
            max-width: min($flyout-theme-max-width, 100%);
            padding: $flyout-content-padding;
            box-shadow: 0 0 16px rgba(0, 0, 0, .14);
            transition: opacity colors.$control-faster-animation-duration linear;

            @include theme.auto-theme {
                background: theme.themed($flyout-presenter-background);
                border: $flyout-border-theme-thickness solid theme.themed($flyout-border-theme-brush);
            }

            @starting-style {
                opacity: 0;
            }

            .cm-diagnostic {
                display: flex;
                flex-wrap: wrap;
                gap: 4px;

                .cm-diagnosticText {
                    flex-basis: 100%;
                }

                .cm-diagnosticAction {
                    margin-left: 0;
                }
            }

            .cm-tooltip-section {
                white-space: pre-wrap;
                word-wrap: break-word;
                font-family: var(--font-monospace);

                &:not(:first-child) {
                    margin-top: 2px;
                    padding-top: 2px;

                    @include theme.auto-theme {
                        border-top: 1px solid theme.themed(colors.$divider-stroke-color-default);
                    }
                }
            }

            .cm-diagnostic-hint,
            .cm-diagnostic-info {
                border-left: 3px solid #a5a5a5;
            }

            .cm-diagnostic-warning {
                border-left: 3px solid #008000;
            }

            .cm-diagnostic-error {
                border-left: 3px solid #ff0000;
            }

            @media (prefers-color-scheme: dark) {
                .cm-diagnostic-warning {
                    border-left: 3px solid #95db7d;
                }

                .cm-diagnostic-error {
                    border-left: 3px solid #fc3e36;
                }
            }
        }

        .cm-tooltip-autocomplete {
            opacity: 1;
            border-radius: colors.$overlay-corner-radius;
            padding: $combobox-dropdown-content-margin;
            box-shadow: 0 0 16px rgba(0, 0, 0, .14);
            transition: opacity colors.$control-faster-animation-duration linear;

            @include theme.auto-theme {
                color: theme.themed($combobox-dropdown-foreground);
                background: theme.themed($combobox-dropdown-background);
                border: $combobox-dropdown-border-thickness solid theme.themed($combobox-dropdown-border);
            }

            @starting-style {
                opacity: 0;
            }

            >ul {
                scrollbar-width: thin;

                >li {
                    padding: $compact-combobox-item-theme-padding;
                    line-height: colors.$content-control-line-height;
                    border-radius: $combobox-item-corner-radius;
                    transition: background-color colors.$control-faster-animation-duration ease-in-out;

                    @include theme.auto-theme {
                        color: theme.themed($combobox-item-foreground);
                        background-color: theme.themed($combobox-item-background);

                        &:not(:disabled):hover {
                            color: theme.themed($combobox-item-foreground-pointer-over);
                            background: theme.themed($combobox-item-background-pointer-over);
                        }

                        &:not(:disabled):active {
                            color: theme.themed($combobox-item-foreground-pressed);
                            background: theme.themed($combobox-item-background-pressed);
                        }

                        &:disabled {
                            color: theme.themed($combobox-item-foreground-disabled);
                            background: theme.themed($combobox-item-background-disabled);
                        }

                        &[aria-selected]:not(:disabled) {
                            color: theme.themed($combobox-item-foreground-selected);
                            background: theme.themed($combobox-item-background-selected);
                        }

                        &[aria-selected]:not(:disabled):hover {
                            color: theme.themed($combobox-item-foreground-selected-pointer-over);
                            background: theme.themed($combobox-item-background-selected-pointer-over);
                        }

                        &[aria-selected]:not(:disabled):active {
                            color: theme.themed($combobox-item-foreground-selected-pressed);
                            background: theme.themed($combobox-item-background-selected-pressed);
                        }

                        &[aria-selected]:disabled {
                            color: theme.themed($combobox-item-foreground-selected-disabled);
                            background: theme.themed($combobox-item-background-selected-disabled);
                        }
                    }
                }
            }
        }

        .tok-keyword,
        .tok-bool,
        .tok-null {
            color: #0000ff;
        }

        .tok-number {
            color: #000000;
        }

        .tok-string {
            color: #a31515;
        }

        .tok-comment {
            color: #008000;
        }

        .tok-type,
        .tok-class,
        .tok-struct,
        .tok-interface,
        .tok-enum,
        .tok-delegate {
            color: #2b91af;
        }

        .cm-lintRange-unnecessary {
            opacity: 0.66;
        }

        .cm-lintRange-hint {
            background: none;
        }

        .cm-lintRange-info {
            background: none;
            position: relative;

            &::after {
                width: 2px;
                height: 2px;
                border-radius: 50%;
                content: '';
                position: absolute;
                bottom: 0;
                left: 1px;
                background: #a5a5a5;
                box-shadow: 3.5px 0 0 #a5a5a5;
            }
        }

        .cm-lintRange-warning {
            background: none;
            text-decoration: underline wavy #008000;
        }

        .cm-lintRange-error {
            background: none;
            text-decoration: underline wavy #ff0000;
        }

        @media (prefers-color-scheme: dark) {

            .tok-keyword,
            .tok-bool,
            .tok-null {
                color: #569cd6;
            }

            .tok-number {
                color: #b5cea8;
            }

            .tok-string {
                color: #d69d85;
            }

            .tok-comment {
                color: #57a64a;
            }

            .tok-type,
            .tok-class,
            .tok-struct,
            .tok-interface,
            .tok-enum,
            .tok-delegate {
                color: #4ec9b0;
            }

            .cm-lintRange-unnecessary {
                opacity: 0.73;
            }

            .cm-lintRange-warning {
                text-decoration: underline wavy #95db7d;
            }

            .cm-lintRange-error {
                text-decoration: underline wavy #fc3e36;
            }
        }

        .cm-panels {
            @include theme.auto-theme {
                background: theme.themed(colors.$card-background-fill-color-default);
                border-top: 1px solid theme.themed(colors.$card-stroke-color-default);
            }
        }

        .cm-panel.cm-search label {
            font-size: colors.$caption-text-block-font-size;
            display: inline-flex;
            vertical-align: middle;
            align-items: center;
        }

        .cm-completionIcon {
            width: 16px;
            height: 16px;
            margin-bottom: -3px;
            padding-right: 0.3em;
            font-size: 100%;
            opacity: 1;

            &:after {
                content: '\00a0';
            }

            &.cm-completionIcon-assembly {
                content: url("../assets/vs-icon/light/Assembly.svg");
            }

            &.cm-completionIcon-class,
            &.cm-completionIcon-class-public {
                content: url("../assets/vs-icon/light/ClassPublic.svg");
            }

            &.cm-completionIcon-class-protected {
                content: url("../assets/vs-icon/light/ClassProtected.svg");
            }

            &.cm-completionIcon-class-private {
                content: url("../assets/vs-icon/light/ClassPrivate.svg");
            }

            &.cm-completionIcon-class-internal {
                content: url("../assets/vs-icon/light/ClassInternal.svg");
            }

            &.cm-completionIcon-constant,
            &.cm-completionIcon-constant-public {
                content: url("../assets/vs-icon/light/ConstantPublic.svg");
            }

            &.cm-completionIcon-constant-protected {
                content: url("../assets/vs-icon/light/ConstantProtected.svg");
            }

            &.cm-completionIcon-constant-private {
                content: url("../assets/vs-icon/light/ConstantPrivate.svg");
            }

            &.cm-completionIcon-constant-internal {
                content: url("../assets/vs-icon/light/ConstantInternal.svg");
            }

            &.cm-completionIcon-delegate-public {
                content: url("../assets/vs-icon/light/DelegatePublic.svg");
            }

            &.cm-completionIcon-delegate-protected {
                content: url("../assets/vs-icon/light/DelegateProtected.svg");
            }

            &.cm-completionIcon-delegate-private {
                content: url("../assets/vs-icon/light/DelegatePrivate.svg");
            }

            &.cm-completionIcon-delegate-internal {
                content: url("../assets/vs-icon/light/DelegateInternal.svg");
            }

            &.cm-completionIcon-enum,
            &.cm-completionIcon-enum-public {
                content: url("../assets/vs-icon/light/EnumerationPublic.svg");
            }

            &.cm-completionIcon-enum-protected {
                content: url("../assets/vs-icon/light/EnumerationProtected.svg");
            }

            &.cm-completionIcon-enum-private {
                content: url("../assets/vs-icon/light/EnumerationPrivate.svg");
            }

            &.cm-completionIcon-enum-internal {
                content: url("../assets/vs-icon/light/EnumerationInternal.svg");
            }

            &.cm-completionIcon-enummember-public {
                content: url("../assets/vs-icon/light/EnumerationItemPublic.svg");
            }

            &.cm-completionIcon-enummember-protected {
                content: url("../assets/vs-icon/light/EnumerationItemProtected.svg");
            }

            &.cm-completionIcon-enummember-private {
                content: url("../assets/vs-icon/light/EnumerationItemPrivate.svg");
            }

            &.cm-completionIcon-enummember-internal {
                content: url("../assets/vs-icon/light/EnumerationItemInternal.svg");
            }

            &.cm-completionIcon-event-public {
                content: url("../assets/vs-icon/light/EventPublic.svg");
            }

            &.cm-completionIcon-event-protected {
                content: url("../assets/vs-icon/light/EventProtected.svg");
            }

            &.cm-completionIcon-event-private {
                content: url("../assets/vs-icon/light/EventPrivate.svg");
            }

            &.cm-completionIcon-event-internal {
                content: url("../assets/vs-icon/light/EventInternal.svg");
            }

            &.cm-completionIcon-extensionmethod-public,
            &.cm-completionIcon-extensionmethod-protected,
            &.cm-completionIcon-extensionmethod-private,
            &.cm-completionIcon-extensionmethod-internal {
                content: url("../assets/vs-icon/light/ExtensionMethod.svg");
            }

            &.cm-completionIcon-field-public {
                content: url("../assets/vs-icon/light/FieldPublic.svg");
            }

            &.cm-completionIcon-field-protected {
                content: url("../assets/vs-icon/light/FieldProtected.svg");
            }

            &.cm-completionIcon-field-private {
                content: url("../assets/vs-icon/light/FieldPrivate.svg");
            }

            &.cm-completionIcon-field-internal {
                content: url("../assets/vs-icon/light/FieldInternal.svg");
            }

            &.cm-completionIcon-interface,
            &.cm-completionIcon-interface-public {
                content: url("../assets/vs-icon/light/InterfacePublic.svg");
            }

            &.cm-completionIcon-interface-protected {
                content: url("../assets/vs-icon/light/InterfaceProtected.svg");
            }

            &.cm-completionIcon-interface-private {
                content: url("../assets/vs-icon/light/InterfacePrivate.svg");
            }

            &.cm-completionIcon-interface-internal {
                content: url("../assets/vs-icon/light/InterfaceInternal.svg");
            }

            &.cm-completionIcon-keyword,
            &.cm-completionIcon-intrinsic,
            &.cm-completionIcon-keyword-intrinsic {
                content: url("../assets/vs-icon/light/IntelliSenseKeyword.svg");
            }

            &.cm-completionIcon-text,
            &.cm-completionIcon-label {
                content: url("../assets/vs-icon/light/Label.svg");
            }

            &.cm-completionIcon-variable,
            &.cm-completionIcon-local {
                content: url("../assets/vs-icon/light/LocalVariable.svg");
            }

            &.cm-completionIcon-namespace {
                content: url("../assets/vs-icon/light/Namespace.svg");
            }

            &.cm-completionIcon-function,
            &.cm-completionIcon-method,
            &.cm-completionIcon-method-public {
                content: url("../assets/vs-icon/light/MethodPublic.svg");
            }

            &.cm-completionIcon-method-protected {
                content: url("../assets/vs-icon/light/MethodProtected.svg");
            }

            &.cm-completionIcon-method-private {
                content: url("../assets/vs-icon/light/MethodPrivate.svg");
            }

            &.cm-completionIcon-method-internal {
                content: url("../assets/vs-icon/light/MethodInternal.svg");
            }

            &.cm-completionIcon-module-public {
                content: url("../assets/vs-icon/light/ModulePublic.svg");
            }

            &.cm-completionIcon-module-protected {
                content: url("../assets/vs-icon/light/ModuleProtected.svg");
            }

            &.cm-completionIcon-module-private {
                content: url("../assets/vs-icon/light/ModulePrivate.svg");
            }

            &.cm-completionIcon-module-internal {
                content: url("../assets/vs-icon/light/ModuleInternal.svg");
            }

            &.cm-completionIcon-folder {
                content: url("../assets/vs-icon/light/FolderClosed.svg");
            }

            &.cm-completionIcon-operator {
                content: url("../assets/vs-icon/light/Operator.svg");
            }

            &.cm-completionIcon-parameter {
                content: url("../assets/vs-icon/light/Parameter.svg");
            }

            &.cm-completionIcon-property,
            &.cm-completionIcon-property-public {
                content: url("../assets/vs-icon/light/PropertyPublic.svg");
            }

            &.cm-completionIcon-property-protected {
                content: url("../assets/vs-icon/light/PropertyProtected.svg");
            }

            &.cm-completionIcon-property-private {
                content: url("../assets/vs-icon/light/PropertyPrivate.svg");
            }

            &.cm-completionIcon-property-internal {
                content: url("../assets/vs-icon/light/PropertyInternal.svg");
            }

            &.cm-completionIcon-reference {
                content: url("../assets/vs-icon/light/Reference.svg");
            }

            &.cm-completionIcon-type,
            &.cm-completionIcon-structure-public {
                content: url("../assets/vs-icon/light/ValueTypePublic.svg");
            }

            &.cm-completionIcon-structure-protected {
                content: url("../assets/vs-icon/light/ValueTypeProtected.svg");
            }

            &.cm-completionIcon-structure-private {
                content: url("../assets/vs-icon/light/ValueTypePrivate.svg");
            }

            &.cm-completionIcon-structure-internal {
                content: url("../assets/vs-icon/light/ValueTypeInternal.svg");
            }

            &.cm-completionIcon-typeparameter {
                content: url("../assets/vs-icon/light/Type.svg");
            }

            &.cm-completionIcon-snippet {
                content: url("../assets/vs-icon/light/Snippet.svg");
            }

            @media (prefers-color-scheme: dark) {
                &.cm-completionIcon-assembly {
                    content: url("../assets/vs-icon/dark/Assembly.svg");
                }

                &.cm-completionIcon-class,
                &.cm-completionIcon-class-public:after {
                    content: url("../assets/vs-icon/dark/ClassPublic.svg");
                }

                &.cm-completionIcon-class-protected {
                    content: url("../assets/vs-icon/dark/ClassProtected.svg");
                }

                &.cm-completionIcon-class-private {
                    content: url("../assets/vs-icon/dark/ClassPrivate.svg");
                }

                &.cm-completionIcon-class-internal {
                    content: url("../assets/vs-icon/dark/ClassInternal.svg");
                }

                &.cm-completionIcon-constant,
                &.cm-completionIcon-constant-public {
                    content: url("../assets/vs-icon/dark/ConstantPublic.svg");
                }

                &.cm-completionIcon-constant-protected {
                    content: url("../assets/vs-icon/dark/ConstantProtected.svg");
                }

                &.cm-completionIcon-constant-private {
                    content: url("../assets/vs-icon/dark/ConstantPrivate.svg");
                }

                &.cm-completionIcon-constant-internal {
                    content: url("../assets/vs-icon/dark/ConstantInternal.svg");
                }

                &.cm-completionIcon-delegate-public {
                    content: url("../assets/vs-icon/dark/DelegatePublic.svg");
                }

                &.cm-completionIcon-delegate-protected {
                    content: url("../assets/vs-icon/dark/DelegateProtected.svg");
                }

                &.cm-completionIcon-delegate-private {
                    content: url("../assets/vs-icon/dark/DelegatePrivate.svg");
                }

                &.cm-completionIcon-delegate-internal {
                    content: url("../assets/vs-icon/dark/DelegateInternal.svg");
                }

                &.cm-completionIcon-enum,
                &.cm-completionIcon-enum-public {
                    content: url("../assets/vs-icon/dark/EnumerationPublic.svg");
                }

                &.cm-completionIcon-enum-protected {
                    content: url("../assets/vs-icon/dark/EnumerationProtected.svg");
                }

                &.cm-completionIcon-enum-private {
                    content: url("../assets/vs-icon/dark/EnumerationPrivate.svg");
                }

                &.cm-completionIcon-enum-internal {
                    content: url("../assets/vs-icon/dark/EnumerationInternal.svg");
                }

                &.cm-completionIcon-enummember-public {
                    content: url("../assets/vs-icon/dark/EnumerationItemPublic.svg");
                }

                &.cm-completionIcon-enummember-protected {
                    content: url("../assets/vs-icon/dark/EnumerationItemProtected.svg");
                }

                &.cm-completionIcon-enummember-private {
                    content: url("../assets/vs-icon/dark/EnumerationItemPrivate.svg");
                }

                &.cm-completionIcon-enummember-internal {
                    content: url("../assets/vs-icon/dark/EnumerationItemInternal.svg");
                }

                &.cm-completionIcon-event-public {
                    content: url("../assets/vs-icon/dark/EventPublic.svg");
                }

                &.cm-completionIcon-event-protected {
                    content: url("../assets/vs-icon/dark/EventProtected.svg");
                }

                &.cm-completionIcon-event-private {
                    content: url("../assets/vs-icon/dark/EventPrivate.svg");
                }

                &.cm-completionIcon-event-internal {
                    content: url("../assets/vs-icon/dark/EventInternal.svg");
                }

                &.cm-completionIcon-extensionmethod-public,
                &.cm-completionIcon-extensionmethod-protected,
                &.cm-completionIcon-extensionmethod-private,
                &.cm-completionIcon-extensionmethod-internal {
                    content: url("../assets/vs-icon/dark/ExtensionMethod.svg");
                }

                &.cm-completionIcon-field-public {
                    content: url("../assets/vs-icon/dark/FieldPublic.svg");
                }

                &.cm-completionIcon-field-protected {
                    content: url("../assets/vs-icon/dark/FieldProtected.svg");
                }

                &.cm-completionIcon-field-private {
                    content: url("../assets/vs-icon/dark/FieldPrivate.svg");
                }

                &.cm-completionIcon-field-internal {
                    content: url("../assets/vs-icon/dark/FieldInternal.svg");
                }

                &.cm-completionIcon-interface,
                &.cm-completionIcon-interface-public {
                    content: url("../assets/vs-icon/dark/InterfacePublic.svg");
                }

                &.cm-completionIcon-interface-protected {
                    content: url("../assets/vs-icon/dark/InterfaceProtected.svg");
                }

                &.cm-completionIcon-interface-private {
                    content: url("../assets/vs-icon/dark/InterfacePrivate.svg");
                }

                &.cm-completionIcon-interface-internal {
                    content: url("../assets/vs-icon/dark/InterfaceInternal.svg");
                }

                &.cm-completionIcon-keyword,
                &.cm-completionIcon-intrinsic,
                &.cm-completionIcon-keyword-intrinsic {
                    content: url("../assets/vs-icon/dark/IntelliSenseKeyword.svg");
                }

                &.cm-completionIcon-text,
                &.cm-completionIcon-label {
                    content: url("../assets/vs-icon/dark/Label.svg");
                }

                &.cm-completionIcon-variable,
                &.cm-completionIcon-local {
                    content: url("../assets/vs-icon/dark/LocalVariable.svg");
                }

                &.cm-completionIcon-namespace {
                    content: url("../assets/vs-icon/dark/Namespace.svg");
                }

                &.cm-completionIcon-function,
                &.cm-completionIcon-method,
                &.cm-completionIcon-method-public {
                    content: url("../assets/vs-icon/dark/MethodPublic.svg");
                }

                &.cm-completionIcon-method-protected {
                    content: url("../assets/vs-icon/dark/MethodProtected.svg");
                }

                &.cm-completionIcon-method-private {
                    content: url("../assets/vs-icon/dark/MethodPrivate.svg");
                }

                &.cm-completionIcon-method-internal {
                    content: url("../assets/vs-icon/dark/MethodInternal.svg");
                }

                &.cm-completionIcon-module-public {
                    content: url("../assets/vs-icon/dark/ModulePublic.svg");
                }

                &.cm-completionIcon-module-protected {
                    content: url("../assets/vs-icon/dark/ModuleProtected.svg");
                }

                &.cm-completionIcon-module-private {
                    content: url("../assets/vs-icon/dark/ModulePrivate.svg");
                }

                &.cm-completionIcon-module-internal {
                    content: url("../assets/vs-icon/dark/ModuleInternal.svg");
                }

                &.cm-completionIcon-folder {
                    content: url("../assets/vs-icon/dark/FolderClosed.svg");
                }

                &.cm-completionIcon-operator {
                    content: url("../assets/vs-icon/dark/Operator.svg");
                }

                &.cm-completionIcon-parameter {
                    content: url("../assets/vs-icon/dark/Parameter.svg");
                }

                &.cm-completionIcon-property,
                &.cm-completionIcon-property-public {
                    content: url("../assets/vs-icon/dark/PropertyPublic.svg");
                }

                &.cm-completionIcon-property-protected {
                    content: url("../assets/vs-icon/dark/PropertyProtected.svg");
                }

                &.cm-completionIcon-property-private {
                    content: url("../assets/vs-icon/dark/PropertyPrivate.svg");
                }

                &.cm-completionIcon-property-internal {
                    content: url("../assets/vs-icon/dark/PropertyInternal.svg");
                }

                &.cm-completionIcon-reference {
                    content: url("../assets/vs-icon/dark/Reference.svg");
                }

                &.cm-completionIcon-type,
                &.cm-completionIcon-structure-public {
                    content: url("../assets/vs-icon/dark/ValueTypePublic.svg");
                }

                &.cm-completionIcon-structure-protected {
                    content: url("../assets/vs-icon/dark/ValueTypeProtected.svg");
                }

                &.cm-completionIcon-structure-private {
                    content: url("../assets/vs-icon/dark/ValueTypePrivate.svg");
                }

                &.cm-completionIcon-structure-internal {
                    content: url("../assets/vs-icon/dark/ValueTypeInternal.svg");
                }

                &.cm-completionIcon-typeparameter {
                    content: url("../assets/vs-icon/dark/Type.svg");
                }

                &.cm-completionIcon-snippet {
                    content: url("../assets/vs-icon/dark/Snippet.svg");
                }
            }
        }
    }
</style>