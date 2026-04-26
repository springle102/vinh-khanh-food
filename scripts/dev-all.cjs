const { spawn, spawnSync } = require("node:child_process");
const fs = require("node:fs");
const net = require("node:net");
const path = require("node:path");
const readline = require("node:readline");
const { resolveBackendProbeTarget } = require("./backend-dev-config.cjs");

const repoRoot = path.resolve(__dirname, "..");
const isWindows = process.platform === "win32";
const nodeCommand = process.execPath;
const powershellCommand = isWindows ? "powershell.exe" : "pwsh";
const includeMobile =
  process.argv.includes("--with-mobile") ||
  process.env.VK_INCLUDE_MOBILE === "1";

const processes = [
  {
    name: "backend",
    color: "\x1b[36m",
    command: nodeCommand,
    args: [path.join(repoRoot, "scripts", "run-backend.cjs"), "dev"],
    required: true,
    env: {
      VK_DISABLE_SPA_PROXY: "1",
    },
  },
  {
    name: "admin",
    color: "\x1b[35m",
    command: nodeCommand,
    args: [path.join(repoRoot, "scripts", "run-admin-script.cjs"), "dev"],
    required: true,
  },
];

if (includeMobile) {
  processes.push({
    name: "mobile",
    color: "\x1b[33m",
    command: powershellCommand,
    args: isWindows
      ? ["-ExecutionPolicy", "Bypass", "-File", path.join(repoRoot, "scripts", "dev-mobile-android.ps1")]
      : ["-File", path.join(repoRoot, "scripts", "dev-mobile-android.ps1")],
    required: false,
  });
}

const resetColor = "\x1b[0m";
let shuttingDown = false;
let firstExitCode = 0;
const children = [];
const backendProbeTarget = resolveBackendProbeTarget({ repoRoot, env: process.env });
const backendTrackedPidFile = path.join(repoRoot, ".artifacts", "backend-dev.pid.json");
const adminProbeTarget = {
  host: "127.0.0.1",
  port: Number.parseInt(process.env.VITE_PORT ?? "5173", 10),
};
const backendStartupTimeoutMs = Number.parseInt(
  process.env.VK_BACKEND_STARTUP_TIMEOUT_MS ?? "120000",
  10
);
const adminStartupTimeoutMs = Number.parseInt(
  process.env.VK_ADMIN_STARTUP_TIMEOUT_MS ?? "120000",
  10
);

function delay(ms) {
  return new Promise((resolve) => {
    setTimeout(resolve, ms);
  });
}

function ignoreBrokenPipe(error) {
  if (error?.code === "EPIPE") {
    return;
  }

  throw error;
}

process.stdout.on("error", ignoreBrokenPipe);
process.stderr.on("error", ignoreBrokenPipe);

function createChildEnv(extraEnv = {}) {
  const mergedEnv = {
    ...process.env,
    ...extraEnv,
  };

  if (!isWindows) {
    return mergedEnv;
  }

  const pathKeys = Object.keys(mergedEnv).filter((key) => /^path$/i.test(key));
  if (pathKeys.length <= 1) {
    return mergedEnv;
  }

  const preferredPathValue =
    mergedEnv.Path ??
    mergedEnv.PATH ??
    pathKeys
      .map((key) => mergedEnv[key])
      .find((value) => typeof value === "string");

  for (const key of pathKeys) {
    delete mergedEnv[key];
  }

  if (typeof preferredPathValue === "string") {
    mergedEnv.Path = preferredPathValue;
  }

  return mergedEnv;
}

function prefixOutput(stream, name, color, source) {
  if (!stream) {
    return;
  }

  const rl = readline.createInterface({ input: stream });
  rl.on("line", (line) => {
    const tag = `${color}[${name}:${source}]${resetColor}`;
    process.stdout.write(`${tag} ${line}\n`);
  });
}

function stopTree(pid) {
  if (!pid) {
    return;
  }

  if (isWindows) {
    spawnSync("taskkill", ["/PID", String(pid), "/T", "/F"], {
      stdio: "ignore",
    });
    return;
  }

  try {
    process.kill(pid, "SIGTERM");
  }
  catch {
    // Process already exited.
  }
}

