const { spawnSync } = require("node:child_process");

const target = (process.argv[2] || "").trim().toLowerCase();

const scriptMap = {
  admin: "dev:admin",
  backend: "dev:backend",
};

if (!target || !scriptMap[target]) {
  const supported = Object.keys(scriptMap).join(", ");
  console.error(`Missing or unsupported dev target. Use one of: ${supported}`);
  process.exit(1);
}

const result = spawnSync("npm", ["run", scriptMap[target]], {
  cwd: process.cwd(),
  stdio: "inherit",
  shell: true,
});

if (typeof result.status === "number") {
  process.exit(result.status);
}

process.exit(1);
