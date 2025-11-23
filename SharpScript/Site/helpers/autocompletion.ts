import type { Fingerprinting } from "../worker";

let assemblies: string[];

export async function getAssemblyAsync(fingerprinting: Fingerprinting | Promise<Fingerprinting>) {
    if (assemblies) {
        return assemblies;
    }
    else {
        const fingers = await fingerprinting;
        assemblies = [];
        for (const key in fingers) {
            const value = fingers[key];
            const assembly = value.substring(0, value.lastIndexOf("."));
            assemblies.push(assembly);
        }
        return assemblies;
    }
}