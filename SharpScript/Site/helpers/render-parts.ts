import type { TaggedText } from "sharp-script";

function createSection() {
    const section = document.createElement("div");
    section.className = "mirrorsharp-parts-section";
    return section;
}

function renderPartTo(parent: HTMLElement, part: TaggedText) {
    const span = document.createElement("span");
    span.className = `tok-${part.tag.toLowerCase()}`;
    span.textContent = part.text;
    parent.appendChild(span);
}

export function renderPartsTo(parent: HTMLElement, parts: TaggedText[], splitLinesToSections: boolean) {
    let section = splitLinesToSections ? createSection() : parent;
    for (const part of parts) {
        if (part.tag === "linebreak" && splitLinesToSections) {
            parent.appendChild(section);
            section = createSection();
            continue;
        }
        renderPartTo(section, part);
    }
    if (splitLinesToSections) {
        parent.appendChild(section);
    }
}

export function renderParts(parts: TaggedText[], splitLinesToSections: boolean) {
    const container = document.createElement("div");
    renderPartsTo(container, parts, splitLinesToSections);
    return container;
}

export function mapTextTagsToType(tags: string[]) {
    switch (tags.length) {
        case 0: if (tags.length === 0)
            console.warn('No tag found for completion, falling back to "keyword".');
            return "keyword";
        case 1:
            return tags[0].toLowerCase();
        default:
            return `${tags[0].toLowerCase()}-${tags[1].toLowerCase()}`;
    }
}