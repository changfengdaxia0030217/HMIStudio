import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const root = dirname(fileURLToPath(import.meta.url));

export default defineConfig({
  root,
  plugins: [react()],
  server: { host: "127.0.0.1", port: 5173, strictPort: true },
  build: {
    outDir: resolve(root, "../HMI.Server/wwwroot"),
    emptyOutDir: true,
  },
});
