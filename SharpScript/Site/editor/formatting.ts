import type { ComputedRef } from "vue";
import type { EditorView, Command } from "@codemirror/view";
import type { dotnet } from "../worker";

type msilFormatter = ReturnType<typeof import("codemirror-lang-msil").msilFormatter>;
let msilFormatter: msilFormatter | undefined;
export async function formatAsync(view: EditorView, isIL: ComputedRef<boolean>, formatCodeAsync: typeof dotnet.formatCodeAsync) {
    if (isIL.value) {
        msilFormatter ||= await import("codemirror-lang-msil").then<msilFormatter>(m => m.msilFormatter());
        if (msilFormatter(view)) {
            return true;
        }
    }
    const results = await formatCodeAsync();
    if (results instanceof Array) {
        view.dispatch({
            changes: results.map(x => {
                const span = x.span;
                return { from: span.start, to: span.end, insert: x.newText };
            })
        });
    }
}

function createFormat(isIL: ComputedRef<boolean>, formatCodeAsync: typeof dotnet.formatCodeAsync): Command {
    return view => {
        formatAsync(view, isIL, formatCodeAsync);
        return true;
    };
}

export function createFormatKeymap(isIL: ComputedRef<boolean>, formatCodeAsync: typeof dotnet.formatCodeAsync) {
    return [
        { key: "Shift-Alt-f", run: createFormat(isIL, formatCodeAsync), preventDefault: true }
    ];
}