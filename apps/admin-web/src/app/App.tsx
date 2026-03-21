import { RouterProvider } from "react-router-dom";
import { Button } from "../components/ui/Button";
import { AdminDataProvider } from "../data/store";
import { useAdminData } from "../data/store";
import { AuthProvider } from "../features/auth/AuthContext";
import { router } from "./router";

const AppReadyRouter = () => {
  const { bootstrapError, isBootstrapping, refreshData } = useAdminData();

  if (isBootstrapping) {
    return (
      <main className="flex min-h-screen items-center justify-center bg-sand-50 px-4 py-10 text-ink-900">
        <div className="max-w-lg rounded-[2rem] border border-sand-100 bg-white px-8 py-10 text-center shadow-soft">
          <p className="text-sm font-semibold uppercase tracking-[0.25em] text-primary-600">
            Backend bootstrap
          </p>
          <h1 className="mt-3 text-3xl font-bold">Dang tai du lieu tu backend</h1>
          <p className="mt-4 text-sm text-ink-500">
            Admin web se khoi dong sau khi nhan duoc bootstrap `/api/v1/bootstrap`.
          </p>
        </div>
      </main>
    );
  }

  if (bootstrapError) {
    return (
      <main className="flex min-h-screen items-center justify-center bg-sand-50 px-4 py-10 text-ink-900">
        <div className="max-w-lg rounded-[2rem] border border-rose-100 bg-white px-8 py-10 text-center shadow-soft">
          <p className="text-sm font-semibold uppercase tracking-[0.25em] text-rose-600">
            Khong ket noi duoc backend
          </p>
          <h1 className="mt-3 text-3xl font-bold">Admin web chua the khoi dong</h1>
          <p className="mt-4 text-sm text-ink-500">{bootstrapError}</p>
          <div className="mt-6 flex justify-center">
            <Button
              onClick={() => {
                void refreshData();
              }}
            >
              Thu tai lai bootstrap
            </Button>
          </div>
        </div>
      </main>
    );
  }

  return <RouterProvider router={router} />;
};

export const App = () => (
  <AdminDataProvider>
    <AuthProvider>
      <AppReadyRouter />
    </AuthProvider>
  </AdminDataProvider>
);
