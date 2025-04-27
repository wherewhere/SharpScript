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
let actions = [];
const dotnet = {
    init(baseURI, onDownloadResourceProgress) {
        document.baseURI = baseURI;
        document.documentElement.style.setProperty = (x, y) => onDownloadResourceProgress(x, y);
    },
    async startAsync() {
        importScripts("_framework/blazor.webassembly.js");
        Blazor = window.Blazor;
        DotNet = window.DotNet;
        await Blazor.start();
    },
    async invokeMethodAsync(assembly, method, ...args) {
        const result = await DotNet.invokeMethodAsync(assembly, method, ...args);
        if (method === "GetDiagnosticsAsync" && result instanceof Array) {
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
        return result;
    },
    async invokeActionAsync(index) {
        const action = actions[index];
        return await action.invokeMethodAsync("InvokeAsync");
    }
};
Comlink.expose(dotnet);