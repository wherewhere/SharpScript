import { vscodeDarkInit, vscodeLightInit } from "@uiw/codemirror-theme-vscode";

export const vscodeDark = vscodeDarkInit({
    settings: {
        background: "#1E1E1E",
        foreground: "#CCCCCC",
        caret: "#AEAFAD",
        selection: "#264F78",
        selectionMatch: "#ADD6FF26",
        gutterForeground: "#6E7681",
        gutterActiveForeground: "#CCCCCC",
        gutterBorder: "#404040",
        fontFamily: "var(--font-monospace)"
    }
});
export const vscodeLight = vscodeLightInit({
    settings: {
        foreground: "#3B3B3B",
        selectionMatch: "#ADD6FF80",
        gutterForeground: "#6E7681",
        gutterActiveForeground: "#171184",
        gutterBorder: "#D3D3D3",
        fontFamily: "var(--font-monospace)"
    }
});