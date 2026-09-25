import { useScript } from "unhead";
import { createHead } from "@unhead/vue/client";

export const head = createHead();

export function useScriptAsync<T extends Record<symbol | string, any> = Record<symbol | string, any>>(_input: Parameters<typeof useScript<T>>[1], _options?: Parameters<typeof useScript<T>>[2]) {
    const { onError, onLoaded } = useScript<T>(head, _input, _options);
    return new Promise<T>((resolve, reject) => {
        onLoaded(resolve);
        onError(reject);
    });
}