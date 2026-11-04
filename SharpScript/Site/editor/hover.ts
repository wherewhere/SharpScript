import { hoverTooltip } from "@codemirror/view";
import { mapTextTagsToType, renderPartsTo } from "../helpers/render-parts";
import type { dotnet } from "../worker";

export function createTooltip(getInfoTipAsync: typeof dotnet.getInfoTipAsync, options?: Parameters<typeof hoverTooltip>[1]) {
    return hoverTooltip(async (_, pos) => {
        const tooltip = await getInfoTipAsync(pos);
        if (!tooltip) {return null;}
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
                    renderPartsTo(element, section.taggedParts, false);
                    dom.appendChild(element);
                });
                return { dom };
            }
        };
    }, options);
}