import { useEffect, useState } from "react";
import { useLocation, useNavigate } from "react-router-dom";
import { Button } from "../../components/ui/Button";
import { Card } from "../../components/ui/Card";
import { Icon } from "../../components/ui/Icons";
import { Input } from "../../components/ui/Input";
import { useAuth } from "./AuthContext";
import { getHomePathForRole, isPathAllowedForRole } from "./auth-routing";

const demoAccounts = [
  {
    label: "Super Admin",
    email: "superadmin@vinhkhanh.vn",
    password: "Admin@123",
  },
  {
    label: "Chủ quán demo",
    email: "bbq@vinhkhanh.vn",
    password: "Admin@123",
  },
];

export const LoginPage = () => {
  const navigate = useNavigate();
  const location = useLocation();
  const { isInitializing, login, user } = useAuth();
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState("");
  const [submitting, setSubmitting] = useState(false);

  useEffect(() => {
    if (!user) {
      return;
    }

    navigate(getHomePathForRole(user.role), { replace: true });
  }, [navigate, user]);

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

  const handleSelectDemoAccount = (selectedEmail: string, selectedPassword: string) => {
    setEmail(selectedEmail);
    setPassword(selectedPassword);
    setError("");
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
                Login Portal
              </p>
              <h1 className="text-2xl font-bold">Đăng nhập hệ thống nhà hàng</h1>
            </div>
          </div>

          <form className="space-y-5" onSubmit={onSubmit}>
            <div>
              <label className="field-label">Email</label>
              <Input
                type="email"
                value={email}
                onChange={(event) => setEmail(event.target.value)}
                placeholder="Nhập email"
              />
            </div>
            <div>
              <label className="field-label">Mật khẩu</label>
              <Input
                type="password"
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
          </form>

          <div className="mt-6 rounded-3xl bg-sand-50 p-4 text-sm text-ink-500">
            <p className="font-semibold text-ink-700">Tài khoản demo</p>
            <div className="mt-3 grid gap-3">
              {demoAccounts.map((account) => {
                const isSelected = email === account.email && password === account.password;

                return (
                  <Button
                    key={account.email}
                    variant={isSelected ? "primary" : "secondary"}
                    className="w-full justify-between text-left"
                    onClick={() => handleSelectDemoAccount(account.email, account.password)}
                  >
                    <span>{account.label}</span>
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
