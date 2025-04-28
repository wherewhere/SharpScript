importScripts("https://cdn.jsdelivr.net/npm/comlink/dist/umd/comlink.min.js");
const window = self;
const document = {
    baseURI: location.href,
    childNodes: [],
    documentElement: {
        style: {
            setProperty() { }
        }
    },
    addEventListener() { },
    createElement() {
        return {};
    },
    createElementNS() {
        return {};
    },
    hasChildNodes() {
        return false;
    }
};
const Node = { COMMENT_NODE: 8 };
const history = { state: {} };
let actions = [], descriptions = [];
const dotnet = {
    init(baseURI, onDownloadResourceProgress) {
        document.baseURI = baseURI;
        document.documentElement.style.setProperty = (x, y) => onDownloadResourceProgress(x, y);
    },
    async startAsync() {
        importScripts("_framework/blazor.webassembly.js");
        await Blazor.start();
    },
    async invokeMethodAsync(assembly, method, ...args) {
        const result = await DotNet.invokeMethodAsync(assembly, method, ...args);
        switch (method) {
            case "GetDiagnosticsAsync":
                if (result instanceof Array) {
                    actions.forEach(x => x.dispose());
                    actions = [];
                    result.forEach(diagnostic => {
                        diagnostic.actions = diagnostic.actions.map(x => {
                            actions.push(x.action);
                            return {
                                title: x.title,
                                action: actions.length - 1
                            }
                        });
                    });
                }
                break;
            case "GetCompletionsAsync":
                if (result instanceof Array) {
                    descriptions.forEach(x => x.dispose());
                    descriptions = [];
                    result.forEach(x => {
                        descriptions.push(x.description);
                        x.description = descriptions.length - 1;
                    });
                }
                break;
        }
        return result;
    },
    async invokeActionAsync(index) {
        const action = actions[index];
        return await action.invokeMethodAsync("InvokeAsync");
    },
    async getDescriptionAsync(index) {
        const description = descriptions[index];
        return await description.invokeMethodAsync("GetDescriptionAsync");
    }
};
Comlink.expose(dotnet);