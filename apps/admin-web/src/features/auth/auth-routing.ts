import type { AdminUser } from "../../data/types";
import { isPathAllowedForRole as isPathAllowedForRoleByRbac } from "../../lib/rbac";

export type AuthPortal = "admin" | "restaurant";

const ROLE_PORTAL_MAP: Record<AdminUser["role"], AuthPortal> = {
  SUPER_ADMIN: "admin",
  PLACE_OWNER: "restaurant",
};

const HOME_PATH_BY_PORTAL: Record<AuthPortal, string> = {
  admin: "/admin/dashboard",
  restaurant: "/dashboard",
};

const CANONICAL_HOME_PATH_BY_PORTAL: Record<AuthPortal, string> = {
  admin: "/admin/dashboard",
  restaurant: "/restaurant/dashboard",
};

export const getPortalForRole = (role: AdminUser["role"]): AuthPortal => ROLE_PORTAL_MAP[role];

export const getHomePathForPortal = (portal: AuthPortal) => HOME_PATH_BY_PORTAL[portal];

export const getCanonicalHomePathForPortal = (portal: AuthPortal) =>
  CANONICAL_HOME_PATH_BY_PORTAL[portal];

export const getHomePathForRole = (role: AdminUser["role"]) =>
  getHomePathForPortal(getPortalForRole(role));

export const getCanonicalHomePathForRole = (role: AdminUser["role"]) =>
  getCanonicalHomePathForPortal(getPortalForRole(role));

export const isPathAllowedForRole = (role: AdminUser["role"], path: string) =>
  isPathAllowedForRoleByRbac(role, path);
