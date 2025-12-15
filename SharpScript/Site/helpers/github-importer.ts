import { Importer } from "sass";

export default {
  canonicalize(url) {
    return url.startsWith("github:") ? new URL(url) : null;
  },
  async load(canonicalUrl) {
    const { pathname, searchParams } = canonicalUrl;
    const branch = searchParams.get("branch") || "main";
    const path = searchParams.get("path") || '/index.css';
    try {
      return {
        contents: await fetch(`https://github.com/${pathname}/raw/refs/heads/${branch}${path}`).then(res => res.text()),
        syntax: (() => {
          switch (path.split('.').pop()) {
            case "scss":
              return "scss";
            case "sass":
              return "indented";
            case "css":
            default:
              return "css";
          }
        })()
      }
    }
    catch (e) {
      console.warn(`\nFailed to fetch '${canonicalUrl.href}': ${e}`);
      return {
        contents: `@import "https://cdn.jsdelivr.net/gh/${pathname}@${branch}${path}";`,
        syntax: "css"
      }
    }
  }
} as Importer<"async">;