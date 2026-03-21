import { useAuth } from "../../features/auth/AuthContext";
import { getInitials, roleLabels } from "../../lib/utils";
import { Button } from "../ui/Button";
import { Icon } from "../ui/Icons";

export const Topbar = ({
  onMenuClick,
}: {
  onMenuClick: () => void;
}) => {
  const { user, logout } = useAuth();

  return (
    <header className="sticky top-0 z-20 border-b border-sand-100 bg-white/90 px-4 py-4 backdrop-blur sm:px-6">
      <div className="flex flex-wrap items-center justify-between gap-4">
        <div className="flex items-center gap-3">
          <Button variant="ghost" className="lg:hidden" onClick={onMenuClick}>
            <Icon name="menu" className="h-5 w-5" />
          </Button>
          <div>
            <p className="text-sm font-medium text-ink-500">Bảng điều khiển quản trị</p>
            <h2 className="text-2xl font-bold text-ink-900">Phố ẩm thực Vĩnh Khánh</h2>
          </div>
        </div>

        <div className="flex flex-1 items-center justify-end gap-3">
          <button
            type="button"
            className="flex h-11 w-11 items-center justify-center rounded-2xl bg-sand-100 text-ink-700"
          >
            <Icon name="bell" className="h-5 w-5" />
          </button>
          {user ? (
            <div className="flex items-center gap-3 rounded-2xl border border-sand-200 bg-white px-3 py-2">
              <div
                className="flex h-11 w-11 items-center justify-center rounded-2xl text-sm font-bold text-white"
                style={{ background: user.avatarColor }}
              >
                {getInitials(user.name)}
              </div>
              <div className="hidden sm:block">
                <p className="text-sm font-semibold text-ink-900">{user.name}</p>
                <p className="text-xs text-ink-500">{roleLabels[user.role]}</p>
              </div>
            </div>
          ) : null}
          <Button
            variant="ghost"
            onClick={() => {
              void logout();
            }}
          >
            <Icon name="logout" className="h-4 w-4" />
            <span className="hidden sm:inline">Đăng xuất</span>
          </Button>
        </div>
      </div>
    </header>
  );
};
