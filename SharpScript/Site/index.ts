import {
    provideFluentDesignSystem,
    fluentButton,
    fluentOption,
    fluentProgressRing,
    fluentSelect,
    fluentTreeItem,
    fluentTreeView,
    baseLayerLuminance,
    StandardLuminance
} from "@fluentui/web-components";
provideFluentDesignSystem()
    .register(
        fluentButton(),
        fluentOption(),
        fluentProgressRing(),
        fluentSelect(),
        fluentTreeItem(),
        fluentTreeView()
    );

const scheme = matchMedia("(prefers-color-scheme: dark)");
if (typeof scheme !== "undefined") {
    scheme.addEventListener("change", e => baseLayerLuminance.withDefault(e.matches ? StandardLuminance.DarkMode : StandardLuminance.LightMode));
    if (scheme.matches) {
        baseLayerLuminance.withDefault(StandardLuminance.DarkMode);
    }
}

import { createApp } from "vue";
import { createHead } from "@unhead/vue/client";
import App from "./App.vue";
import i18n from "./i18n";

createApp(App).use(i18n).use(createHead()).mount("#vue-app");