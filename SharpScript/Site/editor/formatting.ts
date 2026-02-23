import { type EditorView, type Command } from "@codemirror/view";
import type { dotnet } from "../worker";

export async function formatAsync(view: EditorView, formatCodeAsync: typeof dotnet.formatCodeAsync) {
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

function createFormat(formatCodeAsync: typeof dotnet.formatCodeAsync): Command {
    return view => {
        formatAsync(view, formatCodeAsync);
        return true;
    };
}

export function createFormatKeymap(formatCodeAsync: typeof dotnet.formatCodeAsync) {
    return [
        { key: "Shift-Alt-f", run: createFormat(formatCodeAsync), preventDefault: true }
    ];
}