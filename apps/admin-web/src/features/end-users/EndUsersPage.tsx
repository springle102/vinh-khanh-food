import { useMemo, useState } from "react";
import { Button } from "../../components/ui/Button";
import { Card } from "../../components/ui/Card";
import { DataTable, type DataColumn } from "../../components/ui/DataTable";
import { EmptyState } from "../../components/ui/EmptyState";
import { Modal } from "../../components/ui/Modal";
import { Select } from "../../components/ui/Select";
import { StatusBadge } from "../../components/ui/StatusBadge";
import { useAdminData } from "../../data/store";
import type { CustomerStatus, CustomerUser, EndUserProfile, EndUserStatusCode } from "../../data/types";
import { adminApi, getErrorMessage } from "../../lib/api";
import { endUserStatusBadgeTone, endUserStatusLabels, formatDateTime, resolveEndUserStatus } from "../../lib/utils";
import { useAuth } from "../auth/AuthContext";

const getCustomerIdentifier = (customer: CustomerUser) =>
  customer.name?.trim() || customer.username?.trim() || customer.email?.trim() || customer.id;

const getAccountTypeLabel = (customer: CustomerUser | null) =>
  customer?.isPremium ? "Premium" : "Free";

const statusOptions: Array<{ value: CustomerStatus; label: string }> = [
  { value: "active", label: "Đang hoạt động" },
  { value: "inactive", label: "Không hoạt động" },
  { value: "banned", label: "Đã ban" },
];

