/// <reference types="node" />
import type { CodeSplittingOptions, OutputOptions } from "rolldown";
import { extname } from "path";
import Mime from "mime";

function getType(path: string[]) {
    if (path.length) {
        const type = Mime.getType(extname(path[0]).toLowerCase());
        if (type) {
            const list = type.split('/');
            return list[0];
        }
    }
}

export default function getOutputOptions(mode: string, codeSplitting?: boolean | CodeSplittingOptions): OutputOptions {
    const output: OutputOptions = { codeSplitting };
    if (mode === "publish") {
        output.assetFileNames = chunkInfo => {
            const type = getType(chunkInfo.names);
            return type && type !== "text" ? `assets/${type}/[name]-[hash].[ext]` : "assets/[name]-[hash].[ext]";
        };
        output.chunkFileNames = "assets/js/[name]-[hash].js";
    }
    else {
        output.assetFileNames = chunkInfo => {
            const type = getType(chunkInfo.names);
            return type && type !== "text" ? `assets/${type}/[name].[ext]` : "assets/[name].[ext]";
        };
        output.chunkFileNames = "assets/js/[name].js";
        output.entryFileNames = "assets/[name].js";
    }
    return output;
}