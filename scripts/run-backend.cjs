const fs = require("node:fs");
const path = require("node:path");
const { spawn } = require("node:child_process");

const repoRoot = path.resolve(__dirname, "..");
const dotnetHome = path.join(repoRoot, ".dotnet-home");
const appData = path.join(dotnetHome, "AppData", "Roaming");
const nugetPackages = path.join(dotnetHome, ".nuget", "packages");
const projectPath = path.join(
  repoRoot,
  "apps",
  "backend-api",
  "VinhKhanh.BackendApi.csproj"
);

for (const dir of [dotnetHome, appData, nugetPackages]) {
  fs.mkdirSync(dir, { recursive: true });
}

const command = process.argv[2] ?? "dev";
const args =
  command === "build"
    ? ["build", projectPath]
    : ["run", "--project", projectPath];

const child = spawn(process.platform === "win32" ? "dotnet.exe" : "dotnet", args, {
  cwd: repoRoot,
  env: {
    ...process.env,
    DOTNET_CLI_HOME: dotnetHome,
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE: "1",
    APPDATA: appData,
    NUGET_PACKAGES: nugetPackages,
  },
  stdio: "inherit",
});

child.on("exit", (code, signal) => {
  if (signal) {
    process.kill(process.pid, signal);
    return;
  }

  process.exit(code ?? 1);
});
