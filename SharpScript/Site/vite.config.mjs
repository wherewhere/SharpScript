import { defineConfig } from "vite";
import vue from "@vitejs/plugin-vue";
import svgLoader from "vite-svg-loader";
import simpleHtmlPlugin from "vite-plugin-simple-html";
import dotnetFrameworkStaticFiles from "./helpers/dotnet-framework-static-files.mjs";
import githubImporter from "./helpers/github-importer.mjs";

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
        devSourcemap: true
    },
    build: {
        outDir: "../wwwroot",
        sourcemap: true,
        minify: "terser",
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