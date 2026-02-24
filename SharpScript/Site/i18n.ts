import { createI18n } from "vue-i18n";

export default createI18n({
    legacy: false,
    locale: (() => {
        const supportLanguages = ["en-US", "zh-CN"];
        const supportLanguageCodes =
            [
                ["en", "en-au", "en-ca", "en-gb", "en-ie", "en-in", "en-nz", "en-sg", "en-us", "en-za", "en-bz", "en-hk", "en-id", "en-jm", "en-kz", "en-mt", "en-my", "en-ph", "en-pk", "en-tt", "en-vn", "en-zw", "en-053", "en-021", "en-029", "en-011", "en-018", "en-014"],
                ["zh-Hans", "zh-cn", "zh-hans-cn", "zh-sg", "zh-hans-sg"]
            ];
        const fallbackLanguage = "en-US";
        const languages = navigator.languages || [navigator.language || fallbackLanguage];
        for (const lang of languages) {
            const temp = supportLanguageCodes.findIndex(codes => codes.some(x => x === lang.toLowerCase()));
            if (temp !== -1) {
                return supportLanguages[temp];
            }
        }
        return fallbackLanguage;
    })(),
    fallbackLocale: "en-US",
    messages: {
        "en-US": {
            description: ".NET language playground on WASM",
            input: {
                language: {
                    title: "Change Language",
                    placeholder: "Select a language",
                    script: "Script Mode"
                },
                process: {
                    title: "Process"
                },
                version: {
                    title: "Change Version",
                    placeholder: "Select a version"
                }
            },
            output: {
                language: {
                    title: "Change Output",
                    placeholder: "Select an output",
                    run: "Run",
                    syntaxTree: "Syntax Tree"
                },
                format: {
                    title: "Format (Shift+Alt+F)"
                },
                download: {
                    title: "Download Assembly"
                },
                linter: {
                    title: "Init Linter"
                },
                diagnostic: {
                    message: "Message",
                    location: "Location"
                },
                version: {
                    title: "Change Output Version",
                    placeholder: "Select a version"
                }
            },
            status: {
                alert: "Status",
                errors: "Errors: {0}",
                warnings: "Warnings: {0}"
            },
            message: {
                error: "Error: {0}",
                changingLanguage: "Changing language...",
                changingVersion: "Changing version...",
                changingOutput: "Changing output...",
                compiling: "Compiling...",
                initLinter: "Initializing linter...",
                loadingDotnet: "Loading .NET...",
                initWebWorker: "Initializing Web Worker...",
                downloadReferences: "Downloading references...",
                formatting: "Formatting Code..."
            }
        },
        "zh-CN": {
            description: ".NET 语言在线编译器",
            input: {
                language: {
                    title: "选择语言",
                    placeholder: "请选择语言",
                    script: "脚本模式"
                },
                process: {
                    title: "执行"
                },
                version: {
                    title: "选择版本",
                    placeholder: "请选择版本"
                }
            },
            output: {
                language: {
                    title: "选择输出",
                    placeholder: "请选择输出",
                    run: "运行",
                    syntaxTree: "语法树"
                },
                format: {
                    title: "格式化 (Shift+Alt+F)"
                },
                download: {
                    title: "下载程序集"
                },
                linter: {
                    title: "启动分析器"
                },
                diagnostic: {
                    message: "消息",
                    location: "位置"
                },
                version: {
                    title: "选择输出版本",
                    placeholder: "请选择版本"
                }
            },
            status: {
                alert: "状态",
                errors: "错误: {0}",
                warnings: "警告: {0}"
            },
            message: {
                error: "错误: {0}",
                changingLanguage: "正在切换语言...",
                changingVersion: "正在切换版本...",
                changingOutput: "正在切换输出...",
                compiling: "正在编译...",
                initLinter: "正在初始化分析器...",
                loadingDotnet: "正在加载 .NET...",
                initWebWorker: "正在初始化 Web Worker...",
                downloadReferences: "正在下载引用...",
                formatting: "正在格式化代码..."
            }
        }
    }
});