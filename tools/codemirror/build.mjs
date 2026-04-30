import * as esbuild from "esbuild";
import { fileURLToPath } from "node:url";
import path from "node:path";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const outDir = path.resolve(__dirname, "../../src/HermesDesktop/Assets/Wiki");

await esbuild.build({
    entryPoints: [path.join(__dirname, "entry.mjs")],
    bundle: true,
    minify: true,
    format: "iife",
    target: ["es2020"],
    outfile: path.join(outDir, "codemirror.bundle.js"),
    legalComments: "none",
});

console.log("Built", path.join(outDir, "codemirror.bundle.js"));
