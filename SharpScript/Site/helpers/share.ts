/// <reference types="../toy.d.ts" />
import { useScriptAsync } from "./unhead";

const isToy = location.host === "www.bilibilitoy.com";

export async function shareCurrentUrlAsync() {
    if (isToy) {
        const toy = await useScriptAsync("https://s1.hdslb.com/bfs/seed/toy/app/sdk/toy-sdk.js", { use: () => window.toy });
        if (await toy.isSupport("share")) {
            return toy.share({ path: location.href.replace(document.baseURI, '') });
        }
    }
    else {
        if (!navigator.share) { await import("share-api-polyfill"); }
        return navigator.share({
            title: document.title,
            text: document.querySelector<HTMLMetaElement>('meta[name="description"]')?.content,
            url: location.href
        });
    }
}