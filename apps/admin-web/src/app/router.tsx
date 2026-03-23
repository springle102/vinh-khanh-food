import { createBrowserRouter, Navigate } from "react-router-dom";
import { AppShell } from "../components/layout/AppShell";
import { ActivityPage } from "../features/activity/ActivityPage";
import { LoginPage } from "../features/auth/LoginPage";
import { RequireAuth } from "../features/auth/RequireAuth";
import { ContentPage } from "../features/content/ContentPage";
import { DashboardPage } from "../features/dashboard/DashboardPage";
import { PlacesPage } from "../features/places/PlacesPage";
import { PromotionsPage } from "../features/promotions/PromotionsPage";
import { QrRoutesPage } from "../features/qr/QrRoutesPage";
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
          { path: "places", element: <PlacesPage /> },
          { path: "content", element: <ContentPage /> },
          { path: "media", element: <Navigate to="/places" replace /> },
          { path: "qr-routes", element: <QrRoutesPage /> },
          { path: "users", element: <UsersPage /> },
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
