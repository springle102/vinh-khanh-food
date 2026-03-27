import { Outlet } from "react-router-dom";
import { useState } from "react";
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
          <Outlet />
        </main>
      </div>
    </div>
  );
};
