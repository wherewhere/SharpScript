import { linter } from "@codemirror/lint";
import type { Text } from "@codemirror/state";
import type { Ref } from "vue";
import type { LinePosition } from "sharp-script";
import type { DiagnosticWrapper, dotnet } from "../worker";

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

export function createLinter(
    onbefore: () => void,
    getDiagnosticsAsync: typeof dotnet.getDiagnosticsAsync,
    diagnosticInvokeAsync: typeof dotnet.diagnosticInvokeAsync,
    diagnostics: Ref<{
        errors: DiagnosticWrapper[],
        warnings: DiagnosticWrapper[],
        infos: DiagnosticWrapper[]
    }>,
    language: Ref<string>) {
    return linter(async view => {
        onbefore();
        let diags = await getDiagnosticsAsync();
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
                                            return { from: span.start, to: span.end, insert: x.newText };
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
    });
}