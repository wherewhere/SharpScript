import { defineConfig } from "vite";
import vue from "@vitejs/plugin-vue";
import svgLoader from "vite-svg-loader";
import { fileURLToPath } from "url";
import fs from "fs";
import path from "path";
import Mime from "mime";

export default defineConfig({
    base: "./",
    plugins: [
        vue({
            template: {
                compilerOptions: {
                    isCustomElement: tag => tag.includes('-')
                }
            }
        }),
        svgLoader(), {
            name: "dotnet-framework-static-files",
            configureServer(server) {
                const __dirname = path.dirname(fileURLToPath(import.meta.url));
                server.middlewares.use((req, res, next) => {
                    if (req.url && req.url.startsWith("/_framework")) {
                        const frameworkRoot = path.resolve(__dirname, "../bin/Release/net10.0-browser/publish/wwwroot");
                        const rawPath = decodeURIComponent(req.url.split('?')[0]);
                        let filePath = path.resolve(frameworkRoot, `.${rawPath}`);
                        fs.stat(filePath, (err, stat) => {
                            if (err) {
                                res.statusCode = 404;
                                return res.end();
                            }
                            function sendFile(filePath) {
                                const ext = path.extname(filePath).toLowerCase();
                                const mime = Mime.getType(ext) ?? "application/octet-stream";
                                res.setHeader("Content-Type", mime);
                                res.setHeader("Cache-Control", "no-cache");
                                const stream = fs.createReadStream(filePath);
                                stream.on("error", () => {
                                    res.statusCode = 500;
                                    return res.end();
                                });
                                stream.pipe(res);
                            }
                            if (stat.isDirectory()) {
                                filePath = path.join(filePath, "index.html");
                                fs.stat(filePath, (err, stat) => {
                                    if (err || !stat.isFile()) {
                                        res.statusCode = 404;
                                        return res.end();
                                    }
                                    sendFile(filePath);
                                });
                            }
                            sendFile(filePath);
                        });
                    }
                    else {
                        next();
                    }
                });
            }
        }
    ],
    css: {
        preprocessorOptions: {
            scss: {
                importers: [{
                    canonicalize(url) {
                        return url.startsWith("github:") ? new URL(url) : null;
                    },
                    async load(canonicalUrl) {
                        const { pathname, searchParams } = canonicalUrl;
                        const branch = searchParams.get("branch") || "main";
                        const path = searchParams.get("path") || '/index.css';
                        try {
                            return {
                                contents: await fetch(`https://github.com/${pathname}/raw/refs/heads/${branch}${path}`).then(res => res.text()),
                                syntax: (() => {
                                    switch (path.split('.').pop()) {
                                        case "scss":
                                            return "scss";
                                        case "sass":
                                            return "indented";
                                        case "css":
                                        default:
                                            return "css";
                                    }
                                })()
                            }
                        }
                        catch (e) {
                            console.warn(`\nFailed to fetch '${canonicalUrl.href}': ${e}`);
                            return {
                                contents: `@import "https://cdn.jsdelivr.net/gh/${pathname}@${branch}${path}";`,
                                syntax: "css"
                            }
                        }
                    }
                }]
            }
        }
    },
    build: {
        outDir: "../wwwroot",
        rollupOptions: {
            output: {
                assetFileNames: "assets/[name].[ext]",
                chunkFileNames: "assets/[name].js",
                entryFileNames: "assets/[name].js",
                manualChunks: {
                    "shared": ["/helpers/shared.ts"]
                }
            }
        },
        emptyOutDir: true,
        chunkSizeWarningLimit: 1024
    },
    worker: {
        format: "es"
    }
});