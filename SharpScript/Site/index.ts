import { createApp } from "vue";
import { head } from "./helpers/unhead";
import App from "./App.vue";
import i18n from "./i18n";

createApp(App).use(i18n).use(head).mount("#vue-app");