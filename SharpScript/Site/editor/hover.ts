import { hoverTooltip } from "@codemirror/view";
import { mapTextTagsToType, renderPartsTo } from "../helpers/render-parts";
import type { dotnet } from "../worker";

export function createTooltip(getInfoTipAsync: typeof dotnet.getInfoTipAsync) {
    return hoverTooltip(async (_, pos) => {
        const tooltip = await getInfoTipAsync(pos);
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
    });
}