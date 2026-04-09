const fs = require("node:fs");
const path = require("node:path");
const { spawn, spawnSync } = require("node:child_process");

const repoRoot = path.resolve(__dirname, "..");
const dotnetHome = path.join(repoRoot, ".dotnet-home");
const appData = path.join(dotnetHome, "AppData", "Roaming");
const nugetPackages = path.join(dotnetHome, ".nuget", "packages");
const buildOutput = path.join(repoRoot, ".artifacts", "backend-build");
const runtimeOutput = path.join(repoRoot, ".artifacts", "backend-run");
const projectDir = path.join(repoRoot, "apps", "backend-api");
const projectPath = path.join(
  projectDir,
  "VinhKhanh.BackendApi.csproj"
);

for (const dir of [dotnetHome, appData, nugetPackages, buildOutput, runtimeOutput]) {
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

if (command === "dev") {
  const buildArgs = [
    "build",
    projectPath,
    `-p:OutDir=${runtimeOutput}${path.sep}`,
    "-p:AppendTargetFrameworkToOutputPath=true",
    "-p:UseAppHost=false",
  ];

  const buildStep = spawnSync(dotnetCommand, buildArgs, {
    cwd: repoRoot,
    env: baseEnv,
    stdio: "inherit",
  });

  if (buildStep.status !== 0) {
    process.exit(buildStep.status ?? 1);
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
    : [path.join(runtimeOutput, "VinhKhanh.BackendApi.dll")];

const child = spawn(dotnetCommand, args, {
  cwd: command === "build" ? repoRoot : projectDir,
  env: command === "build"
    ? baseEnv
    : {
        ...baseEnv,
        ASPNETCORE_ENVIRONMENT: "Development",
        ASPNETCORE_URLS: resolvedBackendUrls,
        ...(enableSpaProxy
          ? { ASPNETCORE_HOSTINGSTARTUPASSEMBLIES: "Microsoft.AspNetCore.SpaProxy" }
          : {}),
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
