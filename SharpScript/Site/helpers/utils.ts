export function setTimeoutAsync(timeout?: number) {
    return new Promise<void>((resolve) => setTimeout(resolve, timeout));
}