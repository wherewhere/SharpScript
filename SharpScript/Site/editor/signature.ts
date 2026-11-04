import "../types";
import type { dotnet } from "../worker";
import type { SignatureHelpItemsData, SignatureHelpItemData, SignatureHelpParameterData } from "sharp-script";
import { StateField, StateEffect } from "@codemirror/state";
import { ViewPlugin, type Tooltip, showTooltip } from "@codemirror/view";
import { createSection, renderPartsTo } from "../helpers/render-parts";
import CaretLeft16Filled from "@fluentui/svg-icons/icons/caret_left_16_filled.svg?raw";
import CaretRight16Filled from "@fluentui/svg-icons/icons/caret_right_16_filled.svg?raw";

const signaturePlugin = ViewPlugin.define((view, getSignatureHelpAsync: typeof dotnet.getSignatureHelpAsync) => {
    let activeRequest: { pos: number, drop: boolean } | undefined;
    let delayedRequest = 0 as number | ReturnType<typeof setTimeout>;

    async function startRequest(reason: 0 | 1 | 2, triggerCharacter: string | null) {
        const pos = view.state.selection.main.head;
        if (activeRequest) { activeRequest.drop = true; }
        let req = activeRequest = { pos, drop: false }
        const result = await getSignatureHelpAsync(pos, reason, triggerCharacter);
        if (req.drop) { return; }
        if (result?.items.length) {
            let cur = view.state.field(signatureState);
            // Don't update at all if nothing changed
            if (cur && sameSignatures(cur.data, result)) { return; }
            view.dispatch({ effects: signatureEffect.of(result) });
        }
        else if (view.state.field(signatureState)) {
            view.dispatch({ effects: signatureEffect.of(undefined) })
        }
    }

    return {
        update(update) {
            if (activeRequest) {
                if (update.selectionSet) {
                    activeRequest.drop = true;
                    activeRequest = undefined;
                }
                else if (update.docChanged) {
                    activeRequest.pos = update.changes.mapPos(activeRequest.pos);
                }
            }

            let triggerCharacter = '';
            if (update.docChanged && update.transactions.some(tr => tr.isUserEvent("input.type"))) {
                const triggers = ['(', ','] as const;
                update.changes.iterChanges((_fromA, _toA, _fromB, _toB, inserted) => {
                    const ins = inserted.toString();
                    if (ins) {
                        for (const ch of triggers) {
                            if (ins.indexOf(ch) > -1) {
                                triggerCharacter = ch;
                            }
                        }
                    }
                });
            }

            const sigState = update.view.state.field(signatureState);
            if (triggerCharacter) {
                if (delayedRequest) { clearTimeout(delayedRequest); }
                delayedRequest = setTimeout(() => startRequest(sigState ? 2 : 1, triggerCharacter), 50);
            }
            else if (update.selectionSet) {
                if (delayedRequest) { clearTimeout(delayedRequest); }
                delayedRequest = setTimeout(() => startRequest(2, '\0'), 250);
            }
        },
        destroy() {
            if (delayedRequest) { clearTimeout(delayedRequest); }
            if (activeRequest) { activeRequest.drop = true; }
        }
    }
});

function sameSignatures(a: SignatureHelpItemsData, b: SignatureHelpItemsData) {
    if (a === b) { return true; }

    const leftSpan = a.applicableSpan, rightSpan = b.applicableSpan;
    if (leftSpan.start !== rightSpan.start
        || a.argumentCount !== b.argumentCount
        || a.argumentIndex !== b.argumentIndex
        || a.selectedItemIndex !== b.selectedItemIndex) { return false; }

    const leftItems = a.items, rightItems = b.items;
    return leftItems === rightItems
        || (leftItems.length === rightItems.length
            && leftItems.every((s, i) => sameSignature(s, rightItems[i])));
}

function sameSignature(a: SignatureHelpItemData, b: SignatureHelpItemData) {
    return a === b ||
        (a.parameterCount == b.parameterCount
            && a.parameters.every((p, i) => sameParameter(p, b.parameters[i]))
            && a.prefixDisplayParts.every((p, i) => p.text == b.prefixDisplayParts[i].text)
            && a.suffixDisplayParts.every((p, i) => p.text == b.suffixDisplayParts[i].text)
            && a.separatorDisplayParts.every((p, i) => p.text == b.separatorDisplayParts[i].text));
}

function sameParameter(a: SignatureHelpParameterData, b: SignatureHelpParameterData) {
    return a === b ||
        (a.name == b.name
            && a.prefixDisplayParts.every((p, i) => p.text == b.prefixDisplayParts[i].text)
            && a.displayParts.every((p, i) => p.text == b.displayParts[i].text)
            && a.suffixDisplayParts.every((p, i) => p.text == b.suffixDisplayParts[i].text));
}

type SignatureState = {
    readonly data: SignatureHelpItemsData,
    readonly tooltip: Tooltip
}

const signatureState = StateField.define<SignatureState | undefined>({
    create() { },
    update(sig, tr) {
        for (const e of tr.effects) {
            if (e.is(signatureEffect)) {
                if (e.value) {
                    const data = e.value;
                    return {
                        data,
                        tooltip: {
                            above: true,
                            pos: e.value.applicableSpan.start,
                            end: e.value.applicableSpan.end,
                            create: () => {
                                return { dom: renderSignatureList(data) };
                            }
                        }
                    };
                }
                return;
            }
        }
        if (sig && tr.docChanged) {
            return {
                data: sig.data,
                tooltip: {
                    ...sig.tooltip,
                    pos: tr.changes.mapPos(sig.tooltip.pos)
                }
            };
        }
        return sig;
    },
    provide: f => showTooltip.from(f, sig => sig ? sig.tooltip : null)
});

