import { useMemo, useState, type FormEvent } from "react";
import { Button } from "../../components/ui/Button";
import { Card } from "../../components/ui/Card";
import { DataTable, type DataColumn } from "../../components/ui/DataTable";
import { Input, Textarea } from "../../components/ui/Input";
import { Modal } from "../../components/ui/Modal";
import { Select } from "../../components/ui/Select";
import { StatusBadge } from "../../components/ui/StatusBadge";
import { useAdminData } from "../../data/store";
import type { AdminUser } from "../../data/types";
import { adminApi, getErrorMessage } from "../../lib/api";
import { preventImplicitFormSubmit } from "../../lib/forms";
import { getPoiTitle } from "../../lib/selectors";
import { approvalStatusLabels, formatDateTime, getInitials, roleLabels } from "../../lib/utils";
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
  role: "SUPER_ADMIN",
  password: "",
  status: "active",
  avatarColor: "",
  managedPoiId: "",
};

const getOwnerRegistrationQueue = (users: AdminUser[]) =>
  users
    .filter((account) => account.role === "PLACE_OWNER" && account.approvalStatus !== "approved")
    .sort((left, right) =>
      (right.registrationSubmittedAt ?? right.createdAt).localeCompare(
        left.registrationSubmittedAt ?? left.createdAt,
      ),
    );

const getRegistrationStatusLabel = (status: AdminUser["approvalStatus"]) =>
  status === "rejected" ? "Không duyệt" : approvalStatusLabels[status];

const getRegistrationReviewNote = (registration: AdminUser) => {
  if (registration.rejectionReason) {
    return registration.rejectionReason;
  }

  return registration.approvalStatus === "pending"
    ? "Đang chờ super admin xử lý."
    : "Hồ sơ đã không duyệt.";
};

