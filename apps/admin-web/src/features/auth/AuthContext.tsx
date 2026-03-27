import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
  type PropsWithChildren,
} from "react";
import { useAdminData } from "../../data/store";
import type { AdminUser } from "../../data/types";
import { adminApi } from "../../lib/api";
import { getHomePathForRole, type AuthPortal } from "./auth-routing";

const SESSION_KEY = "vinh-khanh-admin-web:session";

type StoredSession = {
  userId: string;
  role: AdminUser["role"];
  accessToken: string;
  refreshToken: string;
  expiresAt: string;
};

type AuthContextValue = {
  user: AdminUser | null;
  isInitializing: boolean;
  login: (
    email: string,
    password: string,
    portal?: AuthPortal,
  ) => Promise<{ ok: boolean; message?: string; redirectTo?: string; role?: AdminUser["role"] }>;
  logout: () => Promise<void>;
};

const AuthContext = createContext<AuthContextValue | null>(null);

const readSession = (): StoredSession | null => {
  const rawValue = localStorage.getItem(SESSION_KEY);
  if (!rawValue) {
    return null;
  }

  try {
    const parsed = JSON.parse(rawValue) as Partial<StoredSession>;
    if (!parsed.userId || !parsed.refreshToken || !parsed.role) {
      localStorage.removeItem(SESSION_KEY);
      return null;
    }

    return {
      userId: parsed.userId,
      role: parsed.role,
      accessToken: parsed.accessToken ?? "",
      refreshToken: parsed.refreshToken,
      expiresAt: parsed.expiresAt ?? "",
    };
  } catch {
    localStorage.removeItem(SESSION_KEY);
    return null;
  }
};

const writeSession = (session: StoredSession) => {
  localStorage.setItem(SESSION_KEY, JSON.stringify(session));
};

const clearSession = () => {
  localStorage.removeItem(SESSION_KEY);
};

export const AuthProvider = ({ children }: PropsWithChildren) => {
  const { state, refreshData } = useAdminData();
  const [user, setUser] = useState<AdminUser | null>(null);
  const [isInitializing, setInitializing] = useState(true);

  const resolveUser = useCallback(
    (userId: string, role: AdminUser["role"], users: AdminUser[]) =>
      users.find(
        (item) =>
          item.id === userId &&
          item.role === role &&
          item.status === "active",
      ) ?? null,
    [],
  );

  useEffect(() => {
    if (!user) {
      return;
    }

    const nextUser = resolveUser(user.id, user.role, state.users);
    if (!nextUser) {
      clearSession();
      setUser(null);
      return;
    }

    setUser(nextUser);
  }, [resolveUser, state.users, user]);

  useEffect(() => {
    const session = readSession();
    if (!session) {
      setInitializing(false);
      return;
    }

    const restoreSession = async () => {
      try {
        const refreshedSession = await adminApi.refresh(session.refreshToken);
        writeSession({
          userId: refreshedSession.userId,
          role: refreshedSession.role,
          accessToken: refreshedSession.accessToken,
          refreshToken: refreshedSession.refreshToken,
          expiresAt: refreshedSession.expiresAt,
        });

        const nextState = await refreshData();
        const nextUser = resolveUser(refreshedSession.userId, refreshedSession.role, nextState.users);
        if (!nextUser) {
          clearSession();
          setUser(null);
          return;
        }

        setUser(nextUser);
      } catch {
        clearSession();
        setUser(null);
      } finally {
        setInitializing(false);
      }
    };

    void restoreSession();
  }, [refreshData, resolveUser]);

  const login = useCallback(
    async (email: string, password: string, portal?: AuthPortal) => {
      setInitializing(true);

      try {
        const session = await adminApi.login(email, password, portal);
        writeSession({
          userId: session.userId,
          role: session.role,
          accessToken: session.accessToken,
          refreshToken: session.refreshToken,
          expiresAt: session.expiresAt,
        });

        const nextState = await refreshData();
        const nextUser = resolveUser(session.userId, session.role, nextState.users);
        if (!nextUser) {
          clearSession();
          setUser(null);
          return { ok: false, message: "Đăng nhập thành công nhưng không tìm thấy tài khoản trong bootstrap." };
        }

        setUser(nextUser);
        return { ok: true, redirectTo: getHomePathForRole(nextUser.role), role: nextUser.role };
      } catch (error) {
        clearSession();
        setUser(null);
        return {
          ok: false,
          message: error instanceof Error ? error.message : "Đăng nhập không thành công.",
        };
      } finally {
        setInitializing(false);
      }
    },
    [refreshData, resolveUser],
  );

  const logout = useCallback(async () => {
    const session = readSession();
    clearSession();
    setUser(null);

    if (!session?.refreshToken) {
      return;
    }

    try {
      await adminApi.logout(session.refreshToken);
    } catch {
      return;
    }
  }, []);

  const value = useMemo(
    () => ({ user, isInitializing, login, logout }),
    [isInitializing, login, logout, user],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
};

export const useAuth = () => {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error("useAuth must be used within AuthProvider");
  }

  return context;
};
