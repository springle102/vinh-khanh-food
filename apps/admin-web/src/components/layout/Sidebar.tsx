import { NavLink } from "react-router-dom";
import { cn } from "../../lib/utils";
import { Icon } from "../ui/Icons";
import type { AppShellNavItem } from "./AppShell";

type SidebarProps = {
  brandKicker: string;
  brandTitle: string;
  navigationItems: AppShellNavItem[];
  open: boolean;
  onClose: () => void;
};

export const Sidebar = ({
  brandKicker,
  brandTitle,
  navigationItems,
  open,
  onClose,
}: SidebarProps) => (
  <>
    <div
      className={cn(
        "fixed inset-0 z-30 bg-ink-900/45 transition lg:hidden",
        open ? "pointer-events-auto opacity-100" : "pointer-events-none opacity-0",
      )}
      onClick={onClose}
    />
    <aside
      className={cn(
        "fixed left-0 top-0 z-40 flex h-screen w-[288px] flex-col border-r border-sand-200 bg-white px-5 py-6 transition lg:sticky lg:translate-x-0",
        open ? "translate-x-0" : "-translate-x-full",
      )}
    >
      <div className="mb-8 flex items-center gap-4">
        <div className="flex h-12 w-12 items-center justify-center rounded-3xl bg-primary-500 text-white shadow-soft">
          <Icon name="dashboard" className="h-6 w-6" />
        </div>
        <div>
          <p className="text-xs font-semibold uppercase tracking-[0.25em] text-primary-600">{brandKicker}</p>
          <h1 className="text-lg font-bold text-ink-900">{brandTitle}</h1>
        </div>
      </div>

      <nav className="space-y-2">
        {navigationItems.map((item) => (
          <NavLink
            key={item.to}
            to={item.to}
            onClick={onClose}
            className={({ isActive }) =>
              cn(
                "flex items-center gap-3 rounded-2xl px-4 py-3 text-sm font-medium transition",
                isActive
                  ? "bg-primary-50 text-primary-700"
                  : "text-ink-600 hover:bg-sand-50 hover:text-ink-900",
              )
            }
          >
            <Icon name={item.icon} className="h-5 w-5" />
            <span>{item.label}</span>
          </NavLink>
        ))}
      </nav>
    </aside>
  </>
);