function getListeningProcessIds(port) {
  if (!Number.isInteger(port) || port <= 0) {
    return [];
  }

  if (isWindows) {
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

  if (isWindows) {
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

function isSafeAdminProcessName(name) {
  if (!name) {
    return false;
  }

  const normalizedName = name.toLowerCase();
  return normalizedName === "node.exe" ||
    normalizedName === "node" ||
    normalizedName === "npm.exe" ||
    normalizedName === "npm";
}

function cleanupOccupiedAdminPort() {
  const listeners = getListeningProcessIds(adminProbeTarget.port).filter((pid) => pid !== process.pid);

  for (const pid of listeners) {
    const processName = getProcessName(pid);
    if (!isSafeAdminProcessName(processName)) {
      throw new Error(
        `[admin] Port ${adminProbeTarget.port} is already in use by ${formatProcessLabel(pid)}. ` +
        "Stop that process so admin-web can start on the expected dev port."
      );
    }

    process.stdout.write(
      `${processes[1].color}[admin]${resetColor} releasing port ${adminProbeTarget.port} from ${formatProcessLabel(pid)}...\n`
    );
    stopTree(pid);
  }

  const remainingListeners = getListeningProcessIds(adminProbeTarget.port).filter((pid) => pid !== process.pid);
  if (remainingListeners.length > 0) {
    throw new Error(
      `[admin] Port ${adminProbeTarget.port} is still in use by ${remainingListeners.map(formatProcessLabel).join(", ")} after cleanup.`
    );
  }
}

function shutdown(code = 0) {
  if (shuttingDown) {
    return;
  }

  shuttingDown = true;
  firstExitCode = firstExitCode || code;

  for (const child of children) {
    stopTree(child.pid);
  }

  setTimeout(() => {
    process.exit(firstExitCode);
  }, 500);
}

function spawnProcess(proc) {
  let child;

  try {
    child = spawn(proc.command, proc.args, {
      cwd: repoRoot,
      env: createChildEnv(proc.env),
      stdio: ["ignore", "inherit", "inherit"],
    });
  }
  catch (error) {
    process.stderr.write(`${proc.color}[${proc.name}:err]${resetColor} ${error.message}\n`);
    if (proc.required === false) {
      return null;
    }

    shutdown(1);
    return null;
  }

  children.push(child);

  child.on("exit", (code, signal) => {
    const detail = signal ? `signal ${signal}` : `code ${code ?? 0}`;
    process.stdout.write(`${proc.color}[${proc.name}]${resetColor} exited with ${detail}\n`);

    if (!shuttingDown) {
      if (proc.required === false) {
        process.stdout.write(`${proc.color}[${proc.name}]${resetColor} optional process stopped; backend/admin will keep running.\n`);
        return;
      }

      shutdown(code ?? 0);
    }
  });

  child.on("error", (error) => {
    process.stderr.write(`${proc.color}[${proc.name}:err]${resetColor} ${error.message}\n`);
    if (proc.required === false) {
      return;
    }

    shutdown(1);
  });

  return child;
}

function tryConnectOnce(host, port) {
  return new Promise((resolve, reject) => {
    const socket = net.createConnection({ host, port });
    let settled = false;

    const finalize = (error) => {
      if (settled) {
        return;
      }

      settled = true;
      socket.destroy();

      if (error) {
        reject(error);
        return;
      }

      resolve();
    };

    socket.setTimeout(1000);
    socket.on("connect", () => finalize());
    socket.on("timeout", () => finalize(new Error(`Timed out connecting to ${host}:${port}.`)));
    socket.on("error", (error) => finalize(error));
  });
}

function readTrackedBackendProcess() {
  try {
    const raw = fs.readFileSync(backendTrackedPidFile, "utf8");
    return JSON.parse(raw);
  } catch {
    return null;
  }
}

function hasCurrentBackendSession(child) {
  const tracked = readTrackedBackendProcess();
  return tracked?.managerPid === child.pid &&
    Number.isInteger(tracked?.childPid) &&
    tracked.childPid > 0;
}

async function monitorBackendReady(child) {
  const { host, port } = backendProbeTarget;
  const startedAt = Date.now();
  let hasLoggedDelayWarning = false;
  let hasObservedCurrentBackendSession = false;

  process.stdout.write(
    `${processes[0].color}[backend]${resetColor} waiting for backend readiness on http://${host}:${port} before starting admin/mobile...\n`
  );

  while (!shuttingDown) {
    if (child.exitCode !== null) {
      return;
    }

    if (!hasObservedCurrentBackendSession) {
      hasObservedCurrentBackendSession = hasCurrentBackendSession(child);
      if (!hasObservedCurrentBackendSession) {
        if (!hasLoggedDelayWarning && Date.now() - startedAt >= backendStartupTimeoutMs) {
          hasLoggedDelayWarning = true;
          process.stderr.write(
            `${processes[0].color}[backend:err]${resetColor} backend manager has not registered the new backend session after ${backendStartupTimeoutMs}ms. Backend logs above should show the startup issue.\n`
          );
        }

        await delay(250);
        continue;
      }
    }

    try {
      await tryConnectOnce(host, port);
      process.stdout.write(
        `${processes[0].color}[backend]${resetColor} backend is ready on http://${host}:${port}.\n`
      );
      return;
    } catch (error) {
      if (!hasLoggedDelayWarning && Date.now() - startedAt >= backendStartupTimeoutMs) {
        hasLoggedDelayWarning = true;
        process.stderr.write(
          `${processes[0].color}[backend:err]${resetColor} backend has not opened http://${host}:${port} after ${backendStartupTimeoutMs}ms. Admin/mobile are still waiting; backend logs above should show the startup issue.\n`
        );
      }

      await delay(500);
    }
  }
}

async function monitorAdminReady(child) {
  const { host, port } = adminProbeTarget;
  const startedAt = Date.now();
  let hasLoggedDelayWarning = false;

  process.stdout.write(
    `${processes[1].color}[admin]${resetColor} waiting for admin dev server on http://${host}:${port} before starting mobile...\n`
  );

  while (!shuttingDown) {
    if (child.exitCode !== null) {
      return;
    }

    try {
      await tryConnectOnce(host, port);
      process.stdout.write(
        `${processes[1].color}[admin]${resetColor} admin dev server is ready on http://${host}:${port}.\n`
      );
      return;
    } catch (error) {
      if (!hasLoggedDelayWarning && Date.now() - startedAt >= adminStartupTimeoutMs) {
        hasLoggedDelayWarning = true;
        process.stderr.write(
          `${processes[1].color}[admin:err]${resetColor} admin dev server has not opened http://${host}:${port} after ${adminStartupTimeoutMs}ms. Admin logs above should show the startup issue.\n`
        );
      }

      await delay(500);
    }
  }
}

async function main() {
  const [backendProcess, adminProcess, ...optionalProcesses] = processes;
  const backendChild = spawnProcess(backendProcess);
  if (!backendChild) {
    return;
  }

  await monitorBackendReady(backendChild);
  if (shuttingDown || backendChild.exitCode !== null) {
    return;
  }

  cleanupOccupiedAdminPort();
  const adminChild = spawnProcess(adminProcess);
  if (!adminChild || shuttingDown) {
    return;
  }

  if (optionalProcesses.length === 0) {
    return;
  }

  await monitorAdminReady(adminChild);
  if (shuttingDown || adminChild.exitCode !== null) {
    return;
  }

  for (const proc of optionalProcesses) {
    if (!shuttingDown) {
      spawnProcess(proc);
    }
  }
}

main().catch((error) => {
  process.stderr.write(`[dev-all:err] ${error instanceof Error ? error.message : String(error)}\n`);
  shutdown(1);
});

process.on("SIGINT", () => shutdown(0));
process.on("SIGTERM", () => shutdown(0));
