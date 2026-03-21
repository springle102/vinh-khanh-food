import { Navigate, Outlet, useLocation } from "react-router-dom";
import { useAuth } from "./AuthContext";

export const RequireAuth = () => {
  const { isInitializing, user } = useAuth();
  const location = useLocation();

  if (isInitializing) {
    return (
      <main className="flex min-h-screen items-center justify-center bg-sand-50 px-4 py-10 text-ink-900">
        <div className="rounded-[2rem] border border-sand-100 bg-white px-8 py-10 text-center shadow-soft">
          <p className="text-sm font-semibold uppercase tracking-[0.25em] text-primary-600">
            Session
          </p>
          <h1 className="mt-3 text-2xl font-bold">Dang khoi phuc phien dang nhap</h1>
        </div>
      </main>
    );
  }

  if (!user) {
    return <Navigate to="/login" replace state={{ from: location.pathname }} />;
  }

  return <Outlet />;
};
