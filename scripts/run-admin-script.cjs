const { spawnSync } = require("node:child_process");
const path = require("node:path");

const workspaceDir = path.join(process.cwd(), "apps", "admin-web");
const requiredPackages = ["typescript/package.json", "vite/package.json"];

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

const [scriptName, ...extraArgs] = process.argv.slice(2);

if (!scriptName) {
  console.error("Missing admin-web npm script name.");
  process.exit(1);
}

if (!hasAdminDependencies()) {
  console.log("Admin web dependencies are missing. Running `npm run install:admin`...");

  const installResult = runNpm(["run", "install:admin"]);

  if (installResult.status !== 0) {
    exitWith(installResult);
  }
}

const separatorArgs = extraArgs.length > 0 ? ["--", ...extraArgs] : [];
const runResult = runNpm(["--prefix", "apps/admin-web", "run", scriptName, ...separatorArgs]);

exitWith(runResult);