const signatureEffect = StateEffect.define<SignatureHelpItemsData | undefined>()

function renderParameterTo(parent: HTMLElement, signature: SignatureHelpParameterData) {
    renderPartsTo(parent, signature.prefixDisplayParts, false);
    renderPartsTo(parent, signature.displayParts, false);
    renderPartsTo(parent, signature.suffixDisplayParts, false);
}

function renderSignatureTo(parent: HTMLElement, signature: SignatureHelpItemData, argumentIndex: number) {
    const title = createSection();
    parent.appendChild(title);
    const decription = createSection();

    const documentation = signature.documentation;
    if (documentation && documentation.length) {
        renderPartsTo(decription, documentation, false);
        parent.appendChild(decription);
    }

    renderPartsTo(title, signature.prefixDisplayParts, false);
    const parameterCount = signature.parameterCount;
    if (parameterCount) {
        let i = 0;
        while (true) {
            if (i === argumentIndex) {
                const parameter = signature.parameters[i++];

                let bold = document.createElement('b');
                renderParameterTo(bold, parameter);
                title.appendChild(bold);

                const documentation = parameter.documentation;
                if (documentation && documentation.length) {
                    const section = createSection();
                    parent.appendChild(section);
                    const italic = document.createElement('i');
                    section.appendChild(italic);
                    bold = document.createElement('b');
                    bold.textContent = `${parameter.name}: `;
                    italic.appendChild(bold);
                    renderPartsTo(italic, parameter.documentation, false);
                }
            }
            else {
                renderParameterTo(title, signature.parameters[i++]);
            }
            if (i < parameterCount) {
                renderPartsTo(title, signature.separatorDisplayParts, false);
            }
            else {
                break;
            }
        }
    }
    renderPartsTo(title, signature.suffixDisplayParts, false);
}

function renderSignatureList(signatures: SignatureHelpItemsData) {
    const container = document.createElement("div");
    container.className = "signature-tooltip";

    let current = signatures.selectedItemIndex || 0;
    if (signatures.items.length > 1) {
        const title = document.createElement("div");
        title.className = "signature-title";
        container.appendChild(title);

        const nav = document.createElement("div");
        nav.className = "signature-nav";
        title.appendChild(nav);

        const left = document.createElement("button");
        left.className = "signature-nav-button left";
        left.innerHTML = CaretLeft16Filled;
        left.addEventListener("click", () => switchSignature(-1));
        if (current === 0) { left.disabled = true; }
        nav.appendChild(left);

        const indexer = document.createElement("span");
        indexer.textContent = `${current + 1} / ${signatures.items.length}`;
        nav.appendChild(indexer);

        const right = document.createElement("button");
        right.className = "signature-nav-button right";
        right.innerHTML = CaretRight16Filled;
        right.addEventListener("click", () => switchSignature(1));
        if (current === signatures.items.length - 1) { right.disabled = true; }
        nav.appendChild(right);

        const dot = document.createElement('i');
        dot.className = "signature-dot";
        dot.style.opacity = signatures.selectedItemIndex === current ? '1' : '0';
        title.appendChild(dot);

        title.addEventListener("wheel", event => {
            const delta = Math.abs(event.deltaY) >= Math.abs(event.deltaX) ? event.deltaY : event.deltaX;
            if (delta === 0) { return; }
            const direction = delta > 0 ? 1 : -1;
            if ((direction < 0 && current === 0) || (direction > 0 && current === signatures.items.length - 1)) { return; }
            event.preventDefault();
            switchSignature(direction);
        }, { passive: false });

        let pointerStart: { id: number, x: number, y: number } | undefined;
        container.addEventListener("pointerdown", event => {
            if (!event.isPrimary || getSelection()?.toString()
                || (event.pointerType !== "touch" && event.pointerType !== "pen")
                || (event.target instanceof Element && event.target.closest("button"))) {
                pointerStart = undefined;
                return;
            }
            pointerStart = { id: event.pointerId, x: event.clientX, y: event.clientY };
        });
        container.addEventListener("pointerup", event => {
            if (!pointerStart || event.pointerId !== pointerStart.id) { return; }
            const deltaX = event.clientX - pointerStart.x;
            const deltaY = event.clientY - pointerStart.y;
            pointerStart = undefined;
            const absDeltaX = Math.abs(deltaX);
            if (absDeltaX < 30 || absDeltaX <= Math.abs(deltaY)) { return; }
            switchSignature(deltaX < 0 ? 1 : -1);
        });
        container.addEventListener("pointercancel", () => pointerStart = undefined);

        function switchSignature(pos: -1 | 0 | 1) {
            const newIndex = current + pos;
            if (newIndex >= 0) {
                const length = signatures.items.length;
                if (newIndex < length) {
                    current = newIndex;
                    content.innerHTML = '';
                    indexer.textContent = `${current + 1} / ${length}`;
                    left.disabled = current === 0;
                    right.disabled = current === length - 1;
                    dot.style.opacity = signatures.selectedItemIndex === current ? '1' : '0';
                    renderSignatureTo(content, signatures.items[current], signatures.argumentIndex);
                }
            }
        }
    }

    const content = document.createElement("div");
    content.className = "signature-content";
    container.appendChild(content);

    renderSignatureTo(content, signatures.items[current], signatures.argumentIndex);

    return container;
}

export function signatureHelp(getSignatureHelpAsync: typeof dotnet.getSignatureHelpAsync) {
    return [
        signatureState,
        signaturePlugin.of(getSignatureHelpAsync)
    ] as const;
}