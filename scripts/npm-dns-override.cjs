const dns = require("node:dns");

const OVERRIDES = new Map([
  ["registry.npmjs.org", "104.16.4.34"],
]);

const originalLookup = dns.lookup.bind(dns);
const originalPromisesLookup = dns.promises.lookup.bind(dns.promises);

function normalizeArgs(options, callback) {
  if (typeof options === "function") {
    return [{}, options];
  }

  return [options ?? {}, callback];
}

function createResult(hostname, options) {
  const address = OVERRIDES.get(String(hostname).toLowerCase());

  if (!address) {
    return null;
  }

  const family = 4;

  if (options && typeof options === "object" && options.all) {
    return [{ address, family }];
  }

  return { address, family };
}

dns.lookup = function lookup(hostname, options, callback) {
  const [normalizedOptions, normalizedCallback] = normalizeArgs(options, callback);
  const override = createResult(hostname, normalizedOptions);

  if (!override) {
    return originalLookup(hostname, options, callback);
  }

  queueMicrotask(() => {
    if (Array.isArray(override)) {
      normalizedCallback(null, override);
      return;
    }

    normalizedCallback(null, override.address, override.family);
  });
};

dns.promises.lookup = async function lookup(hostname, options) {
  const override = createResult(hostname, options);
  return override ?? originalPromisesLookup(hostname, options);
};
