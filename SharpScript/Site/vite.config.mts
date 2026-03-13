import { defineConfig } from "vite";
import vue from "@vitejs/plugin-vue";
import svgLoader from "vite-svg-loader";
import simpleHtmlPlugin from "vite-plugin-simple-html";
import dotnetFrameworkStaticFiles from "./helpers/dotnet-framework-static-files";
import githubImporter from "./helpers/github-importer";
import cssnano from "cssnano";
import getOutputOptions from "./helpers/output";

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
            rolldownOptions: {
                output: getOutputOptions(mode, {
                    groups: [{
                        name: "shared",
                        test: "/helpers/shared.ts"
                    }]
                })
            },
            emptyOutDir: true,
            chunkSizeWarningLimit: 1024
        },
        worker: {
            format: "es"
        }
    };
});