export const UsersPage = () => {
  const { state, isBootstrapping, refreshData, saveUser, saveUserStatus } = useAdminData();
  const { user } = useAuth();
  const [modalOpen, setModalOpen] = useState(false);
  const [registrationQueueOpen, setRegistrationQueueOpen] = useState(false);
  const [form, setForm] = useState<UserForm>(defaultUserForm);
  const [statusSavingUserId, setStatusSavingUserId] = useState<string | null>(null);
  const [statusDraft, setStatusDraft] = useState<{
    userId: string;
    status: AdminUser["status"];
  } | null>(null);
  const [statusError, setStatusError] = useState("");
  const [selectedRegistrationId, setSelectedRegistrationId] = useState<string | null>(null);
  const [rejectReason, setRejectReason] = useState("");
  const [decisionError, setDecisionError] = useState("");
  const [submittingAction, setSubmittingAction] = useState<"approve" | "reject" | null>(null);

  const canManageUsers = user?.role === "SUPER_ADMIN";
  const isSelfService = user?.role === "PLACE_OWNER";
  const visibleUsers = useMemo(() => {
    if (isSelfService && user) {
      return state.users.filter((account) => account.id === user.id);
    }

    if (canManageUsers && user) {
      return state.users.filter(
        (account) =>
          account.id === user.id ||
          (account.role === "PLACE_OWNER" && account.approvalStatus === "approved"),
      );
    }

    return [];
  }, [canManageUsers, isSelfService, state.users, user]);
  const ownerAccounts = useMemo(
    () => visibleUsers.filter((account) => account.role === "PLACE_OWNER"),
    [visibleUsers],
  );
  const registrationQueue = useMemo(
    () => (canManageUsers ? getOwnerRegistrationQueue(state.users) : []),
    [canManageUsers, state.users],
  );
  const selectedRegistration = useMemo(
    () => registrationQueue.find((account) => account.id === selectedRegistrationId) ?? registrationQueue[0] ?? null,
    [registrationQueue, selectedRegistrationId],
  );
  const pendingCount = registrationQueue.filter((account) => account.approvalStatus === "pending").length;
  const rejectedCount = registrationQueue.filter((account) => account.approvalStatus === "rejected").length;

  const openModal = (account?: AdminUser) => {
    if (!account) {
      return;
    }

    if (canManageUsers && account.role === "PLACE_OWNER") {
      return;
    }

    setForm({
      id: account.id,
      name: account.name,
      email: account.email,
      phone: account.phone,
      role: account.role,
      password: account.password,
      status: account.status,
      avatarColor: account.avatarColor,
      managedPoiId: account.managedPoiId ?? "",
    });
    setModalOpen(true);
  };

  const handleOpenProfile = () => {
    if (!user) {
      return;
    }

    openModal(user);
  };

  const handleOwnerStatusChange = async (
    account: AdminUser,
    nextStatus: AdminUser["status"],
  ) => {
    if (!user || !canManageUsers || account.role !== "PLACE_OWNER" || account.status === nextStatus) {
      return;
    }

    setStatusError("");
    setStatusDraft({
      userId: account.id,
      status: nextStatus,
    });
    setStatusSavingUserId(account.id);

    try {
      await saveUserStatus(account.id, nextStatus, user);
    } catch (error) {
      setStatusError(getErrorMessage(error));
    } finally {
      setStatusSavingUserId(null);
      setStatusDraft(null);
    }
  };

  const openRegistrationQueue = () => {
    setRegistrationQueueOpen(true);
    setDecisionError("");
    setSubmittingAction(null);
    setSelectedRegistrationId(registrationQueue[0]?.id ?? null);
    setRejectReason(registrationQueue[0]?.rejectionReason ?? "");
  };

  const closeRegistrationQueue = () => {
    setRegistrationQueueOpen(false);
    setSelectedRegistrationId(null);
    setRejectReason("");
    setDecisionError("");
    setSubmittingAction(null);
  };

  const handleSelectRegistration = (registration: AdminUser) => {
    setSelectedRegistrationId(registration.id);
    setRejectReason(registration.rejectionReason ?? "");
    setDecisionError("");
  };

  const syncRegistrationQueueSelection = async (preferredId: string) => {
    const nextState = await refreshData();
    const nextQueue = getOwnerRegistrationQueue(nextState.users);
    const nextSelected = nextQueue.find((account) => account.id === preferredId) ?? nextQueue[0] ?? null;

    setSelectedRegistrationId(nextSelected?.id ?? null);
    setRejectReason(nextSelected?.rejectionReason ?? "");
    return nextState;
  };

  const handleApproveRegistration = async () => {
    if (!selectedRegistration) {
      return;
    }

    setDecisionError("");
    setSubmittingAction("approve");

    try {
      await adminApi.approvePlaceOwnerRegistration(selectedRegistration.id);
      await syncRegistrationQueueSelection(selectedRegistration.id);
    } catch (error) {
      setDecisionError(getErrorMessage(error));
    } finally {
      setSubmittingAction(null);
    }
  };

  const handleRejectRegistration = async () => {
    if (!selectedRegistration) {
      return;
    }

    if (!rejectReason.trim()) {
      setDecisionError("Hãy nhập rõ lý do không duyệt trước khi lưu.");
      return;
    }

    setDecisionError("");
    setSubmittingAction("reject");

    try {
      await adminApi.rejectPlaceOwnerRegistration(selectedRegistration.id, rejectReason.trim());
      await syncRegistrationQueueSelection(selectedRegistration.id);
    } catch (error) {
      setDecisionError(getErrorMessage(error));
    } finally {
      setSubmittingAction(null);
    }
  };

  const handleSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!user || (!canManageUsers && !isSelfService)) {
      return;
    }

    await saveUser(
      {
        ...form,
        role: canManageUsers ? "SUPER_ADMIN" : user.role,
        status: canManageUsers ? form.status : user.status,
        managedPoiId: isSelfService ? user.managedPoiId : null,
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
          {account.role === "SUPER_ADMIN"
            ? "Toàn hệ thống"
            : getPoiTitle(state, account.managedPoiId ?? "") || "Chưa gán POI"}
        </p>
      ),
    },
    {
      key: "status",
      header: "Trạng thái hoạt động",
      render: (account) =>
        canManageUsers && account.role === "PLACE_OWNER" ? (
          <div>
            <Select
              value={statusDraft?.userId === account.id ? statusDraft.status : account.status}
              className="min-w-[180px]"
              disabled={statusSavingUserId !== null}
              onChange={(event) =>
                void handleOwnerStatusChange(account, event.target.value as AdminUser["status"])
              }
            >
              <option value="active">Đang hoạt động</option>
              <option value="locked">Đã khóa</option>
            </Select>
          </div>
        ) : (
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
      render: (account) =>
        canManageUsers && account.role === "PLACE_OWNER" ? (
          <p className="max-w-[220px] text-sm text-ink-500">Hồ sơ chủ quán mới được xử lý ở nút danh sách bên trên.</p>
        ) : (
          <Button
            variant="secondary"
            onClick={() => openModal(account)}
            disabled={account.id !== user?.id}
          >
            Cập nhật hồ sơ
          </Button>
        ),
    },
  ];

  const registrationColumns: DataColumn<AdminUser>[] = [
    {
      key: "user",
      header: "Chủ quán mới",
      render: (registration) => (
        <div>
          <p className="font-semibold text-ink-900">{registration.name}</p>
          <p className="mt-1 text-sm text-ink-500">{registration.email}</p>
        </div>
      ),
    },
    {
      key: "submittedAt",
      header: "Ngày gửi",
      render: (registration) => (
        <div>
          <p className="text-sm font-medium text-ink-700">
            {formatDateTime(registration.registrationSubmittedAt ?? registration.createdAt)}
          </p>
          <p className="mt-1 text-xs text-ink-500">{registration.phone}</p>
        </div>
      ),
    },
    {
      key: "approvalStatus",
      header: "Kết quả",
      render: (registration) => (
        <StatusBadge
          status={registration.approvalStatus}
          label={getRegistrationStatusLabel(registration.approvalStatus)}
        />
      ),
    },
    {
      key: "reason",
      header: "Ghi chú",
      render: (registration) => (
        <p className="text-sm leading-6 text-ink-600">{getRegistrationReviewNote(registration)}</p>
      ),
    },
    {
      key: "actions",
      header: "Chi tiết",
      render: (registration) => (
        <Button
          variant={selectedRegistration?.id === registration.id ? "primary" : "secondary"}
          onClick={() => handleSelectRegistration(registration)}
        >
          {selectedRegistration?.id === registration.id ? "Đang xem" : "Xem hồ sơ"}
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
              {canManageUsers ? "Quản lý chủ quán" : "Hồ sơ tài khoản"}
            </p>
            <h1 className="mt-3 text-3xl font-bold text-ink-900">
              {canManageUsers ? "Quản lý chủ quán đã duyệt và hồ sơ chủ quán mới" : "Cập nhật hồ sơ chủ quán"}
            </h1>
          </div>
          <Button
            onClick={handleOpenProfile}
            disabled={isBootstrapping || !user}
          >
            {isBootstrapping ? "Đang tải dữ liệu..." : canManageUsers ? "Hồ sơ Super Admin" : "Chỉnh sửa hồ sơ"}
          </Button>
        </div>
      </Card>

      <section className="grid gap-4 md:grid-cols-3">
        <Card>
          <p className="text-sm text-ink-500">{canManageUsers ? "Chủ quán đã duyệt" : "Vai trò"}</p>
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

      {canManageUsers ? (
        <Card className={pendingCount > 0 ? "border border-primary-200 bg-primary-50/40" : undefined}>
          <div className="flex flex-col gap-4 xl:flex-row xl:items-center xl:justify-between">
            <div className="max-w-3xl">
              <p className="text-sm font-semibold uppercase tracking-[0.2em] text-primary-600">Hồ sơ chủ quán mới</p>
              <h2 className="mt-2 text-2xl font-bold text-ink-900">Danh sách chờ duyệt và không duyệt</h2>
              <p className="mt-3 text-sm leading-6 text-ink-500">
                Hiện có {pendingCount} hồ sơ chờ duyệt và {rejectedCount} hồ sơ không duyệt. Mở danh sách để xem chi tiết và ghi rõ lý do không duyệt cho từng hồ sơ.
              </p>
            </div>
            <Button
              onClick={openRegistrationQueue}
              disabled={registrationQueue.length === 0}
            >
              {registrationQueue.length > 0
                ? `Mở danh sách hồ sơ mới (${registrationQueue.length})`
                : "Chưa có hồ sơ mới"}
            </Button>
          </div>
        </Card>
      ) : null}

      {!canManageUsers && !isSelfService ? (
        <Card className="border border-amber-100 bg-amber-50">
          <p className="font-semibold text-amber-800">Tài khoản hiện tại không có quyền quản lý tài khoản admin.</p>
          <p className="mt-2 text-sm text-amber-700">
            Hãy đăng nhập bằng Super Admin để quản lý chủ quán.
          </p>
        </Card>
      ) : null}

      {statusError ? (
        <Card className="border border-rose-100 bg-rose-50">
          <p className="text-sm text-rose-700">{statusError}</p>
        </Card>
      ) : null}

      <Card>
        <div className="mb-5 flex flex-wrap items-center justify-between gap-3">
          <div>
            <h2 className="text-xl font-semibold text-ink-900">
              {canManageUsers ? "Danh sách chủ quán đã duyệt" : "Hồ sơ tài khoản"}
            </h2>
            <p className="mt-1 text-sm text-ink-500">
              {canManageUsers
                ? "Sau khi owner được duyệt, bạn chỉ còn đổi trạng thái hoạt động tại đây."
                : "Bạn chỉ có thể cập nhật hồ sơ của chính mình."}
            </p>
          </div>
        </div>
        <DataTable data={visibleUsers} columns={columns} rowKey={(row) => row.id} />
      </Card>

      <Modal
        open={registrationQueueOpen}
        onClose={closeRegistrationQueue}
        title="Danh sách chủ quán mới"
        description="Gồm các hồ sơ đang chờ duyệt và các hồ sơ không duyệt kèm lý do."
        maxWidthClassName="max-w-6xl"
      >
        {registrationQueue.length === 0 ? (
          <Card>
            <p className="font-semibold text-ink-900">Hiện chưa có hồ sơ chủ quán mới cần xử lý.</p>
            <p className="mt-2 text-sm text-ink-500">
              Khi có owner đăng ký mới hoặc hồ sơ bị không duyệt, danh sách sẽ xuất hiện tại đây.
            </p>
          </Card>
        ) : (
          <div className="space-y-6">
            <section className="grid gap-4 md:grid-cols-2">
              <Card>
                <p className="text-sm text-ink-500">Chờ duyệt</p>
                <p className="mt-2 text-3xl font-bold text-ink-900">{pendingCount}</p>
              </Card>
              <Card>
                <p className="text-sm text-ink-500">Không duyệt</p>
                <p className="mt-2 text-3xl font-bold text-ink-900">{rejectedCount}</p>
              </Card>
            </section>

            <div className="grid gap-6 xl:grid-cols-2">
              <Card>
                <DataTable
                  data={registrationQueue}
                  columns={registrationColumns}
                  rowKey={(row) => row.id}
                  pageSize={5}
                />
              </Card>

              {selectedRegistration ? (
                <div className="space-y-4">
                  <Card>
                    <div className="flex flex-wrap items-start justify-between gap-3">
                      <div>
                        <p className="text-sm font-semibold uppercase tracking-[0.12em] text-primary-600">
                          Hồ sơ đang xem
                        </p>
                        <h3 className="mt-2 text-2xl font-bold text-ink-900">{selectedRegistration.name}</h3>
                        <p className="mt-2 text-sm text-ink-500">{selectedRegistration.email}</p>
                      </div>
                      <StatusBadge
                        status={selectedRegistration.approvalStatus}
                        label={getRegistrationStatusLabel(selectedRegistration.approvalStatus)}
                      />
                    </div>

                    <div className="mt-5 grid gap-4 md:grid-cols-2">
                      <div>
                        <p className="text-sm text-ink-500">Số điện thoại</p>
                        <p className="mt-1 font-medium text-ink-900">{selectedRegistration.phone}</p>
                      </div>
                      <div>
                        <p className="text-sm text-ink-500">Ngày gửi hồ sơ</p>
                        <p className="mt-1 font-medium text-ink-900">
                          {formatDateTime(selectedRegistration.registrationSubmittedAt ?? selectedRegistration.createdAt)}
                        </p>
                      </div>
                      <div>
                        <p className="text-sm text-ink-500">Ngày xử lý</p>
                        <p className="mt-1 font-medium text-ink-900">
                          {formatDateTime(selectedRegistration.registrationReviewedAt)}
                        </p>
                      </div>
                      <div>
                        <p className="text-sm text-ink-500">Trạng thái hoạt động</p>
                        <p className="mt-1 font-medium text-ink-900">
                          {selectedRegistration.status === "active" ? "Đang hoạt động" : "Đã khóa"}
                        </p>
                      </div>
                    </div>
                  </Card>

                  {selectedRegistration.approvalStatus === "pending" ? (
                    <Card>
                      <label className="field-label">Lý do không duyệt</label>
                      <Textarea
                        value={rejectReason}
                        onChange={(event) => setRejectReason(event.target.value)}
                        placeholder="Nhập rõ lý do nếu không duyệt hồ sơ này"
                      />
                      <p className="mt-2 text-xs text-ink-500">
                        Nội dung này sẽ hiển thị cho chủ quán để họ chỉnh sửa và gửi lại hồ sơ.
                      </p>
                    </Card>
                  ) : (
                    <Card className="border border-rose-100 bg-rose-50">
                      <p className="text-sm font-semibold tracking-[0.12em] text-rose-600">Lý do không duyệt</p>
                      <p className="mt-2 text-sm leading-6 text-rose-700">
                        {selectedRegistration.rejectionReason || "Chưa có lý do được ghi nhận."}
                      </p>
                    </Card>
                  )}

                  {decisionError ? (
                    <div className="rounded-2xl bg-rose-50 px-4 py-3 text-sm text-rose-700">{decisionError}</div>
                  ) : null}

                  <div className="rounded-3xl border border-sand-100 bg-sand-50 px-5 py-4 text-sm text-ink-600">
                    {selectedRegistration.approvalStatus === "pending"
                      ? "Hồ sơ này đang chờ super admin quyết định duyệt hoặc không duyệt."
                      : "Hồ sơ này đã không duyệt. Chủ quán có thể sửa thông tin rồi gửi lại từ màn đăng ký."}
                  </div>

                  {selectedRegistration.approvalStatus === "pending" ? (
                    <div className="flex flex-wrap justify-end gap-3">
                      <Button
                        variant="danger"
                        onClick={() => void handleRejectRegistration()}
                        disabled={submittingAction !== null}
                      >
                        {submittingAction === "reject" ? "Đang lưu..." : "Không duyệt"}
                      </Button>
                      <Button
                        onClick={() => void handleApproveRegistration()}
                        disabled={submittingAction !== null}
                      >
                        {submittingAction === "approve" ? "Đang duyệt..." : "Duyệt"}
                      </Button>
                    </div>
                  ) : null}
                </div>
              ) : null}
            </div>
          </div>
        )}
      </Modal>

      <Modal
        open={modalOpen}
        onClose={() => setModalOpen(false)}
        title={canManageUsers ? "Cập nhật hồ sơ Super Admin" : "Cập nhật hồ sơ chủ quán"}
        description={
          canManageUsers
            ? "Hệ thống chỉ có duy nhất một super admin. Bạn chỉ có thể cập nhật hồ sơ và mật khẩu của tài khoản này."
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
            {canManageUsers ? (
              <>
                <div>
                  <label className="field-label">Vai trò</label>
                  <Input value="Super Admin" disabled />
                </div>
                <div>
                  <label className="field-label">Trạng thái</label>
                  <Input value={form.status === "active" ? "Đang hoạt động" : "Đã khóa"} disabled />
                </div>
              </>
            ) : (
              <>
                <div>
                  <label className="field-label">Vai trò</label>
                  <Input value={roleLabels[user?.role ?? "PLACE_OWNER"]} disabled />
                </div>
                <div>
                  <label className="field-label">POI phụ trách</label>
                  <Input value={getPoiTitle(state, user?.managedPoiId ?? "") || "Chưa gán POI"} disabled />
                </div>
              </>
            )}
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
              Lưu hồ sơ
            </Button>
          </div>
        </form>
      </Modal>
    </div>
  );
};
