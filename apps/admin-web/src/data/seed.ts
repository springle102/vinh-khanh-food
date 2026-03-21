import seedSql from "./sql/admin-seed.sql?raw";
import type { AdminDataState } from "./types";

type SeedTableName = keyof AdminDataState;
type SeedPayloadMap = Partial<Record<SeedTableName, unknown>>;

const INSERT_PATTERN =
  /INSERT INTO seed_payloads\s*\(table_name,\s*payload_json\)\s*VALUES\s*\(\s*'([^']+)'\s*,\s*N'([\s\S]*?)'\s*\);/g;

const clonePayload = <T,>(value: T): T => JSON.parse(JSON.stringify(value)) as T;

const parseSeedSql = (sql: string): SeedPayloadMap => {
  const payloads: SeedPayloadMap = {};
  let match: RegExpExecArray | null;

  while ((match = INSERT_PATTERN.exec(sql)) !== null) {
    const tableName = match[1] as SeedTableName;
    const payloadJson = match[2].replace(/''/g, "'");
    payloads[tableName] = JSON.parse(payloadJson);
  }

  return payloads;
};

const parsedSeedPayloads = parseSeedSql(seedSql);

const requirePayload = <K extends SeedTableName>(key: K): AdminDataState[K] => {
  const payload = parsedSeedPayloads[key];

  if (payload === undefined) {
    throw new Error(`Missing SQL seed payload for "${key}".`);
  }

  return clonePayload(payload) as AdminDataState[K];
};

export const createSeedData = (): AdminDataState => ({
  users: requirePayload("users"),
  customerUsers: requirePayload("customerUsers"),
  categories: requirePayload("categories"),
  places: requirePayload("places"),
  foodItems: requirePayload("foodItems"),
  translations: requirePayload("translations"),
  audioGuides: requirePayload("audioGuides"),
  mediaAssets: requirePayload("mediaAssets"),
  qrCodes: requirePayload("qrCodes"),
  routes: requirePayload("routes"),
  promotions: requirePayload("promotions"),
  reviews: requirePayload("reviews"),
  viewLogs: requirePayload("viewLogs"),
  audioListenLogs: requirePayload("audioListenLogs"),
  auditLogs: requirePayload("auditLogs"),
  settings: requirePayload("settings"),
});
