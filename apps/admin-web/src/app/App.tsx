import { RouterProvider } from "react-router-dom";
import { AdminDataProvider } from "../data/store";
import { AuthProvider } from "../features/auth/AuthContext";
import { router } from "./router";

export const App = () => (
  <AdminDataProvider>
    <AuthProvider>
      <RouterProvider router={router} />
    </AuthProvider>
  </AdminDataProvider>
);
