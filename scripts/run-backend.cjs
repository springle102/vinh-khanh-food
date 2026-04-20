const fs = require("node:fs");
const path = require("node:path");
const { spawn, spawnSync } = require("node:child_process");

const repoRoot = path.resolve(__dirname, "..");
const dotnetHome = path.join(repoRoot, ".dotnet-home");
const appData = path.join(dotnetHome, "AppData", "Roaming");
const nugetPackages = path.join(dotnetHome, ".nuget", "packages");
const buildOutput = path.join(repoRoot, ".artifacts", "backend-build");
const backendDevPidFile = path.join(repoRoot, ".artifacts", "backend-dev.pid.json");
const projectDir = path.join(repoRoot, "apps", "backend-api");
const projectPath = path.join(
  projectDir,
  "VinhKhanh.BackendApi.csproj"
);

for (const dir of [dotnetHome, appData, nugetPackages, buildOutput, path.dirname(backendDevPidFile)]) {
  fs.mkdirSync(dir, { recursive: true });
}

const command = process.argv[2] ?? "dev";
const dotnetCommand = process.platform === "win32" ? "dotnet.exe" : "dotnet";
const enableSpaProxy = command !== "serve" && process.env.VK_DISABLE_SPA_PROXY !== "1";
const defaultBackendUrls = "http://0.0.0.0:5080";
const resolvedBackendUrls =
  process.env.VK_BACKEND_URLS ??
  process.env.ASPNETCORE_URLS ??
  defaultBackendUrls;
const backendPorts = parseBackendPorts(resolvedBackendUrls);
const baseEnv = {
  ...process.env,
  DOTNET_CLI_HOME: dotnetHome,
  DOTNET_SKIP_FIRST_TIME_EXPERIENCE: "1",
  APPDATA: appData,
  NUGET_PACKAGES: nugetPackages,
};

