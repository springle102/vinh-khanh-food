import { useMemo, useState } from "react";
import { Button } from "../../components/ui/Button";
import { Card } from "../../components/ui/Card";
import { DataTable, type DataColumn } from "../../components/ui/DataTable";
import { EmptyState } from "../../components/ui/EmptyState";
import { Modal } from "../../components/ui/Modal";
import { StatusBadge } from "../../components/ui/StatusBadge";
import { useAdminData } from "../../data/store";
import type { CustomerUser, EndUserProfile, EndUserStatusCode, EndUserPoiVisit } from "../../data/types";
import { adminApi, getErrorMessage } from "../../lib/api";
import { endUserStatusBadgeTone, endUserStatusLabels, formatDateTime, resolveEndUserStatus } from "../../lib/utils";
import { useAuth } from "../auth/AuthContext";

type ConfirmAction = {
  customer: CustomerUser;
  nextIsBanned: boolean;
  title: string;
  message: string;
  confirmLabel: string;
  buttonVariant: "danger" | "secondary";
};

const getCustomerIdentifier = (customer: CustomerUser) =>
  customer.username?.trim() || customer.deviceId?.trim() || customer.name || customer.id;

const mapProfileToCustomer = (customer: EndUserProfile, totalScans: number): CustomerUser => ({
  id: customer.id,
  name: customer.username ?? customer.deviceId ?? customer.id,
  email: "",
  phone: "",
  status: customer.status,
  isActive: customer.isActive,
  isBanned: customer.isBanned,
  preferredLanguage: customer.defaultLanguage,
  isPremium: false,
  totalScans,
  favoritePoiIds: [],
  createdAt: customer.createdAt,
  lastActiveAt: customer.lastActiveAt,
  username: customer.username,
  deviceId: customer.deviceId,
  country: customer.country,
  deviceType: customer.deviceType,
});

