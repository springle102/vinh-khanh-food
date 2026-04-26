import { Outlet } from "react-router-dom";
import { useState } from "react";
import { useAdminData } from "../../data/store";
import { Button } from "../ui/Button";
import type { IconName } from "../ui/Icons";
import { Sidebar } from "./Sidebar";
import { Topbar } from "./Topbar";

export type AppShellNavItem = {
  to: string;
  label: string;
  icon: IconName;
};

type AppShellProps = {
  brandKicker: string;
  brandTitle: string;
  headerEyebrow: string;
  headerTitle: string;
  navigationItems: AppShellNavItem[];
};

export const AppShell = ({
  brandKicker,
  brandTitle,
  headerEyebrow,
  headerTitle,
  navigationItems,
}: AppShellProps) => {
  const [sidebarOpen, setSidebarOpen] = useState(false);
  const { bootstrapError, isRefreshing, refreshData } = useAdminData();

  const handleRetryBootstrap = () => {
    void refreshData().catch(() => undefined);
  };

  return (
    <div className="min-h-screen bg-sand-50 text-ink-900 lg:grid lg:grid-cols-[288px_minmax(0,1fr)]">
      <Sidebar
        brandKicker={brandKicker}
        brandTitle={brandTitle}
        navigationItems={navigationItems}
        open={sidebarOpen}
        onClose={() => setSidebarOpen(false)}
      />
      <div className="min-w-0">
        <Topbar
          headerEyebrow={headerEyebrow}
          headerTitle={headerTitle}
          onMenuClick={() => setSidebarOpen(true)}
        />
        <main className="p-4 sm:p-6">
          {bootstrapError ? (
            <div className="mb-4 rounded-[2rem] border border-amber-200 bg-amber-50 px-4 py-4 text-sm text-amber-900 shadow-soft">
              <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
                <div>
                  <p className="font-semibold">Không thể tải dữ liệu từ API.</p>
                  <p className="mt-1">{bootstrapError}</p>
                </div>
                <Button
                  type="button"
                  variant="secondary"
                  onClick={handleRetryBootstrap}
                  disabled={isRefreshing}
                >
                  {isRefreshing ? "Đang tải lại..." : "Thử tải lại"}
                </Button>
              </div>
            </div>
          ) : null}
          <Outlet />
        </main>
      </div>
    </div>
  );
};
