import { useState } from "react";
import { Button } from "../../components/ui/Button";
import { Card } from "../../components/ui/Card";
import { DataTable, type DataColumn } from "../../components/ui/DataTable";
import { EmptyState } from "../../components/ui/EmptyState";
import { Modal } from "../../components/ui/Modal";
import { StatusBadge } from "../../components/ui/StatusBadge";
import { useAdminData } from "../../data/store";
import type { CustomerUser, EndUserProfile } from "../../data/types";
import { adminApi, getErrorMessage } from "../../lib/api";
import { formatDateTime } from "../../lib/utils";

const getCustomerIdentifier = (customer: CustomerUser) =>
  customer.name?.trim() || customer.username?.trim() || customer.email?.trim() || customer.id;

const getAccountTypeLabel = (customer: CustomerUser | null) =>
  customer?.isPremium ? "Premium" : "Free";

export const EndUsersPage = () => {
  const { state } = useAdminData();
  const [detailModalOpen, setDetailModalOpen] = useState(false);
  const [detailLoading, setDetailLoading] = useState(false);
  const [detailError, setDetailError] = useState<string | null>(null);
  const [selectedCustomerId, setSelectedCustomerId] = useState<string | null>(null);
  const [selectedCustomer, setSelectedCustomer] = useState<EndUserProfile | null>(null);

  const endUsers = state.customerUsers;
  const selectedCustomerSummary = endUsers.find((item) => item.id === selectedCustomerId) ?? null;
  const premiumCount = endUsers.filter((item) => item.isPremium).length;

  const loadEndUserDetails = async (customerId: string) => {
    setSelectedCustomerId(customerId);
    setDetailLoading(true);
    setDetailError(null);

    try {
      const customer = await adminApi.getEndUser(customerId);
      setSelectedCustomer(customer);
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
      key: "createdAt",
      header: "Ngày tạo",
      render: (customer) => <p className="text-sm text-ink-600">{formatDateTime(customer.createdAt)}</p>,
    },
    {
      key: "actions",
      header: "Thao tác",
      render: (customer) => (
        <Button variant="secondary" onClick={() => void openEndUserDetails(customer.id)}>
          Chi tiết
        </Button>
      ),
    },
  ];

  return (
    <div className="space-y-6">
      <Card>
        <div className="max-w-3xl">
          <p className="text-sm font-semibold uppercase tracking-[0.25em] text-primary-600">
            Quản trị người dùng cuối
          </p>
          <h1 className="mt-3 text-3xl font-bold text-ink-900">Quản lý tài khoản end-user của mobile app</h1>
          <p className="mt-3 text-sm leading-6 text-ink-500">
            Theo dõi thông tin đăng nhập, ngôn ngữ và gói tài khoản của end-user trên mobile app.
          </p>
        </div>
      </Card>

      <section className="grid gap-4 md:grid-cols-2">
        <Card>
          <p className="text-sm text-ink-500">Tổng end-user</p>
          <p className="mt-2 text-3xl font-bold text-ink-900">{endUsers.length}</p>
        </Card>
        <Card>
          <p className="text-sm text-ink-500">Tài khoản Premium</p>
          <p className="mt-2 text-3xl font-bold text-primary-700">{premiumCount}</p>
        </Card>
      </section>

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
        description="Thông tin tài khoản hiện có của end-user."
        maxWidthClassName="max-w-5xl"
      >
        {detailLoading ? (
          <p className="text-sm text-ink-500">Đang tải chi tiết từ backend...</p>
        ) : detailError ? (
          <EmptyState title="Không tải được chi tiết" description={detailError} />
        ) : selectedCustomer ? (
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
              <p className="text-sm text-ink-500">Ngày tạo</p>
              <p className="mt-2 font-semibold text-ink-900">{formatDateTime(selectedCustomer.createdAt)}</p>
            </Card>
            <Card>
              <p className="text-sm text-ink-500">Hoạt động lần cuối</p>
              <p className="mt-2 font-semibold text-ink-900">{formatDateTime(selectedCustomer.lastActiveAt)}</p>
            </Card>
          </section>
        ) : (
          <EmptyState title="Chưa chọn người dùng" description="Hãy chọn một end-user trong bảng để xem chi tiết." />
        )}
      </Modal>
    </div>
  );
};