export const EndUsersPage = () => {
  const { state, saveCustomerUserStatus } = useAdminData();
  const { user } = useAuth();
  const [detailModalOpen, setDetailModalOpen] = useState(false);
  const [detailLoading, setDetailLoading] = useState(false);
  const [detailError, setDetailError] = useState<string | null>(null);
  const [selectedCustomerId, setSelectedCustomerId] = useState<string | null>(null);
  const [selectedCustomer, setSelectedCustomer] = useState<EndUserProfile | null>(null);
  const [selectedHistory, setSelectedHistory] = useState<EndUserPoiVisit[]>([]);
  const [confirmAction, setConfirmAction] = useState<ConfirmAction | null>(null);
  const [submittingUserId, setSubmittingUserId] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);

  const canManageEndUsers = user?.role === "SUPER_ADMIN";
  const endUsers = useMemo(() => state.customerUsers, [state.customerUsers]);

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

    try {
      const [customer, history] = await Promise.all([
        adminApi.getEndUser(customerId),
        adminApi.getEndUserHistory(customerId),
      ]);

      setSelectedCustomer(customer);
      setSelectedHistory(history);
    } catch (error) {
      setDetailError(getErrorMessage(error));
      setSelectedCustomer(null);
      setSelectedHistory([]);
    } finally {
      setDetailLoading(false);
    }
  };

  const openEndUserDetails = async (customerId: string) => {
    setDetailModalOpen(true);
    await loadEndUserDetails(customerId);
  };

  const openConfirmAction = (customer: CustomerUser) => {
    setActionError(null);
    setConfirmAction(
      customer.isBanned
        ? {
            customer,
            nextIsBanned: false,
            title: "Khôi phục tài khoản",
            message: "Bạn có muốn khôi phục tài khoản này không?",
            confirmLabel: "Bỏ ban",
            buttonVariant: "secondary",
          }
        : {
            customer,
            nextIsBanned: true,
            title: "Khóa tài khoản",
            message: "Bạn có chắc muốn khóa tài khoản này không?",
            confirmLabel: "Ban",
            buttonVariant: "danger",
          },
    );
  };

  const handleConfirmedStatusAction = async () => {
    if (!user || !canManageEndUsers || !confirmAction) {
      return;
    }

    try {
      setSubmittingUserId(confirmAction.customer.id);
      await saveCustomerUserStatus(confirmAction.customer.id, confirmAction.nextIsBanned, user);

      if (selectedCustomerId === confirmAction.customer.id && detailModalOpen) {
        await loadEndUserDetails(confirmAction.customer.id);
      }

      setConfirmAction(null);
      setActionError(null);
    } catch (error) {
      setActionError(getErrorMessage(error));
    } finally {
      setSubmittingUserId(null);
    }
  };

  const columns: DataColumn<CustomerUser>[] = [
    {
      key: "identifier",
      header: "Người dùng",
      render: (customer) => (
        <div>
          <p className="font-semibold text-ink-900">{getCustomerIdentifier(customer)}</p>
          <p className="mt-1 text-sm text-ink-500">{customer.deviceId || customer.email || customer.id}</p>
        </div>
      ),
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
      key: "device",
      header: "Thiết bị",
      render: (customer) => (
        <div>
          <p className="font-medium capitalize text-ink-800">{customer.deviceType ?? "--"}</p>
          <p className="mt-1 text-xs text-ink-500">Hoạt động lần cuối: {formatDateTime(customer.lastActiveAt)}</p>
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
      render: (customer) => {
        const isSubmitting = submittingUserId === customer.id;
        return (
          <div className="flex flex-wrap gap-2">
            <Button variant="secondary" onClick={() => void openEndUserDetails(customer.id)} disabled={isSubmitting}>
              Chi tiết
            </Button>
            <Button
              variant={customer.isBanned ? "secondary" : "danger"}
              onClick={() => openConfirmAction(customer)}
              disabled={!canManageEndUsers || isSubmitting}
            >
              {isSubmitting ? "Đang xử lý..." : customer.isBanned ? "Bỏ ban" : "Ban"}
            </Button>
          </div>
        );
      },
    },
  ];

  const selectedStatus = selectedCustomer ? resolveEndUserStatus(selectedCustomer) : null;
  const selectedCustomerRow =
    selectedCustomer && mapProfileToCustomer(selectedCustomer, selectedHistory.length);
  const isDetailActionSubmitting = selectedCustomer ? submittingUserId === selectedCustomer.id : false;

  return (
    <div className="space-y-6">
      <Card>
        <div className="max-w-3xl">
          <p className="text-sm font-semibold uppercase tracking-[0.25em] text-primary-600">
            Quản trị người dùng cuối
          </p>
          <h1 className="mt-3 text-3xl font-bold text-ink-900">Quản lý end-user của ứng dụng mobile</h1>
          <p className="mt-3 text-sm leading-6 text-ink-500">
            Trang riêng cho user cuối: xem danh sách, chi tiết, lịch sử ghé POI, khóa và mở khóa tài khoản.
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
          <p className="font-semibold text-amber-800">Tài khoản hiện tại chỉ có quyền xem end-user.</p>
          <p className="mt-2 text-sm text-amber-700">
            Đăng nhập bằng Super Admin để ban hoặc bỏ ban tài khoản khi cần.
          </p>
        </Card>
      ) : null}

      <Card>
        {endUsers.length === 0 ? (
          <EmptyState
            title="Chưa có end-user nào"
            description="Khi mobile app ghi nhận lượt ghé POI, danh sách người dùng cuối sẽ hiển thị tại đây."
          />
        ) : (
          <DataTable data={endUsers} columns={columns} rowKey={(row) => row.id} pageSize={8} />
        )}
      </Card>

      <Modal
        open={detailModalOpen}
        onClose={() => setDetailModalOpen(false)}
        title="Chi tiết người dùng"
        description="Thông tin tài khoản và lịch sử ghé POI của end-user."
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
                <p className="text-sm text-ink-500">Username / Device</p>
                <p className="mt-2 font-semibold text-ink-900">
                  {selectedCustomer.username || selectedCustomer.deviceId || "--"}
                </p>
              </Card>
              <Card>
                <p className="text-sm text-ink-500">Ngôn ngữ mặc định</p>
                <p className="mt-2 font-semibold uppercase text-ink-900">{selectedCustomer.defaultLanguage}</p>
              </Card>
              <Card>
                <p className="text-sm text-ink-500">Trạng thái</p>
                <div className="mt-2">
                  <StatusBadge status={endUserStatusBadgeTone[selectedStatus]} label={endUserStatusLabels[selectedStatus]} />
                </div>
              </Card>
              <Card>
                <p className="text-sm text-ink-500">Quốc gia</p>
                <p className="mt-2 font-semibold text-ink-900">{selectedCustomer.country}</p>
              </Card>
              <Card>
                <p className="text-sm text-ink-500">Thiết bị</p>
                <p className="mt-2 font-semibold capitalize text-ink-900">{selectedCustomer.deviceType}</p>
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

            {selectedCustomerRow ? (
              <div className="flex justify-end">
                <Button
                  variant={selectedCustomerRow.isBanned ? "secondary" : "danger"}
                  disabled={!canManageEndUsers || isDetailActionSubmitting}
                  onClick={() => openConfirmAction(selectedCustomerRow)}
                >
                  {isDetailActionSubmitting ? "Đang xử lý..." : selectedCustomerRow.isBanned ? "Bỏ ban" : "Ban"}
                </Button>
              </div>
            ) : null}

            <div>
              <div className="mb-4 flex items-center justify-between">
                <h3 className="text-lg font-semibold text-ink-900">Lịch sử ghé POI</h3>
                <p className="text-sm text-ink-500">{selectedHistory.length} lần ghi nhận</p>
              </div>

              {selectedHistory.length === 0 ? (
                <EmptyState
                  title="Chưa có lịch sử POI"
                  description="User này chưa có bản ghi đến POI nào trong hệ thống."
                />
              ) : (
                <div className="overflow-hidden rounded-3xl border border-sand-100">
                  <div className="overflow-x-auto">
                    <table className="min-w-full divide-y divide-sand-100">
                      <thead className="bg-sand-50/80">
                        <tr>
                          <th className="px-4 py-3 text-left text-xs font-semibold uppercase tracking-wide text-ink-500">
                            POI
                          </th>
                          <th className="px-4 py-3 text-left text-xs font-semibold uppercase tracking-wide text-ink-500">
                            Thời gian
                          </th>
                          <th className="px-4 py-3 text-left text-xs font-semibold uppercase tracking-wide text-ink-500">
                            Ngôn ngữ dịch
                          </th>
                        </tr>
                      </thead>
                      <tbody className="divide-y divide-sand-100 bg-white">
                        {selectedHistory.map((item) => (
                          <tr key={item.id}>
                            <td className="px-4 py-4">
                              <p className="font-medium text-ink-900">{item.poiSlug}</p>
                              <p className="mt-1 text-sm text-ink-500">{item.poiAddress}</p>
                            </td>
                            <td className="px-4 py-4 text-sm text-ink-700">{formatDateTime(item.visitedAt)}</td>
                            <td className="px-4 py-4">
                              <StatusBadge status="active" label={item.translatedLanguage.toUpperCase()} />
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                </div>
              )}
            </div>
          </div>
        ) : (
          <EmptyState title="Chưa chọn người dùng" description="Hãy chọn một end-user trong bảng để xem chi tiết." />
        )}
      </Modal>

      <Modal
        open={confirmAction !== null}
        onClose={() => (submittingUserId ? undefined : setConfirmAction(null))}
        title={confirmAction?.title ?? "Xác nhận thao tác"}
        description={confirmAction?.message}
        maxWidthClassName="max-w-xl"
      >
        {actionError ? (
          <div className="rounded-2xl border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-700">
            {actionError}
          </div>
        ) : null}
        <div className="flex justify-end gap-3">
          <Button variant="ghost" onClick={() => setConfirmAction(null)} disabled={submittingUserId !== null}>
            Hủy
          </Button>
          <Button
            variant={confirmAction?.buttonVariant ?? "secondary"}
            onClick={() => void handleConfirmedStatusAction()}
            disabled={submittingUserId !== null}
          >
            {submittingUserId !== null ? "Đang xử lý..." : confirmAction?.confirmLabel ?? "Xác nhận"}
          </Button>
        </div>
      </Modal>
    </div>
  );
};
