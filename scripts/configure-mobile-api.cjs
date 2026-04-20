const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");

const repoRoot = path.resolve(__dirname, "..");
const settingsDir = path.join(repoRoot, ".android-settings");
const settingsPath = path.join(settingsDir, "appsettings.json");

function pickLanIp() {
  for (const addresses of Object.values(os.networkInterfaces())) {
    for (const address of addresses ?? []) {
      if (address.family === "IPv4" && !address.internal && !address.address.startsWith("169.254.")) {
        return address.address;
      }
    }
  }

  return "127.0.0.1";
}

const explicitBaseUrl =
  process.argv[2] ||
  process.env.VK_MOBILE_API_BASE_URL ||
  process.env.VK_PUBLIC_BACKEND_URL;
const lanBaseUrl = explicitBaseUrl || `http://${pickLanIp()}:5080`;

const appSettings = {
  ApiBaseUrl: lanBaseUrl,
  PlatformApiBaseUrls: {
    Android: lanBaseUrl,
    AndroidVirtual: "http://10.0.2.2:5080",
  },
  RoutingBaseUrl: "https://router.project-osrm.org/",
  RoutingProfile: "driving",
  UseOfflineFirst: true,
  EnableSync: true,
};

fs.mkdirSync(settingsDir, { recursive: true });
fs.writeFileSync(settingsPath, `${JSON.stringify(appSettings, null, 2)}\n`, "utf8");

console.log(`[mobile-api] Wrote ${path.relative(repoRoot, settingsPath)}`);
console.log(`[mobile-api] Real Android devices will use: ${lanBaseUrl}`);
console.log("[mobile-api] Android emulator will use: http://10.0.2.2:5080");
