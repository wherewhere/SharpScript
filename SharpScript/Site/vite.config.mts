import { defineConfig } from "vite";
import vue from "@vitejs/plugin-vue";
import svgLoader from "vite-svg-loader";
import simpleHtmlPlugin from "vite-plugin-simple-html";
import dotnetFrameworkStaticFiles from "./helpers/dotnet-framework-static-files";
import githubImporter from "./helpers/github-importer";
import cssnano from "cssnano";

export default defineConfig(({ mode }) => {
    return {
        base: "./",
        plugins: [
            vue({
                template: {
                    compilerOptions: {
                        isCustomElement: tag => tag.includes('-')
                    }
                }
            }),
            svgLoader(),
            simpleHtmlPlugin({
                minify: {
                    minifyJs: true,
                    sortSpaceSeparatedAttributeValues: true,
                    sortAttributes: true,
                    tagOmission: false
                }
            }),
            dotnetFrameworkStaticFiles
        ],
        css: {
            preprocessorOptions: {
                scss: {
                    importers: [githubImporter]
                }
            },
            postcss: {
                plugins: [
                    cssnano({
                        preset: "advanced"
                    })
                ]
            },
            devSourcemap: true
        },
        build: {
            outDir: "../wwwroot",
            sourcemap: true,
            minify: "terser",
            rollupOptions: {
                output: mode === "publish" ? {
                    manualChunks: {
                        "shared": ["/helpers/shared.ts"]
                    }
                } : {
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
    };
});