function parseBackendPorts(urls) {
  return [...new Set(
    String(urls)
      .split(/[;,]/)
      .map((value) => value.trim())
      .filter(Boolean)
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

function isProcessAlive(pid) {
  if (!Number.isInteger(pid) || pid <= 0) {
    return false;
  }

  try {
    process.kill(pid, 0);
    return true;
  } catch {
    return false;
  }
}

function stopTree(pid) {
  if (!Number.isInteger(pid) || pid <= 0 || pid === process.pid) {
    return;
  }

  if (process.platform === "win32") {
    spawnSync("taskkill", ["/PID", String(pid), "/T", "/F"], {
      stdio: "ignore",
    });
    return;
  }

  try {
    process.kill(pid, "SIGTERM");
  } catch {
    // Process already exited.
  }
}

function getListeningProcessIds(port) {
  if (!Number.isInteger(port) || port <= 0) {
    return [];
  }

  if (process.platform === "win32") {
    const result = spawnSync("netstat", ["-ano", "-p", "tcp"], {
      encoding: "utf8",
    });

    if (result.status !== 0 || typeof result.stdout !== "string") {
      return [];
    }

    const matchingPids = new Set();
    const expectedPortSuffix = `:${port}`;

    for (const line of result.stdout.split(/\r?\n/)) {
      const parts = line.trim().split(/\s+/);
      if (parts.length < 5) {
        continue;
      }

      const protocol = parts[0].toUpperCase();
      const localAddress = parts[1];
      const state = parts[3].toUpperCase();
      const pid = Number.parseInt(parts[4], 10);

      if (protocol !== "TCP" || state !== "LISTENING" || !localAddress.endsWith(expectedPortSuffix)) {
        continue;
      }

      if (Number.isInteger(pid) && pid > 0) {
        matchingPids.add(pid);
      }
    }

    return [...matchingPids];
  }

  const result = spawnSync("lsof", ["-ti", `TCP:${port}`, "-sTCP:LISTEN"], {
    encoding: "utf8",
  });
  if (result.status !== 0 || typeof result.stdout !== "string") {
    return [];
  }

  return [...new Set(
    result.stdout
      .split(/\r?\n/)
      .map((value) => Number.parseInt(value.trim(), 10))
      .filter((value) => Number.isInteger(value) && value > 0)
  )];
}

function getProcessName(pid) {
  if (!Number.isInteger(pid) || pid <= 0) {
    return null;
  }

  if (process.platform === "win32") {
    const result = spawnSync("tasklist", ["/FI", `PID eq ${pid}`, "/FO", "CSV", "/NH"], {
      encoding: "utf8",
    });

    if (result.status !== 0 || typeof result.stdout !== "string") {
      return null;
    }

    const firstLine = result.stdout
      .split(/\r?\n/)
      .map((value) => value.trim())
      .find(Boolean);

    if (!firstLine || firstLine.startsWith("INFO:")) {
      return null;
    }

    return firstLine.replace(/^"|"$/g, "").split('","')[0] ?? null;
  }

  const result = spawnSync("ps", ["-p", String(pid), "-o", "comm="], {
    encoding: "utf8",
  });
  if (result.status !== 0 || typeof result.stdout !== "string") {
    return null;
  }

  const name = result.stdout.trim();
  return name || null;
}

function formatProcessLabel(pid) {
  const processName = getProcessName(pid);
  return processName ? `PID ${pid} (${processName})` : `PID ${pid}`;
}

function isSafeBackendProcessName(name) {
  if (!name) {
    return false;
  }

  const normalizedName = name.toLowerCase();
  return normalizedName === "dotnet.exe" ||
    normalizedName === "dotnet" ||
    normalizedName === "vinhkhanh.backendapi.exe";
}

function cleanupOccupiedBackendPorts() {
  for (const port of backendPorts) {
    const listeners = getListeningProcessIds(port).filter((pid) => pid !== process.pid);

    for (const pid of listeners) {
      const processName = getProcessName(pid);
      if (!isSafeBackendProcessName(processName)) {
        throw new Error(
          `[backend] Port ${port} is already in use by ${formatProcessLabel(pid)}. ` +
          `Stop that process or change VK_BACKEND_URLS before starting the backend.`
        );
      }

      console.log(`[backend] Releasing port ${port} from ${formatProcessLabel(pid)}...`);
      stopTree(pid);
    }

    const remainingListeners = getListeningProcessIds(port).filter((pid) => pid !== process.pid);
    if (remainingListeners.length > 0) {
      throw new Error(
        `[backend] Port ${port} is still in use by ${remainingListeners.map(formatProcessLabel).join(", ")} ` +
        `after cleanup. Please stop the conflicting process and try again.`
      );
    }
  }
}

function readTrackedBackendProcess() {
  try {
    const raw = fs.readFileSync(backendDevPidFile, "utf8");
    return JSON.parse(raw);
  } catch {
    return null;
  }
}

function clearTrackedBackendProcess(expectedPid) {
  const tracked = readTrackedBackendProcess();
  if (!tracked) {
    fs.rmSync(backendDevPidFile, { force: true });
    return;
  }

  if (expectedPid !== undefined && tracked.pid !== expectedPid) {
    return;
  }

  fs.rmSync(backendDevPidFile, { force: true });
}

function cleanupTrackedBackendProcess() {
  const tracked = readTrackedBackendProcess();
  if (!tracked || !Number.isInteger(tracked.pid)) {
    clearTrackedBackendProcess();
    return;
  }

  if (tracked.pid === process.pid || !isProcessAlive(tracked.pid)) {
    clearTrackedBackendProcess(tracked.pid);
    return;
  }

  console.log(`[backend] Stopping previous tracked backend process ${tracked.pid}...`);
  stopTree(tracked.pid);
  clearTrackedBackendProcess(tracked.pid);
}

if (command !== "build") {
  try {
    cleanupTrackedBackendProcess();
    cleanupOccupiedBackendPorts();
  } catch (error) {
    console.error(error instanceof Error ? error.message : String(error));
    process.exit(1);
  }
}

const args =
  command === "build"
    ? [
        "build",
        projectPath,
        `-p:OutDir=${buildOutput}${path.sep}`,
        "-p:AppendTargetFrameworkToOutputPath=true",
        "-p:UseAppHost=false",
      ]
    : command === "serve"
      ? [
          "run",
          "--project",
          projectPath,
          "--no-launch-profile",
          "--no-restore",
        ]
    : [
        "watch",
        "--project",
        projectPath,
        "--non-interactive",
        "run",
        "--no-launch-profile",
      ];

const child = spawn(dotnetCommand, args, {
  cwd: command === "build" ? repoRoot : projectDir,
  env: command === "build"
    ? baseEnv
    : {
        ...baseEnv,
        ASPNETCORE_ENVIRONMENT: "Development",
        ASPNETCORE_URLS: resolvedBackendUrls,
        ...(command === "serve"
          ? {}
          : {
              DOTNET_WATCH_RESTART_ON_RUDE_EDIT: "1",
              DOTNET_WATCH_SUPPRESS_MSBUILD_INCREMENTALISM: "1",
            }),
        ...(enableSpaProxy
          ? { ASPNETCORE_HOSTINGSTARTUPASSEMBLIES: "Microsoft.AspNetCore.SpaProxy" }
          : {}),
      },
  stdio: "inherit",
});

if (command !== "build" && Number.isInteger(child.pid)) {
  fs.writeFileSync(
    backendDevPidFile,
    JSON.stringify(
      {
        pid: child.pid,
        projectPath,
        urls: resolvedBackendUrls,
        startedAt: new Date().toISOString(),
      },
      null,
      2
    ),
    "utf8"
  );
}

child.on("exit", (code, signal) => {
  if (command !== "build") {
    clearTrackedBackendProcess(child.pid);
  }

  if (signal) {
    process.kill(process.pid, signal);
    return;
  }

  process.exit(code ?? 1);
});

process.on("exit", () => {
  if (command !== "build") {
    clearTrackedBackendProcess(child.pid);
  }
});
