const fs = require("node:fs");
const path = require("node:path");

const DEFAULT_BACKEND_URLS = "http://localhost:5080";
const WILDCARD_BACKEND_HOSTS = new Set(["0.0.0.0", "::", "[::]"]);

function getDefaultRepoRoot() {
  return path.resolve(__dirname, "..");
}

function normalizeBackendUrl(value) {
  const trimmed = String(value ?? "").trim();
  if (!trimmed) {
    return null;
  }

  try {
    const parsed = new URL(trimmed);
    if (parsed.protocol !== "http:" && parsed.protocol !== "https:") {
      return null;
    }

    parsed.hash = "";
    parsed.search = "";

    if (parsed.pathname === "/") {
      parsed.pathname = "";
    }

    return parsed.toString().replace(/\/$/, "");
  } catch {
    return null;
  }
}

function parseBackendUrls(value) {
  return [...new Set(
    String(value ?? "")
      .split(/[;,]/)
      .map((item) => normalizeBackendUrl(item))
      .filter(Boolean)
  )];
}

function readLaunchSettingsUrls(repoRoot = getDefaultRepoRoot()) {
  const launchSettingsPath = path.join(repoRoot, "apps", "backend-api", "Properties", "launchSettings.json");

  try {
    const rawValue = fs.readFileSync(launchSettingsPath, "utf8");
    const parsed = JSON.parse(rawValue);
    const profiles = Object.values(parsed?.profiles ?? {});
    const collectedUrls = [];

    for (const profile of profiles) {
      if (!profile || typeof profile !== "object") {
        continue;
      }

      collectedUrls.push(...parseBackendUrls(profile.applicationUrl));
    }

    return [...new Set(collectedUrls)];
  } catch {
    return [];
  }
}

function resolveBackendUrls(options = {}) {
  const repoRoot = options.repoRoot ?? getDefaultRepoRoot();
  const env = options.env ?? process.env;
  const explicitUrls = parseBackendUrls(env.VK_BACKEND_URLS || env.ASPNETCORE_URLS);

  if (explicitUrls.length > 0) {
    return explicitUrls.join(";");
  }

  const launchSettingsUrls = readLaunchSettingsUrls(repoRoot);
  if (launchSettingsUrls.length > 0) {
    return launchSettingsUrls.join(";");
  }

  return DEFAULT_BACKEND_URLS;
}

function resolveBackendPrimaryUrl(options = {}) {
  const urls = parseBackendUrls(resolveBackendUrls(options));
  if (urls.length === 0) {
    return DEFAULT_BACKEND_URLS;
  }

  return urls.find((url) => url.startsWith("http://")) ?? urls[0];
}

function parseBackendPorts(urls) {
  return [...new Set(
    parseBackendUrls(urls)
      .map((value) => {
        try {
          const parsed = new URL(value);
          if (parsed.port) {
            return Number.parseInt(parsed.port, 10);
          }

          return parsed.protocol === "https:" ? 443 : 80;
        } catch {
          return null;
        }
      })
      .filter((value) => Number.isInteger(value) && value > 0)
  )];
}

function isLoopbackHost(hostname) {
  return hostname === "localhost" ||
    WILDCARD_BACKEND_HOSTS.has(hostname) ||
    hostname === "127.0.0.1" ||
    hostname === "[::1]" ||
    hostname === "::1";
}

function toBackendListenerUrls(urls) {
  return parseBackendUrls(urls)
    .map((value) => {
      const parsed = new URL(value);
      if (isLoopbackHost(parsed.hostname)) {
        parsed.hostname = "0.0.0.0";
      }

      return parsed.toString().replace(/\/$/, "");
    })
    .join(";");
}

function resolveBackendListenerUrls(options = {}) {
  const env = options.env ?? process.env;
  const explicitUrls = parseBackendUrls(env.VK_BACKEND_URLS || env.ASPNETCORE_URLS);
  if (explicitUrls.length > 0) {
    return explicitUrls.join(";");
  }

  return toBackendListenerUrls(resolveBackendUrls(options));
}

function resolveBackendProbeTarget(options = {}) {
  const configuredUrl = resolveBackendPrimaryUrl(options);
  const parsed = new URL(configuredUrl);
  const probeHost = WILDCARD_BACKEND_HOSTS.has(parsed.hostname)
    ? "127.0.0.1"
    : parsed.hostname;
  const port = parsed.port
    ? Number.parseInt(parsed.port, 10)
    : parsed.protocol === "https:"
      ? 443
      : 80;

  return {
    configuredUrl,
    host: probeHost,
    port,
  };
}

module.exports = {
  DEFAULT_BACKEND_URLS,
  parseBackendPorts,
  parseBackendUrls,
  readLaunchSettingsUrls,
  resolveBackendListenerUrls,
  resolveBackendPrimaryUrl,
  resolveBackendProbeTarget,
  resolveBackendUrls,
};
