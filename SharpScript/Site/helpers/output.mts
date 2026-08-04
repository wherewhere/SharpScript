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

export default function getOutputOptions(codeSplitting?: boolean | CodeSplittingOptions): OutputOptions {
    return {
        assetFileNames: chunkInfo => {
            const type = getType(chunkInfo.names);
            return type && type !== "text" ? `assets/${type}/[name]-[hash].[ext]` : "assets/[name]-[hash].[ext]";
        },
        chunkFileNames: chunkInfo =>
            chunkInfo.name === "worker" ? "assets/[name]-[hash].js" : "assets/js/[name]-[hash].js",
        codeSplitting
    };
}