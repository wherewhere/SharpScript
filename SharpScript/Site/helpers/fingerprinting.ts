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
            const value = fingers[key];
            const assembly = value.substring(0, value.lastIndexOf("."));
            assemblies.push(assembly);
        }
        return assemblies;
    }
}

export function getCustomCompletionAsync(isIL: ComputedRef<boolean>, fingerprinting: Fingerprinting) {
    return async (context: CompletionContext) => {
        if (!isIL.value) {
            const pos = context.pos;
            const line = context.state.doc.lineAt(pos);
            const text = line.text;
            if (text.startsWith("#r ") || text.startsWith("#R ")) {
                const path = text.substring(3).trim();
                const from = line.from + 3;
                const results: Completion[] = [];
                for (const assembly of await getAssemblyAsync(fingerprinting)) {
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
}