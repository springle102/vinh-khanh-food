import type { AdminUser } from "../../data/types";
import { Navigate, Outlet, useLocation } from "react-router-dom";
import { useAuth } from "./AuthContext";
import { getHomePathForRole } from "./auth-routing";

type RequireAuthProps = {
  allowedRoles: AdminUser["role"][];
  loginPath: string;
};

export const AuthLoadingScreen = () => (
  <main className="flex min-h-screen items-center justify-center bg-sand-50 px-4 py-10 text-ink-900">
    <div className="rounded-[2rem] border border-sand-100 bg-white px-8 py-10 text-center shadow-soft">
      <p className="text-sm font-semibold uppercase tracking-[0.25em] text-primary-600">Session</p>
      <h1 className="mt-3 text-2xl font-bold">Đang khôi phục phiên đăng nhập</h1>
    </div>
  </main>
);

export const RequireAuth = ({ allowedRoles, loginPath }: RequireAuthProps) => {
  const { isInitializing, user } = useAuth();
  const location = useLocation();

  if (isInitializing) {
    return <AuthLoadingScreen />;
  }

  if (!user) {
    return (
      <Navigate
        to={loginPath}
        replace
        state={{ from: `${location.pathname}${location.search}${location.hash}` }}
      />
    );
  }

  if (!allowedRoles.includes(user.role)) {
    return <Navigate to={getHomePathForRole(user.role)} replace />;
  }

  return <Outlet />;
};
