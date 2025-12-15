import fs from "fs";
import path from "path";
import Mime from "mime";
import type { Plugin } from "vite";

export default {
    name: "dotnet-framework-static-files",
    configureServer(server) {
        server.middlewares.use((req, res, next) => {
            if (req.url && req.url.startsWith("/_framework")) {
                const frameworkRoot = path.resolve(__dirname, "../../bin/Release/net10.0-browser/publish/wwwroot");
                const rawPath = decodeURIComponent(req.url.split('?')[0]);
                let filePath = path.resolve(frameworkRoot, `.${rawPath}`);
                fs.stat(filePath, (err, stat) => {
                    if (err) {
                        res.statusCode = 404;
                        return res.end();
                    }
                    function sendFile(filePath: string) {
                        const ext = path.extname(filePath).toLowerCase();
                        const mime = Mime.getType(ext) ?? "application/octet-stream";
                        res.setHeader("Content-Type", mime);
                        res.setHeader("Cache-Control", "no-cache");
                        const stream = fs.createReadStream(filePath);
                        stream.on("error", () => {
                            res.statusCode = 500;
                            return res.end();
                        });
                        stream.pipe(res);
                    }
                    if (stat.isDirectory()) {
                        filePath = path.join(filePath, "index.html");
                        fs.stat(filePath, (err, stat) => {
                            if (err || !stat.isFile()) {
                                res.statusCode = 404;
                                return res.end();
                            }
                            sendFile(filePath);
                        });
                    }
                    sendFile(filePath);
                });
            }
            else {
                next();
            }
        });
    }
} as Plugin;