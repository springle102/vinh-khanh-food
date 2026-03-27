import { createBrowserRouter, Navigate } from "react-router-dom";
import { AppShell, type AppShellNavItem } from "../components/layout/AppShell";
import { ActivityPage } from "../features/activity/ActivityPage";
import { useAuth } from "../features/auth/AuthContext";
import { getHomePathForRole } from "../features/auth/auth-routing";
import { LoginPage } from "../features/auth/LoginPage";
import { AuthLoadingScreen, RequireAuth } from "../features/auth/RequireAuth";
import { ContentPage } from "../features/content/ContentPage";
import { DashboardPage } from "../features/dashboard/DashboardPage";
import { EndUsersPage } from "../features/end-users/EndUsersPage";
import { PoisPage } from "../features/pois/PoisPage";
import { PromotionsPage } from "../features/promotions/PromotionsPage";
import { ReviewsPage } from "../features/reviews/ReviewsPage";
import { SettingsPage } from "../features/settings/SettingsPage";
import { UsersPage } from "../features/users/UsersPage";

const adminNavigationItems: AppShellNavItem[] = [
  { to: "/admin/dashboard", label: "Tổng quan", icon: "dashboard" },
  { to: "/admin/pois", label: "POI", icon: "map" },
  { to: "/admin/content", label: "Món ăn", icon: "content" },
  { to: "/admin/users", label: "Chủ quán", icon: "users" },
  { to: "/admin/end-users", label: "Người dùng", icon: "users" },
  { to: "/admin/promotions", label: "Ưu đãi", icon: "gift" },
  { to: "/admin/reviews", label: "Đánh giá", icon: "star" },
  { to: "/admin/activity", label: "Nhật ký", icon: "activity" },
  { to: "/admin/settings", label: "Cài đặt", icon: "settings" },
];

const restaurantNavigationItems: AppShellNavItem[] = adminNavigationItems.map((item) => ({
  ...item,
  to: item.to.replace("/admin", "/restaurant"),
}));

const AdminShellLayout = () => (
  <AppShell
    brandKicker="Vinh Khanh"
    brandTitle="Admin Console"
    headerEyebrow="Bảng điều khiển quản trị"
    headerTitle="Hệ thống vận hành nhà hàng"
    navigationItems={adminNavigationItems}
  />
);

const RestaurantShellLayout = () => (
  <AppShell
    brandKicker="Vinh Khanh"
    brandTitle="Admin Console"
    headerEyebrow="Bảng điều khiển quản trị"
    headerTitle="Hệ thống vận hành nhà hàng"
    navigationItems={restaurantNavigationItems}
  />
);

const RootRedirect = () => {
  const { isInitializing, user } = useAuth();

  if (isInitializing) {
    return <AuthLoadingScreen />;
  }

  return <Navigate to={user ? getHomePathForRole(user.role) : "/login"} replace />;
};

export const router = createBrowserRouter([
  {
    path: "/",
    element: <RootRedirect />,
  },
  {
    path: "/login",
    element: <LoginPage />,
  },
  {
    path: "/dashboard",
    element: <Navigate to="/restaurant/dashboard" replace />,
  },
  {
    path: "/admin/login",
    element: <Navigate to="/login" replace />,
  },
  {
    path: "/restaurant/login",
    element: <Navigate to="/login" replace />,
  },
  {
    path: "/admin",
    element: <RequireAuth allowedRoles={["SUPER_ADMIN"]} loginPath="/login" />,
    children: [
      {
        element: <AdminShellLayout />,
        children: [
          { index: true, element: <Navigate to="dashboard" replace /> },
          { path: "dashboard", element: <DashboardPage /> },
          { path: "pois", element: <PoisPage /> },
          { path: "content", element: <ContentPage /> },
          { path: "media", element: <Navigate to="/admin/pois" replace /> },
          { path: "users", element: <UsersPage /> },
          { path: "end-users", element: <EndUsersPage /> },
          { path: "promotions", element: <PromotionsPage /> },
          { path: "reviews", element: <ReviewsPage /> },
          { path: "analytics", element: <Navigate to="/admin/dashboard" replace /> },
          { path: "activity", element: <ActivityPage /> },
          { path: "settings", element: <SettingsPage /> },
        ],
      },
    ],
  },
  {
    path: "/restaurant",
    element: <RequireAuth allowedRoles={["PLACE_OWNER"]} loginPath="/login" />,
    children: [
      {
        element: <RestaurantShellLayout />,
        children: [
          { index: true, element: <Navigate to="dashboard" replace /> },
          { path: "dashboard", element: <DashboardPage /> },
          { path: "pois", element: <PoisPage /> },
          { path: "content", element: <ContentPage /> },
          { path: "media", element: <Navigate to="/restaurant/pois" replace /> },
          { path: "users", element: <UsersPage /> },
          { path: "end-users", element: <EndUsersPage /> },
          { path: "promotions", element: <PromotionsPage /> },
          { path: "reviews", element: <ReviewsPage /> },
          { path: "analytics", element: <Navigate to="/restaurant/dashboard" replace /> },
          { path: "activity", element: <ActivityPage /> },
          { path: "settings", element: <SettingsPage /> },
        ],
      },
    ],
  },
  {
    path: "*",
    element: <RootRedirect />,
  },
]);
