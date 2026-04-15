import type { AppShellNavItem } from "../components/layout/AppShell";
import type { AdminUser, Role, TourRoute } from "../data/types";

const SUPER_ADMIN_NAV: AppShellNavItem[] = [
  { to: "/admin/dashboard", label: "Tổng quan", icon: "dashboard" },
  { to: "/admin/pois", label: "POI", icon: "map" },
  { to: "/admin/tours", label: "Tuyến tham quan", icon: "route" },
  { to: "/admin/users", label: "Chủ quán", icon: "users" },
  { to: "/admin/activity", label: "Nhật ký", icon: "activity" },
  { to: "/admin/settings", label: "Cài đặt", icon: "settings" },
];

const PLACE_OWNER_NAV: AppShellNavItem[] = [
  { to: "/restaurant/dashboard", label: "Tổng quan", icon: "dashboard" },
  { to: "/restaurant/pois", label: "POI", icon: "map" },
  { to: "/restaurant/tours", label: "Tuyến tham quan", icon: "route" },
  { to: "/restaurant/promotions", label: "Ưu đãi", icon: "gift" },
  { to: "/restaurant/activity", label: "Nhật ký", icon: "activity" },
  { to: "/restaurant/profile", label: "Hồ sơ", icon: "users" },
];

const ROLE_PATH_PREFIXES: Record<Role, string[]> = {
  SUPER_ADMIN: ["/", "/login", "/admin"],
  PLACE_OWNER: ["/", "/login", "/dashboard", "/restaurant"],
};

export const navigationItemsByRole: Record<Role, AppShellNavItem[]> = {
  SUPER_ADMIN: SUPER_ADMIN_NAV,
  PLACE_OWNER: PLACE_OWNER_NAV.filter((item) => item.to !== "/restaurant/tours"),
};

export const canManageOwnerAccounts = (role: Role | undefined) => role === "SUPER_ADMIN";

export const canManageSystemSettings = (role: Role | undefined) => role === "SUPER_ADMIN";

export const canManageRoute = (user: AdminUser | null, route: TourRoute) =>
  !!user &&
  (user.role === "SUPER_ADMIN" ||
    (!route.isSystemRoute && route.ownerUserId === user.id));

export const canEditFeaturedRoute = (role: Role | undefined) => role === "SUPER_ADMIN";

export const isPathAllowedForRole = (role: Role, path: string) => {
  const normalizedPath = path.split("?")[0]?.split("#")[0] ?? path;
  return ROLE_PATH_PREFIXES[role].some((prefix) =>
    prefix === "/"
      ? normalizedPath === "/"
      : normalizedPath === prefix || normalizedPath.startsWith(`${prefix}/`));
};
