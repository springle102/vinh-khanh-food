import type { AppShellNavItem } from "../components/layout/AppShell";
import type { AdminUser, Role, TourRoute } from "../data/types";

const SUPER_ADMIN_NAV: AppShellNavItem[] = [
  { to: "/admin/dashboard", label: "Tong quan", icon: "dashboard" },
  { to: "/admin/pois", label: "POI", icon: "map" },
  { to: "/admin/tours", label: "Tuyen tham quan", icon: "route" },
  { to: "/admin/users", label: "Chu quan", icon: "users" },
  { to: "/admin/promotions", label: "Uu dai", icon: "gift" },
  { to: "/admin/reviews", label: "Danh gia", icon: "star" },
  { to: "/admin/activity", label: "Nhat ky", icon: "activity" },
  { to: "/admin/settings", label: "Cai dat", icon: "settings" },
];

const PLACE_OWNER_NAV: AppShellNavItem[] = [
  { to: "/restaurant/dashboard", label: "Tong quan", icon: "dashboard" },
  { to: "/restaurant/pois", label: "POI", icon: "map" },
  { to: "/restaurant/tours", label: "Tuyen tham quan", icon: "route" },
  { to: "/restaurant/promotions", label: "Uu dai", icon: "gift" },
  { to: "/restaurant/reviews", label: "Danh gia", icon: "star" },
  { to: "/restaurant/activity", label: "Nhat ky", icon: "activity" },
  { to: "/restaurant/profile", label: "Ho so", icon: "users" },
];

const ROLE_PATH_PREFIXES: Record<Role, string[]> = {
  SUPER_ADMIN: ["/", "/login", "/admin"],
  PLACE_OWNER: ["/", "/login", "/dashboard", "/restaurant"],
};

export const navigationItemsByRole: Record<Role, AppShellNavItem[]> = {
  SUPER_ADMIN: SUPER_ADMIN_NAV,
  PLACE_OWNER: PLACE_OWNER_NAV,
};

export const canManageOwnerAccounts = (role: Role | undefined) => role === "SUPER_ADMIN";

export const canManageSystemSettings = (role: Role | undefined) => role === "SUPER_ADMIN";

export const canModerateReviews = (role: Role | undefined) => role === "SUPER_ADMIN";

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
