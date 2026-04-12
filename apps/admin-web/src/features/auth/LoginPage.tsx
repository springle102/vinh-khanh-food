import { useEffect, useRef, useState } from "react";
import { Link, useLocation, useNavigate } from "react-router-dom";
import { ApiError, adminApi, getErrorMessage, type LoginAccountOption } from "../../lib/api";
import { Button } from "../../components/ui/Button";
import { Card } from "../../components/ui/Card";
import { Icon } from "../../components/ui/Icons";
import { Input } from "../../components/ui/Input";
import { useAuth } from "./AuthContext";
import { getHomePathForRole, isPathAllowedForRole } from "./auth-routing";

const getAccountRoleLabel = (role: LoginAccountOption["role"]) =>
  role === "SUPER_ADMIN" ? "Super Admin" : "Chủ quán";

const shouldRetryLoginOptions = (error: unknown) =>
  error instanceof ApiError &&
  (error.kind === "network" ||
    error.kind === "invalid_response" ||
    [404, 502, 503, 504].includes(error.status));

const wait = (milliseconds: number) =>
  new Promise((resolve) => window.setTimeout(resolve, milliseconds));

export const LoginPage = () => {
  const navigate = useNavigate();
  const location = useLocation();
  const { isInitializing, login, user } = useAuth();
  const [loginAccounts, setLoginAccounts] = useState<LoginAccountOption[]>([]);
  const [isLoadingAccounts, setLoadingAccounts] = useState(true);
  const [accountsError, setAccountsError] = useState("");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const emailInputRef = useRef<HTMLInputElement | null>(null);
  const passwordInputRef = useRef<HTMLInputElement | null>(null);

  const applyAccountCredentials = (account: Pick<LoginAccountOption, "email" | "password">) => {
    setEmail(account.email);
    setPassword(account.password);
    setError("");
  };

  useEffect(() => {
    if (!user) {
      return;
    }

    navigate(getHomePathForRole(user.role), { replace: true });
  }, [navigate, user]);

  useEffect(() => {
    let isMounted = true;

    const loadLoginAccounts = async () => {
      setLoadingAccounts(true);
      setAccountsError("");

      try {
        for (let attempt = 1; attempt <= 8; attempt += 1) {
          try {
            const nextAccounts = await adminApi.getLoginOptions();
            if (!isMounted) {
              return;
            }

            setLoginAccounts(nextAccounts);
            setAccountsError("");
            return;
          } catch (nextError) {
            if (!isMounted) {
              return;
            }

            if (!shouldRetryLoginOptions(nextError) || attempt === 8) {
              setAccountsError(getErrorMessage(nextError));
              return;
            }

            await wait(Math.min(2500, 400 * attempt));
          }
        }
      } finally {
        if (isMounted) {
          setLoadingAccounts(false);
        }
      }
    };

    void loadLoginAccounts();

    return () => {
      isMounted = false;
    };
  }, []);

  const onSubmit = async (event: React.FormEvent) => {
    event.preventDefault();
    setSubmitting(true);
    setError("");

    const result = await login(email, password);
    setSubmitting(false);

    if (!result.ok) {
      setError(result.message ?? "Đăng nhập không thành công.");
      return;
    }

    const requestedPath = (location.state as { from?: string } | null)?.from;
    const redirectTo =
      result.role && requestedPath && isPathAllowedForRole(result.role, requestedPath)
        ? requestedPath
        : result.redirectTo;

    navigate(redirectTo ?? "/dashboard", { replace: true });
  };

  const handleSelectDatabaseAccount = (account: LoginAccountOption) => {
    applyAccountCredentials(account);
  };

  return (
    <main className="flex min-h-screen items-center justify-center bg-sand-50 bg-hero-warm px-4 py-10 text-ink-900">
      <div className="w-full max-w-[540px]">
        <Card className="p-8 sm:p-10">
          <div className="mb-8 flex items-center gap-4">
            <div className="flex h-14 w-14 items-center justify-center rounded-3xl bg-primary-500 text-white">
              <Icon name="dashboard" className="h-7 w-7" />
            </div>
            <div>
              <p className="text-sm font-semibold uppercase tracking-[0.2em] text-primary-600">
                Cổng đăng nhập
              </p>
              <h1 className="text-2xl font-bold">Đăng nhập hệ thống nhà hàng</h1>
            </div>
          </div>

          <form className="space-y-5" onSubmit={onSubmit} autoComplete="off">
            <div>
              <label className="field-label">Email</label>
              <Input
                ref={emailInputRef}
                type="email"
                name="email"
                autoComplete="off"
                value={email}
                onChange={(event) => setEmail(event.target.value)}
                placeholder="Nhập email"
              />
            </div>
            <div>
              <label className="field-label">Mật khẩu</label>
              <Input
                ref={passwordInputRef}
                type="password"
                name="password"
                autoComplete="new-password"
                value={password}
                onChange={(event) => setPassword(event.target.value)}
                placeholder="Nhập mật khẩu"
              />
            </div>

            {error ? (
              <div className="rounded-2xl bg-rose-50 px-4 py-3 text-sm text-rose-700">{error}</div>
            ) : null}

            <Button type="submit" className="w-full" disabled={submitting || isInitializing}>
              {submitting || isInitializing ? "Đang đăng nhập..." : "Đăng nhập"}
            </Button>
            <div className="flex justify-end">
              <Link
                to={email ? `/register-owner?email=${encodeURIComponent(email)}` : "/register-owner"}
                className="text-sm font-medium text-primary-700 transition hover:text-primary-800"
              >
                Đăng ký chủ quán
              </Link>
            </div>
          </form>

          <div className="mt-6 rounded-3xl bg-sand-50 p-4 text-sm text-ink-500">
            <p className="font-semibold text-ink-700">Tài khoản trong database</p>
            <p className="mt-1 text-xs text-ink-500">
              Danh sách này được lấy từ bảng AdminUsers. Nhấn vào để điền sẵn email và mật khẩu.
            </p>

            {accountsError ? (
              <div className="mt-3 rounded-2xl bg-rose-50 px-4 py-3 text-sm text-rose-700">
                {accountsError}
              </div>
            ) : null}

            <div className="mt-3 grid gap-3">
              {isLoadingAccounts ? (
                <div className="rounded-2xl border border-dashed border-sand-200 px-4 py-3 text-sm text-ink-500">
                  Đang tải tài khoản từ database...
                </div>
              ) : null}

              {!isLoadingAccounts && !accountsError && loginAccounts.length === 0 ? (
                <div className="rounded-2xl border border-dashed border-sand-200 px-4 py-3 text-sm text-ink-500">
                  Chưa có tài khoản đang hoạt động trong database.
                </div>
              ) : null}

              {loginAccounts.map((account) => {
                const isSelected = email === account.email;

                return (
                  <Button
                    key={account.userId}
                    variant={isSelected ? "primary" : "secondary"}
                    className="w-full justify-between text-left"
                    onClick={() => handleSelectDatabaseAccount(account)}
                  >
                    <span>
                      {account.name} • {getAccountRoleLabel(account.role)}
                    </span>
                    <span className="text-xs font-medium opacity-90">{account.email}</span>
                  </Button>
                );
              })}
            </div>
          </div>
        </Card>
      </div>
    </main>
  );
};
