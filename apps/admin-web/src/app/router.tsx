import { createBrowserRouter, Navigate } from "react-router-dom";
import { AppShell } from "../components/layout/AppShell";
import { ActivityPage } from "../features/activity/ActivityPage";
import { LoginPage } from "../features/auth/LoginPage";
import { RequireAuth } from "../features/auth/RequireAuth";
import { ContentPage } from "../features/content/ContentPage";
import { DashboardPage } from "../features/dashboard/DashboardPage";
import { EndUsersPage } from "../features/end-users/EndUsersPage";
import { PoisPage } from "../features/pois/PoisPage";
import { PromotionsPage } from "../features/promotions/PromotionsPage";
import { ReviewsPage } from "../features/reviews/ReviewsPage";
import { SettingsPage } from "../features/settings/SettingsPage";
import { UsersPage } from "../features/users/UsersPage";

export const router = createBrowserRouter([
  {
    path: "/login",
    element: <LoginPage />,
  },
  {
    element: <RequireAuth />,
    children: [
      {
        element: <AppShell />,
        children: [
          { index: true, element: <DashboardPage /> },
          { path: "pois", element: <PoisPage /> },
          { path: "content", element: <ContentPage /> },
          { path: "media", element: <Navigate to="/pois" replace /> },
          { path: "users", element: <UsersPage /> },
          { path: "end-users", element: <EndUsersPage /> },
          { path: "promotions", element: <PromotionsPage /> },
          { path: "reviews", element: <ReviewsPage /> },
          { path: "analytics", element: <Navigate to="/" replace /> },
          { path: "activity", element: <ActivityPage /> },
          { path: "settings", element: <SettingsPage /> },
        ],
      },
    ],
  },
  {
    path: "*",
    element: <Navigate to="/" replace />,
  },
]);
