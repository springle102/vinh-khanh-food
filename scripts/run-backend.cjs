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
const enableSpaProxy = process.env.VK_DISABLE_SPA_PROXY !== "1";
const defaultBackendUrls = "http://0.0.0.0:5080";
const resolvedBackendUrls =
  process.env.VK_BACKEND_URLS ??
  process.env.ASPNETCORE_URLS ??
  defaultBackendUrls;
const baseEnv = {
  ...process.env,
  DOTNET_CLI_HOME: dotnetHome,
  DOTNET_SKIP_FIRST_TIME_EXPERIENCE: "1",
  APPDATA: appData,
  NUGET_PACKAGES: nugetPackages,
};

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
  cleanupTrackedBackendProcess();
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
        DOTNET_WATCH_RESTART_ON_RUDE_EDIT: "1",
        DOTNET_WATCH_SUPPRESS_MSBUILD_INCREMENTALISM: "1",
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
