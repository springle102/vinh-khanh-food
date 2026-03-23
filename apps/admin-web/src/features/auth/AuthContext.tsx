import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
  type PropsWithChildren,
} from "react";
import { adminApi } from "../../lib/api";
import { useAdminData } from "../../data/store";
import type { AdminUser } from "../../data/types";

const SESSION_KEY = "vinh-khanh-admin-web:session";

type StoredSession = {
  userId: string;
  accessToken: string;
  refreshToken: string;
  expiresAt: string;
};

type AuthContextValue = {
  user: AdminUser | null;
  isInitializing: boolean;
  login: (email: string, password: string) => Promise<{ ok: boolean; message?: string }>;
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
    if (!parsed.userId || !parsed.refreshToken) {
      localStorage.removeItem(SESSION_KEY);
      return null;
    }

    return {
      userId: parsed.userId,
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
  const [isInitializing, setInitializing] = useState(false);

  useEffect(() => {
    if (!user) {
      return;
    }

    const nextUser = state.users.find((item) => item.id === user.id && item.status === "active") ?? null;
    if (!nextUser) {
      clearSession();
      setUser(null);
      return;
    }

    setUser(nextUser);
  }, [state.users, user]);

  const login = useCallback(
    async (email: string, password: string) => {
      setInitializing(true);

      try {
        const session = await adminApi.login(email, password);
        writeSession({
          userId: session.userId,
          accessToken: session.accessToken,
          refreshToken: session.refreshToken,
          expiresAt: session.expiresAt,
        });

        const nextState = await refreshData();
        const nextUser = nextState.users.find((item) => item.id === session.userId) ?? null;
        if (!nextUser) {
          clearSession();
          setUser(null);
          return { ok: false, message: "Đăng nhập thành công nhưng không tìm thấy tài khoản trong bootstrap." };
        }

        setUser(nextUser);
        return { ok: true };
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
    [refreshData],
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
