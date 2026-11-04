import type { ComputedRef } from "vue";
import type { Completion, CompletionContext } from "@codemirror/autocomplete";
import type { Fingerprinting } from "../worker";

let assemblies: string[];
async function getAssemblyAsync(fingerprinting: Fingerprinting | Promise<Fingerprinting>) {
    if (assemblies) {
        return assemblies;
    }
    else {
        const fingers = await fingerprinting;
        assemblies = [];
        for (const key in fingers) {
            const assembly = key.substring(0, key.lastIndexOf("."));
            assemblies.push(assembly);
        }
        return assemblies;
    }
}

const features = [
    "strict",
    "UseLegacyStrongNameProvider",
    "updated-memory-safety-rules",
    "enable-generator-cache",
    "pdb-path-determinism",
    "debug-determinism",
    "debug-analyzers",
    "runtime-async",
    "peverify-compat",
    "FileBasedProgram",
    "nullablePublicOnly",
    "run-nullable-analysis",
    "InterceptorsNamespaces",
    "noRefSafetyRulesAttribute",
    "disable-length-based-switch",
    "experimental-data-section-string-literals"
] as const;

export function getCustomCompletionAsync(isIL: ComputedRef<boolean>, fingerprinting: Fingerprinting) {
    return async (context: CompletionContext) => {
        if (!isIL.value) {
            const pos = context.pos;
            const line = context.state.doc.lineAt(pos);
            const text = line.text;
            if (text[0] === '#') {
                function createCompletion(label: string, type: string, from: number, info?: string): Completion {
                    return {
                        label, type, info,
                        apply(view, completion) {
                            const label = completion.label;
                            view.dispatch({
                                changes: { from, to: line.to, insert: label },
                                selection: { anchor: from + label.length }
                            });
                        }
                    };
                }
                if (text.length === 1) {
                    const from = line.from + 1;
                    return [
                        createCompletion('r', "keyword", from, "Reference an assembly"),
                        createCompletion("feature", "keyword", from, "Add a feature"),
                        createCompletion("assembly", "keyword", from, "Set the assembly name")
                    ];
                }
                else if ((text[1] == 'r' || text[1] == 'R') && text[2] == ' ') {
                    const path = text.substring(3).trim();
                    const from = line.from + 3;
                    const results: Completion[] = [];
                    for (const assembly of await getAssemblyAsync(fingerprinting)) {
                        if (assembly.startsWith(path)) {
                            results.push(createCompletion(assembly, "assembly", from));
                        }
                    }
                    return results;
                }
                else if (text.toLocaleLowerCase().startsWith("#feature ")) {
                    const feature = text.substring(9).trim();
                    if (feature.includes('=')) { return []; }
                    const from = line.from + 9;
                    const results: Completion[] = [];
                    for (const label of features) {
                        if (label.startsWith(feature)) {
                            results.push(createCompletion(label, "feature", from));
                        }
                    }
                    return results;
                }
                else if (!text.includes(' ')) {
                    switch (text[1]) {
                        case 'f':
                            return [createCompletion("feature", "keyword", line.from + 1, "Add a feature")];
                        case 'a':
                            return [createCompletion("assembly", "keyword", line.from + 1, "Set the assembly name")];
                        case 'F':
                            return [createCompletion("Feature", "keyword", line.from + 1, "Add a feature")];
                        case 'A':
                            return [createCompletion("Assembly", "keyword", line.from + 1, "Set the assembly name")];
                    }
                }
            }
        }
        return [];
    }
}