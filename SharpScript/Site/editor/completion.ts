import { autocompletion, ifNotIn, type Completion } from "@codemirror/autocomplete";
import { mapTextTagsToType, renderParts } from "../helpers/render-parts";
import type { dotnet } from "../worker";

export function createCompletion(
    getCompletionsAsync: typeof dotnet.getCompletionsAsync,
    completionGetDescriptionAsync: typeof dotnet.completionGetDescriptionAsync,
    completionGetChangeAsync: typeof dotnet.completionGetChangeAsync,
    customCompletionAsync: (context: any) => Promise<Completion[]>
) {
    return autocompletion({
        override: [ifNotIn([';', '{', '}'], async context => {
            const from = context.pos;
            const completions = await getCompletionsAsync(from);
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
    });
}