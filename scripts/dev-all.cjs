const { spawn, spawnSync } = require("node:child_process");
const path = require("node:path");
const readline = require("node:readline");

const repoRoot = path.resolve(__dirname, "..");
const isWindows = process.platform === "win32";
const shellCommand = isWindows ? (process.env.ComSpec || "cmd.exe") : "sh";
const powershellCommand = isWindows ? "powershell.exe" : "pwsh";

const processes = [
  {
    name: "backend",
    color: "\x1b[36m",
    command: isWindows ? shellCommand : "npm",
    args: isWindows ? ["/d", "/s", "/c", "npm run dev:backend"] : ["run", "dev:backend"],
    required: true,
    env: {
      VK_DISABLE_SPA_PROXY: "1",
    },
  },
  {
    name: "admin",
    color: "\x1b[35m",
    command: isWindows ? shellCommand : "npm",
    args: isWindows ? ["/d", "/s", "/c", "npm run dev"] : ["run", "dev"],
    required: true,
  },
  {
    name: "mobile",
    color: "\x1b[33m",
    command: powershellCommand,
    args: isWindows
      ? ["-ExecutionPolicy", "Bypass", "-File", path.join(repoRoot, "scripts", "dev-mobile-android.ps1")]
      : ["-File", path.join(repoRoot, "scripts", "dev-mobile-android.ps1")],
    required: false,
  },
];

const resetColor = "\x1b[0m";
let shuttingDown = false;
let firstExitCode = 0;
const children = [];

function prefixOutput(stream, name, color, source) {
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

for (const proc of processes) {
  let child;

  try {
    child = spawn(proc.command, proc.args, {
      cwd: repoRoot,
      env: {
        ...process.env,
        ...proc.env,
      },
      stdio: ["ignore", "pipe", "pipe"],
    });
  }
  catch (error) {
    process.stderr.write(`${proc.color}[${proc.name}:err]${resetColor} ${error.message}\n`);
    if (proc.required === false) {
      continue;
    }

    shutdown(1);
    break;
  }

  children.push(child);
  prefixOutput(child.stdout, proc.name, proc.color, "out");
  prefixOutput(child.stderr, proc.name, proc.color, "err");

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
}

process.on("SIGINT", () => shutdown(0));
process.on("SIGTERM", () => shutdown(0));
