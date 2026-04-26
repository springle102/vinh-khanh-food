const fs = require("node:fs");
const { spawnSync } = require("node:child_process");
const path = require("node:path");

const repoRoot = process.cwd();
const workspaceDir = path.join(process.cwd(), "apps", "admin-web");
const requiredPackages = ["typescript/package.json", "vite/package.json"];
const adminDevPidFile = path.join(repoRoot, ".artifacts", "admin-web-dev.pid.json");

function resolveNpmCommand() {
  if (process.env.npm_execpath) {
    return {
      command: process.execPath,
      argsPrefix: [process.env.npm_execpath],
    };
  }

  return {
    command: process.platform === "win32" ? "npm.cmd" : "npm",
    argsPrefix: [],
  };
}

function runNpm(args) {
  const npm = resolveNpmCommand();

  return spawnSync(npm.command, [...npm.argsPrefix, ...args], {
    stdio: "inherit",
    env: process.env,
  });
}

function hasAdminDependencies() {
  try {
    for (const packageName of requiredPackages) {
      require.resolve(packageName, { paths: [workspaceDir] });
    }

    return true;
  } catch {
    return false;
  }
}

function exitWith(result) {
  if (result.error) {
    console.error(result.error);
  }

  if (typeof result.status === "number") {
    process.exit(result.status);
  }

  process.exit(1);
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

function readTrackedAdminProcess() {
  try {
    return JSON.parse(fs.readFileSync(adminDevPidFile, "utf8"));
  } catch {
    return null;
  }
}

function writeTrackedAdminProcess() {
  fs.mkdirSync(path.dirname(adminDevPidFile), { recursive: true });
  fs.writeFileSync(
    adminDevPidFile,
    JSON.stringify(
      {
        managerPid: process.pid,
        startedAt: new Date().toISOString(),
      },
      null,
      2
    ),
    "utf8"
  );
}

function clearTrackedAdminProcess() {
  const tracked = readTrackedAdminProcess();
  if (tracked?.managerPid && tracked.managerPid !== process.pid) {
    return;
  }

  fs.rmSync(adminDevPidFile, { force: true });
}

function cleanupTrackedAdminProcess() {
  const tracked = readTrackedAdminProcess();
  if (!tracked?.managerPid || tracked.managerPid === process.pid) {
    clearTrackedAdminProcess();
    return;
  }

  if (isProcessAlive(tracked.managerPid)) {
    console.log(`[admin] Stopping previous tracked admin dev process PID ${tracked.managerPid}...`);
    stopTree(tracked.managerPid);
  }

  fs.rmSync(adminDevPidFile, { force: true });
}

const [scriptName, ...extraArgs] = process.argv.slice(2);

if (!scriptName) {
  console.error("Missing admin-web npm script name.");
  process.exit(1);
}

const resolvedScriptName = scriptName === "dev" ? "dev:client" : scriptName;
const isAdminDevCommand = resolvedScriptName === "dev:client";

if (!hasAdminDependencies()) {
  console.log("Admin web dependencies are missing. Running `npm run install:admin`...");

  const installResult = runNpm(["run", "install:admin"]);

  if (installResult.status !== 0) {
    exitWith(installResult);
  }
}

if (isAdminDevCommand) {
  cleanupTrackedAdminProcess();
  writeTrackedAdminProcess();

  process.on("SIGINT", () => {
    clearTrackedAdminProcess();
    process.exit(0);
  });

  process.on("SIGTERM", () => {
    clearTrackedAdminProcess();
    process.exit(0);
  });

  process.on("exit", () => {
    clearTrackedAdminProcess();
  });
}

const separatorArgs = extraArgs.length > 0 ? ["--", ...extraArgs] : [];
const runResult = runNpm(["--prefix", "apps/admin-web", "run", resolvedScriptName, ...separatorArgs]);

exitWith(runResult);
