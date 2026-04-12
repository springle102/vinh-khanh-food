import { createBrowserRouter, Navigate, useParams } from "react-router-dom";
import { AppShell } from "../components/layout/AppShell";
import { ActivityPage } from "../features/activity/ActivityPage";
import { useAuth } from "../features/auth/AuthContext";
import { getHomePathForRole } from "../features/auth/auth-routing";
import { LoginPage } from "../features/auth/LoginPage";
import { PlaceOwnerRegistrationPage } from "../features/auth/PlaceOwnerRegistrationPage";
import { AuthLoadingScreen, RequireAuth } from "../features/auth/RequireAuth";
import { DashboardPage } from "../features/dashboard/DashboardPage";
import { OwnerRegistrationsPage } from "../features/owner-registrations/OwnerRegistrationsPage";
import { PoisPage } from "../features/pois/PoisPage";
import { PromotionsPage } from "../features/promotions/PromotionsPage";
import { ReviewsPage } from "../features/reviews/ReviewsPage";
import { SettingsPage } from "../features/settings/SettingsPage";
import { ToursPage } from "../features/tours/ToursPage";
import { UsersPage } from "../features/users/UsersPage";
import { navigationItemsByRole } from "../lib/rbac";

const AdminShellLayout = () => (
  <AppShell
    brandKicker="Vinh Khanh"
    brandTitle="Admin Console"
    headerEyebrow="Bảng điều khiển quản trị"
    headerTitle="Hệ thống vận hành nhà hàng"
    navigationItems={navigationItemsByRole.SUPER_ADMIN}
  />
);

const RestaurantShellLayout = () => (
  <AppShell
    brandKicker="Vinh Khanh"
    brandTitle="Admin Console"
    headerEyebrow="Bảng điều khiển quản trị"
    headerTitle="Hệ thống vận hành nhà hàng"
    navigationItems={navigationItemsByRole.PLACE_OWNER}
  />
);

const RootRedirect = () => {
  const { isInitializing, user } = useAuth();

  if (isInitializing) {
    return <AuthLoadingScreen />;
  }

  return <Navigate to={user ? getHomePathForRole(user.role) : "/login"} replace />;
};

const PoiEditRedirect = () => {
  const { isInitializing, user } = useAuth();
  const { poiId } = useParams();

  if (isInitializing) {
    return <AuthLoadingScreen />;
  }

  if (!user) {
    return <Navigate to="/login" replace />;
  }

  if (!poiId) {
    return <Navigate to={getHomePathForRole(user.role)} replace />;
  }

  if (user.role === "SUPER_ADMIN") {
    return <Navigate to={`/admin/pois?viewPoiId=${encodeURIComponent(poiId)}`} replace />;
  }

  return <Navigate to={`/restaurant/pois?editPoiId=${encodeURIComponent(poiId)}`} replace />;
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
    path: "/edit-poi/:poiId",
    element: <PoiEditRedirect />,
  },
  {
    path: "/register-owner",
    element: <PlaceOwnerRegistrationPage />,
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
          { path: "tours", element: <ToursPage /> },
          { path: "content", element: <Navigate to="/admin/pois" replace /> },
          { path: "media", element: <Navigate to="/admin/pois" replace /> },
          { path: "users", element: <UsersPage /> },
          { path: "owner-registrations", element: <OwnerRegistrationsPage /> },
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
          { path: "tours", element: <ToursPage /> },
          { path: "content", element: <Navigate to="/restaurant/pois" replace /> },
          { path: "media", element: <Navigate to="/restaurant/pois" replace /> },
          { path: "users", element: <Navigate to="/restaurant/profile" replace /> },
          { path: "promotions", element: <PromotionsPage /> },
          { path: "reviews", element: <ReviewsPage /> },
          { path: "analytics", element: <Navigate to="/restaurant/dashboard" replace /> },
          { path: "activity", element: <ActivityPage /> },
          { path: "profile", element: <UsersPage /> },
          { path: "settings", element: <Navigate to="/restaurant/profile" replace /> },
        ],
      },
    ],
  },
  {
    path: "*",
    element: <RootRedirect />,
  },
]);
