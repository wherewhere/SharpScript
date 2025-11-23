import type { InfoTipItem } from "sharp-script";
import { mapTextTagsToType, renderPartsTo } from "./render-parts";

export function createTooltip(tooltip: InfoTipItem, pos: number) {
    return {
        pos,
        create() {
            const dom = document.createElement("div")
            dom.classList.add("mirrorsharp-infotip");
            tooltip.sections.forEach((section, index) => {
                const element = document.createElement("div");
                element.className = "mirrorsharp-parts-section";
                if (index === 0) {
                    const icon = document.createElement("span");
                    icon.classList.add("cm-completionIcon", `cm-completionIcon-${mapTextTagsToType(tooltip.tags)}`);
                    element.appendChild(icon);
                }
                renderPartsTo(element, section.parts, false);
                dom.appendChild(element);
            });
            return { dom };
        }
    };
}