export const EndUsersPage = () => {
  const { state, saveCustomerUserStatus } = useAdminData();
  const { user } = useAuth();
  const [detailModalOpen, setDetailModalOpen] = useState(false);
  const [detailLoading, setDetailLoading] = useState(false);
  const [detailError, setDetailError] = useState<string | null>(null);
  const [selectedCustomerId, setSelectedCustomerId] = useState<string | null>(null);
  const [selectedCustomer, setSelectedCustomer] = useState<EndUserProfile | null>(null);
  const [statusDraft, setStatusDraft] = useState<CustomerStatus>("active");
  const [submittingUserId, setSubmittingUserId] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);

  const canManageEndUsers = user?.role === "SUPER_ADMIN";
  const endUsers = useMemo(() => state.customerUsers, [state.customerUsers]);
  const selectedCustomerSummary = useMemo(
    () => endUsers.find((item) => item.id === selectedCustomerId) ?? null,
    [endUsers, selectedCustomerId],
  );

  const counts = useMemo(() => {
    return endUsers.reduce(
      (accumulator, customer) => {
        const status = resolveEndUserStatus(customer);
        accumulator[status] += 1;
        return accumulator;
      },
      { ACTIVE: 0, INACTIVE: 0, BANNED: 0 } as Record<EndUserStatusCode, number>,
    );
  }, [endUsers]);

  const loadEndUserDetails = async (customerId: string) => {
    setSelectedCustomerId(customerId);
    setDetailLoading(true);
    setDetailError(null);
    setActionError(null);

    try {
      const customer = await adminApi.getEndUser(customerId);
      setSelectedCustomer(customer);
      setStatusDraft(customer.status);
    } catch (error) {
      setDetailError(getErrorMessage(error));
      setSelectedCustomer(null);
    } finally {
      setDetailLoading(false);
    }
  };

  const openEndUserDetails = async (customerId: string) => {
    setDetailModalOpen(true);
    await loadEndUserDetails(customerId);
  };

  const handleSaveStatus = async () => {
    if (!user || !canManageEndUsers || !selectedCustomer) {
      return;
    }

    try {
      setSubmittingUserId(selectedCustomer.id);
      setActionError(null);
      await saveCustomerUserStatus(selectedCustomer.id, statusDraft, user);
      await loadEndUserDetails(selectedCustomer.id);
    } catch (error) {
      setActionError(getErrorMessage(error));
    } finally {
      setSubmittingUserId(null);
    }
  };

  const columns: DataColumn<CustomerUser>[] = [
    {
      key: "user",
      header: "Người dùng",
      render: (customer) => (
        <div>
          <p className="font-semibold text-ink-900">{getCustomerIdentifier(customer)}</p>
          <p className="mt-1 text-sm text-ink-500">{customer.username?.trim() || "--"}</p>
        </div>
      ),
    },
    {
      key: "email",
      header: "Email",
      render: (customer) => <p className="text-sm text-ink-700">{customer.email || "--"}</p>,
    },
    {
      key: "userId",
      header: "User ID",
      render: (customer) => <p className="font-mono text-sm text-ink-700">{customer.id}</p>,
    },
    {
      key: "accountType",
      header: "Loại tài khoản",
      render: (customer) => (
        <StatusBadge
          status={customer.isPremium ? "active" : "inactive"}
          label={getAccountTypeLabel(customer)}
        />
      ),
    },
    {
      key: "password",
      header: "Mật khẩu",
      render: (customer) => <p className="font-mono text-sm text-ink-700">{customer.password || "--"}</p>,
    },
    {
      key: "locale",
      header: "Ngôn ngữ / Quốc gia",
      render: (customer) => (
        <div>
          <p className="font-medium text-ink-800">{customer.preferredLanguage.toUpperCase()}</p>
          <p className="mt-1 text-xs text-ink-500">{customer.country || "--"}</p>
        </div>
      ),
    },
    {
      key: "status",
      header: "Trạng thái",
      render: (customer) => {
        const status = resolveEndUserStatus(customer);
        return <StatusBadge status={endUserStatusBadgeTone[status]} label={endUserStatusLabels[status]} />;
      },
    },
    {
      key: "createdAt",
      header: "Ngày tạo",
      render: (customer) => <p className="text-sm text-ink-600">{formatDateTime(customer.createdAt)}</p>,
    },
    {
      key: "actions",
      header: "Thao tác",
      render: (customer) => (
        <Button variant="secondary" onClick={() => void openEndUserDetails(customer.id)}>
          Chi tiết / sửa
        </Button>
      ),
    },
  ];

  const selectedStatus = selectedCustomer ? resolveEndUserStatus(selectedCustomer) : null;
  const isDetailActionSubmitting = selectedCustomer ? submittingUserId === selectedCustomer.id : false;

  return (
    <div className="space-y-6">
      <Card>
        <div className="max-w-3xl">
          <p className="text-sm font-semibold uppercase tracking-[0.25em] text-primary-600">
            Quản trị người dùng cuối
          </p>
          <h1 className="mt-3 text-3xl font-bold text-ink-900">Quản lý tài khoản end-user của mobile app</h1>
          <p className="mt-3 text-sm leading-6 text-ink-500">
            Quản lý thông tin đăng nhập end-user và cập nhật trạng thái tài khoản theo từng người dùng.
          </p>
        </div>
      </Card>

      <section className="grid gap-4 md:grid-cols-4">
        <Card>
          <p className="text-sm text-ink-500">Tổng end-user</p>
          <p className="mt-2 text-3xl font-bold text-ink-900">{endUsers.length}</p>
        </Card>
        <Card>
          <p className="text-sm text-ink-500">Đang hoạt động</p>
          <p className="mt-2 text-3xl font-bold text-emerald-700">{counts.ACTIVE}</p>
        </Card>
        <Card>
          <p className="text-sm text-ink-500">Không hoạt động</p>
          <p className="mt-2 text-3xl font-bold text-slate-700">{counts.INACTIVE}</p>
        </Card>
        <Card>
          <p className="text-sm text-ink-500">Đã ban</p>
          <p className="mt-2 text-3xl font-bold text-rose-700">{counts.BANNED}</p>
        </Card>
      </section>

      {!canManageEndUsers ? (
        <Card className="border border-amber-100 bg-amber-50">
          <p className="font-semibold text-amber-800">Tài khoản hiện tại chỉ có quyền xem thông tin end-user.</p>
          <p className="mt-2 text-sm text-amber-700">
            Đăng nhập bằng Super Admin để đổi trạng thái tài khoản khi cần.
          </p>
        </Card>
      ) : null}

      <Card>
        {endUsers.length === 0 ? (
          <EmptyState
            title="Chưa có end-user nào"
            description="Khi mobile app ghi nhận người dùng mới, danh sách tài khoản sẽ hiển thị tại đây."
          />
        ) : (
          <DataTable data={endUsers} columns={columns} rowKey={(row) => row.id} pageSize={8} />
        )}
      </Card>

      <Modal
        open={detailModalOpen}
        onClose={() => setDetailModalOpen(false)}
        title="Chi tiết người dùng"
        description="Thông tin tài khoản và trạng thái hiện tại của end-user."
        maxWidthClassName="max-w-5xl"
      >
        {detailLoading ? (
          <p className="text-sm text-ink-500">Đang tải chi tiết từ backend...</p>
        ) : detailError ? (
          <EmptyState title="Không tải được chi tiết" description={detailError} />
        ) : selectedCustomer && selectedStatus ? (
          <div className="space-y-6">
            <section className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
              <Card>
                <p className="text-sm text-ink-500">User ID</p>
                <p className="mt-2 font-semibold text-ink-900">{selectedCustomer.id}</p>
              </Card>
              <Card>
                <p className="text-sm text-ink-500">Họ tên</p>
                <p className="mt-2 font-semibold text-ink-900">{selectedCustomer.name || "--"}</p>
              </Card>
              <Card>
                <p className="text-sm text-ink-500">Loại tài khoản</p>
                <div className="mt-2">
                  <StatusBadge
                    status={selectedCustomerSummary?.isPremium ? "active" : "inactive"}
                    label={getAccountTypeLabel(selectedCustomerSummary)}
                  />
                </div>
              </Card>
              <Card>
                <p className="text-sm text-ink-500">Username</p>
                <p className="mt-2 font-semibold text-ink-900">{selectedCustomer.username || "--"}</p>
              </Card>
              <Card>
                <p className="text-sm text-ink-500">Email</p>
                <p className="mt-2 font-semibold text-ink-900">{selectedCustomer.email || "--"}</p>
              </Card>
              <Card>
                <p className="text-sm text-ink-500">Mật khẩu</p>
                <p className="mt-2 font-mono font-semibold text-ink-900">{selectedCustomer.password || "--"}</p>
              </Card>
              <Card>
                <p className="text-sm text-ink-500">Số điện thoại</p>
                <p className="mt-2 font-semibold text-ink-900">{selectedCustomer.phone || "--"}</p>
              </Card>
              <Card>
                <p className="text-sm text-ink-500">Ngôn ngữ mặc định</p>
                <p className="mt-2 font-semibold uppercase text-ink-900">{selectedCustomer.defaultLanguage}</p>
              </Card>
              <Card>
                <p className="text-sm text-ink-500">Quốc gia</p>
                <p className="mt-2 font-semibold text-ink-900">{selectedCustomer.country || "--"}</p>
              </Card>
              <Card>
                <p className="text-sm text-ink-500">Trạng thái hiện tại</p>
                <div className="mt-2">
                  <StatusBadge status={endUserStatusBadgeTone[selectedStatus]} label={endUserStatusLabels[selectedStatus]} />
                </div>
              </Card>
              <Card>
                <p className="text-sm text-ink-500">Ngày tạo</p>
                <p className="mt-2 font-semibold text-ink-900">{formatDateTime(selectedCustomer.createdAt)}</p>
              </Card>
              <Card>
                <p className="text-sm text-ink-500">Hoạt động lần cuối</p>
                <p className="mt-2 font-semibold text-ink-900">{formatDateTime(selectedCustomer.lastActiveAt)}</p>
              </Card>
            </section>

            <Card className="border border-sand-100 bg-sand-50/70">
              <div className="flex flex-col gap-4 lg:flex-row lg:items-end lg:justify-between">
                <div className="max-w-2xl">
                  <p className="text-sm font-semibold text-ink-900">Chỉnh sửa trạng thái người dùng</p>
                  <p className="mt-1 text-sm text-ink-500">
                    Dùng trạng thái để tạm ngưng, kích hoạt lại hoặc ban hoàn toàn tài khoản end-user.
                  </p>
                </div>
                <div className="flex flex-col gap-3 sm:flex-row sm:items-end">
                  <div>
                    <label className="field-label">Trạng thái mới</label>
                    <Select
                      value={statusDraft}
                      onChange={(event) => setStatusDraft(event.target.value as CustomerStatus)}
                      disabled={!canManageEndUsers || isDetailActionSubmitting}
                    >
                      {statusOptions.map((option) => (
                        <option key={option.value} value={option.value}>
                          {option.label}
                        </option>
                      ))}
                    </Select>
                  </div>
                  <Button
                    onClick={() => void handleSaveStatus()}
                    disabled={!canManageEndUsers || isDetailActionSubmitting || statusDraft === selectedCustomer.status}
                  >
                    {isDetailActionSubmitting ? "Đang lưu..." : "Lưu trạng thái"}
                  </Button>
                </div>
              </div>
              {actionError ? (
                <div className="mt-4 rounded-2xl border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-700">
                  {actionError}
                </div>
              ) : null}
            </Card>
          </div>
        ) : (
          <EmptyState title="Chưa chọn người dùng" description="Hãy chọn một end-user trong bảng để xem chi tiết." />
        )}
      </Modal>
    </div>
  );
};
