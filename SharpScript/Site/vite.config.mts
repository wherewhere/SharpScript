import { defineConfig } from "vite";
import vue from "@vitejs/plugin-vue";
import svgLoader from "vite-svg-loader";
import simpleHtmlPlugin from "vite-plugin-simple-html";
import dotnetFrameworkStaticFiles from "./helpers/dotnet-framework-static-files.mts";
import githubImporter from "./helpers/github-importer.mts";
import cssnano from "cssnano";
import getOutputOptions from "./helpers/output.mts";

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
        rolldownOptions: {
            checks: {
                pluginTimings: false
            },
            output: getOutputOptions({
                groups: [{
                    name: "shared",
                    test: /\/comlink\/|\/async-lock\/|vite\/preload-helper.js/
                }, {
                    name: "lezer",
                    test: "@lezer"
                }, {
                    name: "codemirror",
                    test: moduleId => (/\/@?codemirror\//.test(moduleId))
                        && !/\/@codemirror\/(legacy-modes\/mode\/|lang-)/.test(moduleId)
                }, {
                    name: "msil",
                    test: "codemirror-lang-msil"
                }, {
                    name: "csharp",
                    test: "@where/codemirror-lang-csharp"
                }, {
                    name: "vb",
                    test: "@codemirror/legacy-modes/mode/vb"
                }]
            })
        },
        emptyOutDir: true
    },
    worker: {
        format: "es"
    }
});