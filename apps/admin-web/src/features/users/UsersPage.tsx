import { useMemo, useState, type FormEvent } from "react";
import { Button } from "../../components/ui/Button";
import { Card } from "../../components/ui/Card";
import { DataTable, type DataColumn } from "../../components/ui/DataTable";
import { Input } from "../../components/ui/Input";
import { Modal } from "../../components/ui/Modal";
import { Select } from "../../components/ui/Select";
import { StatusBadge } from "../../components/ui/StatusBadge";
import { useAdminData } from "../../data/store";
import type { AdminUser } from "../../data/types";
import { preventImplicitFormSubmit } from "../../lib/forms";
import { getPoiTitle } from "../../lib/selectors";
import { formatDateTime, getInitials, roleLabels } from "../../lib/utils";
import { useAuth } from "../auth/AuthContext";

type UserForm = {
  id?: string;
  name: string;
  email: string;
  phone: string;
  role: AdminUser["role"];
  password: string;
  status: AdminUser["status"];
  avatarColor: string;
  managedPoiId: string;
};

const defaultUserForm: UserForm = {
  name: "",
  email: "",
  phone: "",
  role: "PLACE_OWNER",
  password: "",
  status: "active",
  avatarColor: "",
  managedPoiId: "",
};

export const UsersPage = () => {
  const { state, isBootstrapping, saveUser } = useAdminData();
  const { user } = useAuth();
  const [modalOpen, setModalOpen] = useState(false);
  const [form, setForm] = useState<UserForm>(defaultUserForm);

  const canManageUsers = user?.role === "SUPER_ADMIN";
  const isSelfService = user?.role === "PLACE_OWNER";
  const visibleUsers = useMemo(
    () => (isSelfService && user ? state.users.filter((account) => account.id === user.id) : state.users),
    [isSelfService, state.users, user],
  );
  const ownerAccounts = useMemo(
    () => visibleUsers.filter((account) => account.role === "PLACE_OWNER"),
    [visibleUsers],
  );

  const openModal = (account?: AdminUser) => {
    setForm(
      account
        ? {
            id: account.id,
            name: account.name,
            email: account.email,
            phone: account.phone,
            role: account.role,
            password: account.password,
            status: account.status,
            avatarColor: account.avatarColor,
            managedPoiId: account.managedPoiId ?? "",
          }
        : defaultUserForm,
    );
    setModalOpen(true);
  };

  const handleSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!user || (!canManageUsers && !isSelfService)) {
      return;
    }

    await saveUser(
      {
        ...form,
        role: isSelfService ? user.role : form.role,
        status: isSelfService ? user.status : form.status,
        managedPoiId: isSelfService
          ? user.managedPoiId
          : form.role === "PLACE_OWNER"
            ? form.managedPoiId || null
            : null,
      },
      user,
    );
    setModalOpen(false);
  };

  const columns: DataColumn<AdminUser>[] = [
    {
      key: "user",
      header: "Tài khoản",
      render: (account) => (
        <div className="flex items-center gap-3">
          <div
            className="flex h-12 w-12 items-center justify-center rounded-2xl text-sm font-bold text-white"
            style={{ background: account.avatarColor || "#f97316" }}
          >
            {getInitials(account.name)}
          </div>
          <div>
            <p className="font-semibold text-ink-900">{account.name}</p>
            <p className="mt-1 text-sm text-ink-500">{account.email}</p>
          </div>
        </div>
      ),
    },
    {
      key: "role",
      header: "Vai trò",
      render: (account) => (
        <div>
          <p className="font-medium text-ink-800">{roleLabels[account.role]}</p>
          <p className="mt-1 text-xs text-ink-500">{account.phone}</p>
        </div>
      ),
    },
    {
      key: "poi",
      header: "POI phụ trách",
      render: (account) => (
        <p className="text-sm text-ink-600">
          {account.role === "SUPER_ADMIN" ? "Toàn hệ thống" : getPoiTitle(state, account.managedPoiId ?? "")}
        </p>
      ),
    },
    {
      key: "status",
      header: "Trạng thái",
      render: (account) => (
        <StatusBadge
          status={account.status === "locked" ? "suspended" : "active"}
          label={account.status === "locked" ? "Đã khóa" : "Đang hoạt động"}
        />
      ),
    },
    {
      key: "activity",
      header: "Lần đăng nhập cuối",
      render: (account) => <p className="text-sm text-ink-600">{formatDateTime(account.lastLoginAt)}</p>,
    },
    {
      key: "actions",
      header: "Thao tác",
      render: (account) => (
        <Button
          variant="secondary"
          onClick={() => openModal(account)}
          disabled={!canManageUsers && account.id !== user?.id}
        >
          {canManageUsers ? "Chỉnh sửa" : "Cập nhật hồ sơ"}
        </Button>
      ),
    },
  ];

  return (
    <div className="space-y-6">
      <Card>
        <div className="flex flex-col gap-4 xl:flex-row xl:items-end xl:justify-between">
          <div className="max-w-3xl">
            <p className="text-sm font-semibold uppercase tracking-[0.25em] text-primary-600">
              {canManageUsers ? "Quản trị tài khoản chủ quán" : "Hồ sơ tài khoản"}
            </p>
            <h1 className="mt-3 text-3xl font-bold text-ink-900">
              {canManageUsers ? "Quản lý Super Admin và tài khoản chủ quán" : "Cập nhật hồ sơ chủ quán"}
            </h1>
            <p className="mt-3 text-sm leading-6 text-ink-500">
              {canManageUsers
                ? "Đây là khu vực riêng để quản lý tài khoản admin và chủ quán. End-user mobile được tách sang trang riêng trên sidebar."
                : "Chủ quán chỉ được cập nhật hồ sơ, mật khẩu và thông tin liên hệ của chính mình."}
            </p>
          </div>
          <Button onClick={() => openModal(isSelfService ? user ?? undefined : undefined)} disabled={isBootstrapping || (!canManageUsers && !isSelfService)}>
            {isBootstrapping ? "Đang tải dữ liệu..." : canManageUsers ? "Thêm tài khoản chủ quán" : "Chỉnh sửa hồ sơ"}
          </Button>
        </div>
      </Card>

      <section className="grid gap-4 md:grid-cols-3">
        <Card>
          <p className="text-sm text-ink-500">{canManageUsers ? "Tổng chủ quán" : "Vai trò"}</p>
          <p className="mt-2 text-3xl font-bold text-ink-900">{ownerAccounts.length}</p>
        </Card>
        <Card>
          <p className="text-sm text-ink-500">{canManageUsers ? "Đang hoạt động" : "Trạng thái"}</p>
          <p className="mt-2 text-3xl font-bold text-ink-900">
            {canManageUsers
              ? ownerAccounts.filter((account) => account.status === "active").length
              : user?.status === "active"
                ? "Hoạt động"
                : "Đã khóa"}
          </p>
        </Card>
        <Card>
          <p className="text-sm text-ink-500">{canManageUsers ? "Đã khóa" : "POI phụ trách"}</p>
          <p className="mt-2 text-3xl font-bold text-ink-900">
            {canManageUsers
              ? ownerAccounts.filter((account) => account.status === "locked").length
              : getPoiTitle(state, user?.managedPoiId ?? "") || "--"}
          </p>
        </Card>
      </section>

      {!canManageUsers && !isSelfService ? (
        <Card className="border border-amber-100 bg-amber-50">
          <p className="font-semibold text-amber-800">Tài khoản hiện tại không có quyền quản lý tài khoản admin.</p>
          <p className="mt-2 text-sm text-amber-700">
            Hãy đăng nhập bằng Super Admin để cấp quyền hoặc khóa tài khoản chủ quán.
          </p>
        </Card>
      ) : null}

      <Card>
        <DataTable data={visibleUsers} columns={columns} rowKey={(row) => row.id} />
      </Card>

      <Modal
        open={modalOpen}
        onClose={() => setModalOpen(false)}
        title={
          canManageUsers
            ? form.id
              ? "Cập nhật tài khoản admin"
              : "Tạo tài khoản chủ quán"
            : "Cập nhật hồ sơ chủ quán"
        }
        description={
          canManageUsers
            ? "Super Admin có thể gán mỗi tài khoản PLACE_OWNER cho một POI cụ thể trong hệ thống."
            : "Bạn chỉ có thể cập nhật hồ sơ và mật khẩu của chính mình."
        }
      >
        <form
          className="space-y-5"
          onSubmit={(event) => void handleSubmit(event)}
          onKeyDown={preventImplicitFormSubmit}
          autoComplete="off"
        >
          <div className="grid gap-5 md:grid-cols-2">
            <div>
              <label className="field-label">Họ tên</label>
              <Input
                value={form.name}
                onChange={(event) => setForm((current) => ({ ...current, name: event.target.value }))}
                required
              />
            </div>
            <div>
              <label className="field-label">Email</label>
              <Input
                type="email"
                value={form.email}
                onChange={(event) => setForm((current) => ({ ...current, email: event.target.value }))}
                required
              />
            </div>
            <div>
              <label className="field-label">Số điện thoại</label>
              <Input
                value={form.phone}
                onChange={(event) => setForm((current) => ({ ...current, phone: event.target.value }))}
              />
            </div>
            <div>
              <label className="field-label">Mật khẩu</label>
              <Input
                type="password"
                value={form.password}
                autoComplete="new-password"
                onChange={(event) => setForm((current) => ({ ...current, password: event.target.value }))}
              />
            </div>
            <div>
              <label className="field-label">Vai trò</label>
              <Select
                value={form.role}
                onChange={(event) =>
                  setForm((current) => ({ ...current, role: event.target.value as AdminUser["role"] }))
                }
                disabled={isSelfService}
              >
                <option value="SUPER_ADMIN">Super Admin</option>
                <option value="PLACE_OWNER">Chủ quán</option>
              </Select>
            </div>
            <div>
              <label className="field-label">Trạng thái</label>
              <Select
                value={form.status}
                onChange={(event) =>
                  setForm((current) => ({ ...current, status: event.target.value as AdminUser["status"] }))
                }
                disabled={isSelfService}
              >
                <option value="active">Đang hoạt động</option>
                <option value="locked">Đã khóa</option>
              </Select>
            </div>
            <div className="md:col-span-2">
              <label className="field-label">POI phụ trách</label>
              <Select
                value={form.managedPoiId}
                onChange={(event) => setForm((current) => ({ ...current, managedPoiId: event.target.value }))}
                disabled={isSelfService || form.role === "SUPER_ADMIN"}
              >
                <option value="">Chưa gán POI</option>
                {state.pois.map((poi) => (
                  <option key={poi.id} value={poi.id}>
                    {getPoiTitle(state, poi.id)}
                  </option>
                ))}
              </Select>
            </div>
          </div>
          <div>
            <label className="field-label">Màu avatar</label>
            <Input
              value={form.avatarColor}
              onChange={(event) => setForm((current) => ({ ...current, avatarColor: event.target.value }))}
            />
          </div>
          <div className="flex justify-end gap-3 border-t border-sand-100 pt-5">
            <Button variant="ghost" onClick={() => setModalOpen(false)}>
              Hủy
            </Button>
            <Button type="submit" disabled={!canManageUsers && !isSelfService}>
              {canManageUsers ? "Lưu tài khoản" : "Lưu hồ sơ"}
            </Button>
          </div>
        </form>
      </Modal>
    </div>
  );
};
