import AsyncLock from "async-lock";
export { AsyncLock };

import { expose, wrap, proxy } from "comlink";
export const Comlink = { expose, wrap, proxy };

export function importAsync(url: string) {
    return import(url);
}