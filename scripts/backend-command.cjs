const { mkdirSync } = require("node:fs");
const { join } = require("node:path");
const { spawnSync } = require("node:child_process");

const mode = (process.argv[2] || "").trim().toLowerCase();
const project = "apps/backend-api/VinhKhanh.BackendApi.csproj";
const dotnetHome = join(process.cwd(), ".dotnet-home");
const buildOutput = join(process.cwd(), ".tmp-build", "backend-api", `${mode}-${Date.now()}`);
const builtDll = join(buildOutput, "VinhKhanh.BackendApi.dll");
const backendContentRoot = join(process.cwd(), "apps", "backend-api");
const powershellExe = "C:\\Windows\\System32\\WindowsPowerShell\\v1.0\\powershell.exe";

mkdirSync(dotnetHome, { recursive: true });
mkdirSync(buildOutput, { recursive: true });

const env = {
  ...process.env,
  DOTNET_CLI_HOME: dotnetHome,
  DOTNET_SKIP_FIRST_TIME_EXPERIENCE: "1",
  DOTNET_NOLOGO: "1",
  NUGET_PACKAGES: join(dotnetHome, ".nuget", "packages"),
  ASPNETCORE_ENVIRONMENT: "Development",
  ASPNETCORE_URLS: "http://localhost:5080",
};

const run = (args) => {
  const result = spawnSync("dotnet", args, {
    cwd: process.cwd(),
    env,
    stdio: "inherit",
    shell: true,
  });

  if (typeof result.status === "number" && result.status !== 0) {
    process.exit(result.status);
  }

  if (result.error) {
    console.error(result.error.message);
    process.exit(1);
  }
};

const getListeningPidFromNetstat = (port) => {
  const result = spawnSync(powershellExe, [
    "-NoProfile",
    "-Command",
    `netstat -ano -p tcp | Select-String ':${port}\\s+.*LISTENING\\s+(\\d+)' | Select-Object -First 1 | ForEach-Object { if ($_.Matches.Count -gt 0) { $_.Matches[0].Groups[1].Value } }`,
  ], {
    cwd: process.cwd(),
    env,
    encoding: "utf8",
  });

  if (result.status !== 0 || !result.stdout) {
    return null;
  }

  const pid = result.stdout.trim();
  return pid || null;
};

const getListeningPid = (port) => {
  const result = spawnSync(powershellExe, [
    "-NoProfile",
    "-Command",
    `(Get-NetTCPConnection -LocalPort ${port} -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty OwningProcess)`,
  ], {
    cwd: process.cwd(),
    env,
    encoding: "utf8",
  });

  if (result.status !== 0 || !result.stdout) {
    return getListeningPidFromNetstat(port);
  }

  const pid = result.stdout.trim();
  return pid || getListeningPidFromNetstat(port);
};

const waitForPortToBeFree = (port, retries = 20) => {
  for (let attempt = 0; attempt < retries; attempt += 1) {
    if (!getListeningPid(port)) {
      return true;
    }

    spawnSync(powershellExe, ["-NoProfile", "-Command", "Start-Sleep -Milliseconds 500"], {
      cwd: process.cwd(),
      env,
      stdio: "ignore",
    });
  }

  return !getListeningPid(port);
};

const stopListeningProcess = (port) => {
  const listeningPid = getListeningPid(port);
  if (!listeningPid) {
    return;
  }

  console.log(`Stopping existing process on port ${port} (PID ${listeningPid})...`);
  const result = spawnSync("taskkill", ["/PID", String(listeningPid), "/F"], {
    cwd: process.cwd(),
    env,
    stdio: "inherit",
  });

  if (typeof result.status === "number" && result.status !== 0) {
    process.exit(result.status);
  }

  if (result.error) {
    console.error(result.error.message);
    process.exit(1);
  }

  if (!waitForPortToBeFree(port)) {
    console.error(`Port ${port} is still in use after stopping PID ${listeningPid}.`);
    process.exit(1);
  }
};

const buildBackend = () =>
  run([
    "build",
    project,
    "--no-restore",
    "-p:UseAppHost=false",
    "-o",
    buildOutput,
  ]);

if (mode === "dev") {
  stopListeningProcess(5080);
  buildBackend();
  run([builtDll, "--contentRoot", backendContentRoot]);
  process.exit(0);
}

if (mode === "build") {
  buildBackend();
  process.exit(0);
}

console.error("Unsupported backend command. Use: dev or build");
process.exit(1);
