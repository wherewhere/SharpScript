import type { ManualChunksOption, OutputOptions } from "rollup";
import { extname } from "path";
import Mime from "mime";

function getType(path?: string) {
    if (path) {
        const type = Mime.getType(extname(path).toLowerCase());
        if (type) {
            const list = type.split('/');
            if (list.length) {
                return list[0];
            }
        }
    }
}

export default function getOutputOptions(mode: string, manualChunks?: ManualChunksOption): OutputOptions {
    const output: OutputOptions = { manualChunks };
    if (mode === "publish") {
        output.assetFileNames = (chunkInfo) => {
            const type = getType(chunkInfo.name);
            if (type && type !== "text") {
                return `assets/${type}/[name].[hash].[ext]`;
            }
            return "assets/[name].[hash].[ext]";
        };
        output.chunkFileNames = "assets/[name].[hash].js";
        output.entryFileNames = "assets/[name].[hash].js";
    }
    else {
        output.assetFileNames = (chunkInfo) => {
            const type = getType(chunkInfo.name);
            if (type && type !== "text") {
                return `assets/${type}/[name].[ext]`;
            }
            return "assets/[name].[ext]";
        };
        output.chunkFileNames = "assets/[name].js";
        output.entryFileNames = "assets/[name].js";
    }
    return output;
}