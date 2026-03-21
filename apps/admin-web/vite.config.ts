import { defineConfig, loadEnv } from "vite";
import react from "@vitejs/plugin-react";

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), "");
  const apiTarget = env.VITE_API_PROXY_TARGET ?? "http://localhost:5080";

  return {
    plugins: [react()],
    build: {
      rollupOptions: {
        output: {
          manualChunks: {
            "react-vendor": ["react", "react-dom", "react-router-dom"],
            "chart-vendor": ["recharts"],
            "qr-vendor": ["qrcode"],
          },
        },
      },
    },
    server: {
      host: true,
      port: Number(env.VITE_PORT ?? 5173),
      proxy: {
        "/api": {
          target: apiTarget,
          changeOrigin: true,
        },
        "/storage": {
          target: apiTarget,
          changeOrigin: true,
        },
      },
    },
    preview: {
      host: true,
      port: Number(env.VITE_PREVIEW_PORT ?? 4173),
    },
  };
});
