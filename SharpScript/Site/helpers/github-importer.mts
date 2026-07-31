import { Importer } from "sass";
import { createHash } from "crypto";
import { existsSync } from "fs";
import { mkdir, readFile, writeFile } from "fs/promises";
import { extname } from "path";

const contentCache = new Map<string, string | undefined>();

export default {
    canonicalize(url) {
        return url.startsWith("github:") ? new URL(url) : null;
    },
    async load(canonicalUrl) {
        const { pathname, searchParams } = canonicalUrl;
        const branch = searchParams.get("branch") || "main";
        const path = searchParams.get("path") || '/index.css';
        const url = `https://github.com/${pathname}/raw/refs/heads/${branch}${path}`;
        const hash = createHash("md5").update(url).digest("hex");
        const ext = extname(path);
        let contents = contentCache.get(hash);
        if (!contents) {
            if (!existsSync("node_modules/.vite/temp")) {
                await mkdir("node_modules/.vite/temp", { recursive: true });
            }
            const file = `node_modules/.vite/temp/${hash}${ext}`;
            if (existsSync(file)) {
                contents = await readFile(file, "utf-8");
            }
            else {
                try {
                    contents = await fetch(url).then(res => res.text());
                    if (contents) {
                        await writeFile(file, contents);
                    }
                }
                catch (e) {
                    console.warn(`\nFailed to fetch '${url}': ${e}`);
                    return {
                        contents: `@import "https://cdn.jsdelivr.net/gh/${pathname}@${branch}${path}";`,
                        syntax: "css"
                    }
                }
            }
            contentCache.set(hash, contents);
        }
        return {
            contents,
            syntax: (() => {
                switch (ext) {
                    case ".scss":
                        return "scss";
                    case ".sass":
                        return "indented";
                    case ".css":
                    default:
                        return "css";
                }
            })()
        }
    }
} as Importer<"async">;