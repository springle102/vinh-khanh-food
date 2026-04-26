import { defineConfig, loadEnv } from "vite";
import type { ServerResponse } from "node:http";
import path from "node:path";
import { createRequire } from "node:module";
import react from "@vitejs/plugin-react";

const DEFAULT_API_BASE_URL = "/api/v1";
const ABSOLUTE_URL_PATTERN = /^[a-z]+:\/\//i;
const require = createRequire(import.meta.url);
const { resolveBackendPrimaryUrl } = require("../../scripts/backend-dev-config.cjs") as {
  resolveBackendPrimaryUrl: (options?: { repoRoot?: string; env?: Record<string, string | undefined> }) => string;
};

const normalizeEnvValue = (value: string | undefined) => value?.trim().replace(/\/+$/, "") ?? "";

const usesRelativeApiBaseUrl = (apiBaseUrl: string) =>
  !ABSOLUTE_URL_PATTERN.test(apiBaseUrl) && apiBaseUrl.startsWith("/");

const writeProxyErrorResponse = (
  response: ServerResponse,
  message: string,
) => {
  if (response.headersSent || response.writableEnded) {
    return;
  }

  response.statusCode = 503;
  response.setHeader("Content-Type", "application/json; charset=utf-8");
  response.end(JSON.stringify({
    success: false,
    data: null,
    message,
  }));
};

const createProxyEntry = (apiTarget: string) => ({
  target: apiTarget,
  changeOrigin: true,
  secure: apiTarget.startsWith("https://") ? false : undefined,
  configure: (proxy: {
    on: (event: string, listener: (...args: unknown[]) => void) => void;
  }) => {
    proxy.on("error", (error: Error, request: { method?: string; url?: string } | undefined, response: unknown) => {
      const requestLabel = `${request?.method ?? "GET"} ${request?.url ?? "/"}`;
      const message =
        `Không thể kết nối tới backend dev tại ${apiTarget}. ` +
        "Hãy chạy backend hoặc dùng `npm run dev` để bật cả backend và admin-web.";

      console.error(`[vite-proxy] ${requestLabel} -> ${apiTarget}`, error.message);

      if (response && typeof response === "object") {
        writeProxyErrorResponse(response as ServerResponse, message);
      }
    });
  },
});

const createProxyConfig = (apiTarget: string) => ({
  "/api": createProxyEntry(apiTarget),
  "/storage": createProxyEntry(apiTarget),
  "/swagger": createProxyEntry(apiTarget),
});

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), "");
  const apiBaseUrl = normalizeEnvValue(env.VITE_API_BASE_URL || DEFAULT_API_BASE_URL) || DEFAULT_API_BASE_URL;
  const devApiTarget =
    normalizeEnvValue(env.VITE_DEV_API_TARGET || env.VITE_API_PROXY_TARGET) ||
    resolveBackendPrimaryUrl({
      repoRoot: path.resolve(process.cwd(), "../.."),
      env: {
        ...process.env,
        ...env,
      },
    });
  const proxy = usesRelativeApiBaseUrl(apiBaseUrl) ? createProxyConfig(devApiTarget) : undefined;

  return {
    plugins: [react()],
    build: {
      rollupOptions: {
        output: {
          manualChunks: {
            "react-vendor": ["react", "react-dom", "react-router-dom"],
            "chart-vendor": ["recharts"],
          },
        },
      },
    },
    server: {
      host: true,
      port: Number(env.VITE_PORT ?? 5173),
      proxy,
    },
    preview: {
      host: true,
      port: Number(env.VITE_PREVIEW_PORT ?? 4173),
      proxy,
    },
  };
});
