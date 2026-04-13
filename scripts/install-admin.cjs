const { spawnSync } = require("node:child_process");
const path = require("node:path");

const npmCache = path.join(process.cwd(), ".npm-cache");
const dnsOverride = path.join(process.cwd(), "scripts", "npm-dns-override.cjs");

function resolveNpmInvocation() {
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

const npm = resolveNpmInvocation();

const result = spawnSync(
  npm.command,
  [...npm.argsPrefix, "--prefix", "apps/admin-web", "ci"],
  {
    stdio: "inherit",
    env: {
      ...process.env,
      NODE_OPTIONS: `--require ${dnsOverride}`,
      npm_config_audit: "false",
      npm_config_cache: npmCache,
      npm_config_fund: "false",
      npm_config_registry: "https://registry.npmjs.org/",
    },
  },
);

if (result.error) {
  console.error(result.error);
}

if (typeof result.status === "number") {
  process.exit(result.status);
}

process.exit(1);
