const fs = require("node:fs");
const path = require("node:path");
const { spawn, spawnSync } = require("node:child_process");
const {
  parseBackendPorts,
  resolveBackendListenerUrls,
  resolveBackendPrimaryUrl,
} = require("./backend-dev-config.cjs");

const repoRoot = path.resolve(__dirname, "..");
const dotnetHome = path.join(repoRoot, ".dotnet-home");
const appData = path.join(dotnetHome, "AppData", "Roaming");
const nugetPackages = path.join(dotnetHome, ".nuget", "packages");
const buildOutput = path.join(repoRoot, ".artifacts", "backend-build");
const backendDevRuntimeRoot = path.join(repoRoot, ".artifacts", "backend-dev");
const projectDir = path.join(repoRoot, "apps", "backend-api");
const coreProjectDir = path.join(repoRoot, "apps", "core");
const projectPath = path.join(
  projectDir,
  "VinhKhanh.BackendApi.csproj"
);
const publishOutput = path.join(projectDir, "publish");
const backendDevPidFile = path.join(repoRoot, ".artifacts", "backend-dev.pid.json");
const backendWatchRoots = [projectDir, coreProjectDir];
const watchedBackendExtensions = new Set([
  ".cs",
  ".csproj",
  ".json",
  ".props",
  ".targets",
  ".config",
  ".http",
]);
const backendRestartDebounceMs = Number.parseInt(
  process.env.VK_BACKEND_RESTART_DEBOUNCE_MS ?? "700",
  10
);

for (const dir of [dotnetHome, appData, nugetPackages, buildOutput, backendDevRuntimeRoot, path.dirname(backendDevPidFile)]) {
  fs.mkdirSync(dir, { recursive: true });
}

const command = process.argv[2] ?? "dev";
const dotnetCommand = process.platform === "win32" ? "dotnet.exe" : "dotnet";
const runModes = new Set(["dev", "serve"]);
const buildModes = new Set(["build", "publish"]);
const validCommands = new Set([...runModes, ...buildModes]);
const requiresBackendCleanup = runModes.has(command);

if (!validCommands.has(command)) {
  console.error(`[backend] Unsupported command "${command}". Expected one of: ${[...validCommands].join(", ")}.`);
  process.exit(1);
}

const enableSpaProxy = command !== "serve" && process.env.VK_DISABLE_SPA_PROXY !== "1";
const resolvedBackendUrls = resolveBackendListenerUrls({ repoRoot, env: process.env });
const primaryBackendUrl = resolveBackendPrimaryUrl({ repoRoot, env: process.env });
const backendPorts = parseBackendPorts(resolvedBackendUrls);
const baseEnv = {
  ...process.env,
  DOTNET_CLI_HOME: dotnetHome,
  DOTNET_SKIP_FIRST_TIME_EXPERIENCE: "1",
  APPDATA: appData,
  NUGET_PACKAGES: nugetPackages,
};
const backendRunSessionId = requiresBackendCleanup ? `${Date.now()}-${process.pid}` : null;
const backendRuntimeOutputDir = backendRunSessionId
  ? path.join(backendDevRuntimeRoot, backendRunSessionId)
  : null;

if (backendRuntimeOutputDir) {
  fs.mkdirSync(backendRuntimeOutputDir, { recursive: true });
}

function ensureTrailingPathSeparator(value) {
  return value.endsWith(path.sep) ? value : `${value}${path.sep}`;
}

function createBackendRunEnv() {
  return {
    ...baseEnv,
    ASPNETCORE_ENVIRONMENT: "Development",
    ASPNETCORE_URLS: resolvedBackendUrls,
    ...(enableSpaProxy
      ? { ASPNETCORE_HOSTINGSTARTUPASSEMBLIES: "Microsoft.AspNetCore.SpaProxy" }
      : {}),
  };
}

function writeTrackedBackendProcess(details = {}) {
  if (!requiresBackendCleanup) {
    return;
  }

  fs.writeFileSync(
    backendDevPidFile,
    JSON.stringify(
      {
        managerPid: process.pid,
        childPid: details.childPid ?? null,
        command,
        projectPath,
        urls: resolvedBackendUrls,
        outputDir: backendRuntimeOutputDir,
        startedAt: new Date().toISOString(),
      },
      null,
      2
    ),
    "utf8"
  );
}

