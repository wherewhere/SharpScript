import { createApp } from "vue";
import { createHead } from "@unhead/vue/client";
import App from "./App.vue";
import i18n from "./i18n";

createApp(App).use(i18n).use(createHead()).mount("#vue-app");