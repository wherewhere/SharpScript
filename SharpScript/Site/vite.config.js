import { defineConfig } from "vite";
import vue from "@vitejs/plugin-vue";
import legacy from "@vitejs/plugin-legacy";
import svgLoader from "vite-svg-loader";
import postcssPresetEnv from "postcss-preset-env";

export default defineConfig({
    base: "/",
    base: "./",
    plugins: [
        vue({
            include: [/\.vue$/, /\.md$/],
            template: {
                compilerOptions: {
                    isCustomElement: tag => tag.includes('-')
                }
            }
        }),
        legacy({
            targets: ["supports custom-elementsv1"],
            polyfills: false,
            renderLegacyChunks: false
        }),
        svgLoader()
    ],
    css: {
        postcss: {
            plugins: [postcssPresetEnv({
                stage: 0,
                browsers: ["supports custom-elementsv1"]
            })]
        },
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
        chunkSizeWarningLimit: 1024,
        outDir: "../wwwroot",
        emptyOutDir: true,
        rollupOptions: {
            output: {
                entryFileNames: "assets/[name].js",
                chunkFileNames: "assets/[name].js",
                assetFileNames: "assets/[name].[ext]",
                manualChunks: {
                    "shared": ["/helpers/shared.ts"]
                }
            }
        }
    },
    worker: {
        format: "es"
    }
});