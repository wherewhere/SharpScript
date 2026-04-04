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
                checks: {
                    pluginTimings: false
                },
                output: getOutputOptions(mode, {
                    groups: [{
                        name: "shared",
                        test: "/helpers/shared.ts"
                    }, {
                        name: "lezer",
                        test: "@lezer"
                    }, {
                        name: "codemirror",
                        test: "/codemirror/"
                    }, {
                        name: "msil",
                        test: "codemirror-lang-msil"
                    }, {
                        name: "csharp",
                        test: "@replit/codemirror-lang-csharp"
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
    };
});