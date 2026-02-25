import "../types";
import { useHead, useScript } from "@unhead/vue";
import { injectScript } from "@microsoft/clarity/src/utils";

declare global {
    interface Window {
        dataLayer: IArguments[];
        gtag: (key: string, value: any) => void;
        _hmt: { push: (args: string[]) => void };
    }
}

export function useAnalytics() {
    useHead({
        meta: [
            { name: "google-site-verification", content: "TPDbPzhpaqYCFwTabtkmyHbwnULZEHrAihJNMsmpsks" },
            { name: "msvalidate.01", content: "C979E9108FB600BD7D5A68A70678F7AD" },
            { name: "yandex-verification", content: "e15f6a5a8ad325cd" },
        ]
    });

    window.dataLayer = window.dataLayer || [];
    function gtag(key: string, value: any): void;
    function gtag() { window.dataLayer.push(arguments); }
    window.gtag = gtag;
    gtag("js", new Date());
    gtag("config", "G-6FDQ56LCDG");
    useScript({
        src: "https://www.googletagmanager.com/gtag/js?id=G-6FDQ56LCDG",
        async: true
    });

    injectScript("m7ua7korsz");

    window._hmt = window._hmt || [];
    useScript({
        src: "https://hm.baidu.com/hm.js?271f5d3abe652ce0122880579c66289e",
        async: true
    });
}