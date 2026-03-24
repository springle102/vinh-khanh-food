const { spawnSync } = require("node:child_process");
const path = require("node:path");

const npmCache = path.join(process.cwd(), ".npm-cache");
const dnsOverride = path.join(process.cwd(), "scripts", "npm-dns-override.cjs");
const npmCli = process.env.npm_execpath;

const result = spawnSync(
  process.execPath,
  [npmCli, "--prefix", "apps/admin-web", "ci"],
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