function waitForChildExit(child, timeoutMs = 5000) {
  if (!child || child.exitCode !== null) {
    return Promise.resolve();
  }

  return new Promise((resolve) => {
    let settled = false;

    const finalize = () => {
      if (settled) {
        return;
      }

      settled = true;
      clearTimeout(timeoutHandle);
      child.removeListener("exit", onExit);
      resolve();
    };

    const onExit = () => finalize();
    const timeoutHandle = setTimeout(finalize, timeoutMs);

    child.once("exit", onExit);
  });
}

function isWithinDirectory(filePath, directoryPath) {
  const relativePath = path.relative(directoryPath, filePath);
  return relativePath === "" || (!relativePath.startsWith("..") && !path.isAbsolute(relativePath));
}

function shouldWatchBackendPath(filePath) {
  if (!filePath) {
    return false;
  }

  const normalizedPath = path.resolve(filePath);
  const extension = path.extname(normalizedPath).toLowerCase();

  if (!watchedBackendExtensions.has(extension)) {
    return false;
  }

  if (!backendWatchRoots.some((root) => isWithinDirectory(normalizedPath, root))) {
    return false;
  }

  const pathSegments = normalizedPath.split(path.sep).map((segment) => segment.toLowerCase());
  return !pathSegments.includes("bin") &&
    !pathSegments.includes("obj") &&
    !pathSegments.includes("publish");
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
          `Stop that process so the backend can start on the configured URL ${primaryBackendUrl}.`
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

function clearTrackedBackendProcess(expectedManagerPid) {
  const tracked = readTrackedBackendProcess();
  if (!tracked) {
    fs.rmSync(backendDevPidFile, { force: true });
    return;
  }

  if (expectedManagerPid !== undefined && tracked.managerPid !== expectedManagerPid) {
    return;
  }

  fs.rmSync(backendDevPidFile, { force: true });
}

function cleanupTrackedBackendProcess() {
  const tracked = readTrackedBackendProcess();
  if (!tracked) {
    clearTrackedBackendProcess();
    return;
  }

  const trackedPids = [...new Set(
    [tracked.managerPid, tracked.childPid]
      .filter((pid) => Number.isInteger(pid) && pid > 0 && pid !== process.pid)
  )];

  if (trackedPids.length === 0) {
    clearTrackedBackendProcess(tracked.managerPid);
    return;
  }

  let stoppedAnyProcess = false;

  for (const pid of trackedPids) {
    if (!isProcessAlive(pid)) {
      continue;
    }

    console.log(`[backend] Stopping previous tracked backend process ${formatProcessLabel(pid)}...`);
    stopTree(pid);
    stoppedAnyProcess = true;
  }

  if (!stoppedAnyProcess) {
    clearTrackedBackendProcess(tracked.managerPid);
    return;
  }

  clearTrackedBackendProcess(tracked.managerPid);
}

if (requiresBackendCleanup) {
  try {
    cleanupTrackedBackendProcess();
    cleanupOccupiedBackendPorts();
  } catch (error) {
    console.error(error instanceof Error ? error.message : String(error));
    process.exit(1);
  }
}

function buildOneShotArgs() {
  if (command === "build") {
    return [
      "build",
      projectPath,
      `-p:OutDir=${buildOutput}${path.sep}`,
      "-p:AppendTargetFrameworkToOutputPath=true",
      "-p:UseAppHost=false",
    ];
  }

  if (command === "publish") {
    return [
      "publish",
      projectPath,
      "-c",
      "Release",
      "-o",
      publishOutput,
      "-p:UseAppHost=false",
    ];
  }

  return [
    "run",
    "--project",
    projectPath,
    "--no-launch-profile",
    "--no-restore",
    `--property:OutDir=${ensureTrailingPathSeparator(backendRuntimeOutputDir)}`,
    "--property:UseAppHost=false",
  ];
}

function spawnBackendChild() {
  const child = spawn(dotnetCommand, buildOneShotArgs(), {
    cwd: command === "serve" ? projectDir : repoRoot,
    env: runModes.has(command) ? createBackendRunEnv() : baseEnv,
    stdio: "inherit",
  });

  if (requiresBackendCleanup) {
    writeTrackedBackendProcess({
      childPid: Number.isInteger(child.pid) ? child.pid : null,
    });
  }

  return child;
}

async function runBackendWatcher() {
  let activeChild = null;
  let isRestartInFlight = false;
  let queuedRestartReason = null;
  let restartTimer = null;
  const watchers = [];

  const closeWatchers = () => {
    for (const watcher of watchers) {
      watcher.close();
    }
  };

  const scheduleRestart = (reason) => {
    if (restartTimer) {
      clearTimeout(restartTimer);
    }

    restartTimer = setTimeout(() => {
      restartTimer = null;
      queuedRestartReason = reason;
      void restartBackendProcess();
    }, backendRestartDebounceMs);
  };

  const restartBackendProcess = async () => {
    if (isRestartInFlight || !queuedRestartReason) {
      return;
    }

    isRestartInFlight = true;

    while (queuedRestartReason) {
      const currentReason = queuedRestartReason;
      queuedRestartReason = null;

      if (activeChild && activeChild.exitCode === null) {
        console.log(`[backend] Restarting backend (${currentReason})...`);
        stopTree(activeChild.pid);
        await waitForChildExit(activeChild);
      }
      else {
        console.log(`[backend] Starting backend (${currentReason})...`);
      }

      try {
        cleanupOccupiedBackendPorts();
      } catch (error) {
        console.error(error instanceof Error ? error.message : String(error));
        continue;
      }

      const nextChild = spawnBackendChild();
      activeChild = nextChild;

      nextChild.on("exit", (code, signal) => {
        if (activeChild !== nextChild) {
          return;
        }

        activeChild = null;
        writeTrackedBackendProcess({ childPid: null });

        if (signal) {
          console.log(`[backend] Backend process stopped by signal ${signal}. Waiting for source changes to restart.`);
          return;
        }

        if ((code ?? 0) !== 0) {
          console.log(`[backend] Backend process exited with code ${code ?? 1}. Waiting for source changes to restart.`);
        }
      });

      nextChild.on("error", (error) => {
        console.error(`[backend] Failed to start backend process: ${error.message}`);
      });
    }

    isRestartInFlight = false;
  };

  const shutdownWatcher = async (exitCode) => {
    if (restartTimer) {
      clearTimeout(restartTimer);
      restartTimer = null;
    }

    closeWatchers();

    if (activeChild && activeChild.exitCode === null) {
      stopTree(activeChild.pid);
      await waitForChildExit(activeChild);
    }

    clearTrackedBackendProcess(process.pid);
    process.exit(exitCode);
  };

  for (const watchRoot of backendWatchRoots) {
    if (!fs.existsSync(watchRoot)) {
      continue;
    }

    const watcher = fs.watch(
      watchRoot,
      { recursive: process.platform === "win32" },
      (_eventType, fileName) => {
        if (!fileName) {
          scheduleRestart(`change under ${path.relative(repoRoot, watchRoot)}`);
          return;
        }

        const absolutePath = path.join(watchRoot, fileName.toString());
        if (!shouldWatchBackendPath(absolutePath)) {
          return;
        }

        scheduleRestart(`source change: ${path.relative(repoRoot, absolutePath)}`);
      }
    );

    watcher.on("error", (error) => {
      console.error(`[backend] File watcher error on ${watchRoot}: ${error.message}`);
    });

    watchers.push(watcher);
  }

  console.log(`[backend] Watching ${backendWatchRoots.map((root) => path.relative(repoRoot, root)).join(", ")}.`);
  console.log(`[backend] Using isolated dev output ${backendRuntimeOutputDir}.`);

  writeTrackedBackendProcess({ childPid: null });
  queuedRestartReason = "initial startup";
  await restartBackendProcess();

  process.on("SIGINT", () => {
    void shutdownWatcher(0);
  });

  process.on("SIGTERM", () => {
    void shutdownWatcher(0);
  });

  process.on("exit", () => {
    closeWatchers();
    if (activeChild && activeChild.exitCode === null) {
      stopTree(activeChild.pid);
    }

    clearTrackedBackendProcess(process.pid);
  });
}

if (command === "dev") {
  runBackendWatcher().catch((error) => {
    console.error(error instanceof Error ? error.message : String(error));
    clearTrackedBackendProcess(process.pid);
    process.exit(1);
  });
} else {
  const child = spawnBackendChild();

  child.on("exit", (code, signal) => {
    if (requiresBackendCleanup) {
      clearTrackedBackendProcess(process.pid);
    }

    if (signal) {
      process.kill(process.pid, signal);
      return;
    }

    process.exit(code ?? 1);
  });

  process.on("exit", () => {
    if (requiresBackendCleanup) {
      if (child.exitCode === null) {
        stopTree(child.pid);
      }

      clearTrackedBackendProcess(process.pid);
    }
  });
}
